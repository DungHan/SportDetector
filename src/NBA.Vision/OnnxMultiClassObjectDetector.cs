using Microsoft.ML.OnnxRuntime;
using NBA.Inference;

namespace NBA.Vision;

/// <summary>
/// Multi-class object detector backed by a YOLOv8-style object-detection ONNX model - specifically the raw
/// (no-NMS) export shape Ultralytics' own exporter produces (verified against a real `yolov8n.onnx` export:
/// input "images" of shape [1, 3, 640, 640], output "output0" of shape [1, 4 + numClasses, numCandidates] - 4
/// box channels (cx, cy, w, h, in the model's input-pixel coordinate space, i.e. [0, inputSize]) followed by
/// one confidence channel per class, indexed by class - not a combined confidence+classId pair). Every class
/// channel is decoded from the one inference pass this instance wraps - a `playerClassId` channel is routed
/// into <see cref="PlayerDetection"/> (including its upper-body mean-color sampling, a cheap appearance signal
/// consumed by `tracking/player-tracking`'s team-color veto), every other channel into
/// <see cref="OnCourtObjectDetection"/>. `App.axaml.cs` wires up two separate instances of this class, one per
/// trained model (player+referee, ball-only) - a single model no longer covers every on-court/broadcast-overlay
/// class, so the composition root merges each instance's own <see cref="MultiClassDetectionResult"/>. A
/// `playerClassId` of <see langword="null"/> means this particular model has no Player channel at all - every
/// class it decodes lands on <see cref="MultiClassDetectionResult.Others"/> instead, as is the case for a
/// single-class ball-only export. Non-max suppression always runs in postprocessing, independently per class,
/// since this raw export shape has no NMS baked in and one class's boxes must never suppress another's.
/// </summary>
public sealed class OnnxMultiClassObjectDetector : IMultiClassObjectDetector, IDisposable
{
    private readonly int _inputSize;
    private readonly OnnxModelPipeline<
        (byte[] Pixels, int Width, int Height, int Stride),
        (IReadOnlyList<(double X1, double Y1, double X2, double Y2, float Confidence)> Players,
         IReadOnlyList<(double X1, double Y1, double X2, double Y2, float Confidence, string ClassName)> Others)> _pipeline;

    /// <summary>Which ONNX Runtime execution provider this instance's session actually ended up on (see <see cref="ExecutionProviderSelector"/>).</summary>
    public ExecutionProviderKind Provider => _pipeline.Provider;

    public OnnxMultiClassObjectDetector(
        string modelPath,
        IReadOnlyList<string> classNames,
        int inputSize = 640,
        float confidenceThreshold = 0.5f,
        float iouThreshold = 0.45f,
        int? playerClassId = 0,
        string inputName = "images",
        Action<float, int>? onDiagnostics = null)
    {
        _inputSize = inputSize;
        _pipeline = new OnnxModelPipeline<
            (byte[] Pixels, int Width, int Height, int Stride),
            (IReadOnlyList<(double, double, double, double, float)>, IReadOnlyList<(double, double, double, double, float, string)>)>(
            modelPath,
            preprocess: frame =>
            {
                var tensor = ImagePreprocessing.ToNchwTensor(frame.Pixels, frame.Width, frame.Height, frame.Stride, inputSize, inputSize, out _);
                return [NamedOnnxValue.CreateFromTensor(inputName, tensor)];
            },
            postprocess: results =>
            {
                var output = results.First().AsTensor<float>();
                var classCount = output.Dimensions[1] - 4;

                // Guard against a misconfigured/mismatched class index or class-name list (e.g. a model swap
                // that changes the class count) reading past the tensor's actual channel range - fail to zero
                // detections, the same "no model/no class to read" degraded shape the rest of this pipeline
                // already uses, rather than an IndexOutOfRangeException. A null playerClassId is not a
                // misconfiguration - it means this model has no Player channel at all (e.g. a single-class
                // ball-only export), so every class it decodes belongs on the Others list.
                if (classNames.Count != classCount || (playerClassId is { } configuredPlayerClassId && (configuredPlayerClassId < 0 || configuredPlayerClassId >= classCount)))
                {
                    onDiagnostics?.Invoke(0f, 0);
                    return ([], []);
                }

                var candidateCount = output.Dimensions[2];
                var maxConfidenceSeen = 0f;
                var aboveThresholdCount = 0;

                var playerCandidates = new List<(double X1, double Y1, double X2, double Y2, float Confidence)>();
                var otherCandidatesByClass = new List<(double X1, double Y1, double X2, double Y2, float Confidence)>[classCount];
                for (var c = 0; c < classCount; c++)
                {
                    otherCandidatesByClass[c] = [];
                }

                for (var classIndex = 0; classIndex < classCount; classIndex++)
                {
                    var channel = 4 + classIndex;
                    var isPlayerChannel = classIndex == playerClassId;
                    // With no Player channel configured at all, diagnostics track across every class instead
                    // of a specific one - still meaningful here since onDiagnostics is only ever wired up to
                    // log "how strong/how many did this model see", not specifically about Player detections.
                    var isDiagnosticsChannel = playerClassId is null || isPlayerChannel;

                    for (var i = 0; i < candidateCount; i++)
                    {
                        var confidence = output[0, channel, i];

                        if (isDiagnosticsChannel && confidence > maxConfidenceSeen)
                        {
                            maxConfidenceSeen = confidence;
                        }

                        if (confidence < confidenceThreshold)
                        {
                            continue;
                        }

                        if (isDiagnosticsChannel)
                        {
                            aboveThresholdCount++;
                        }

                        var cx = output[0, 0, i];
                        var cy = output[0, 1, i];
                        var w = output[0, 2, i];
                        var h = output[0, 3, i];

                        // Left in letterboxed model-input-pixel space (not normalized to [0,1]) - Detect()
                        // below maps these back to source-frame pixel space via the letterbox transform,
                        // which (unlike a plain /inputSize normalization) accounts for the padding/scale a
                        // non-square source frame gets when resized into this model's square input.
                        var box = (
                            (double)(cx - (w / 2)),
                            (double)(cy - (h / 2)),
                            (double)(cx + (w / 2)),
                            (double)(cy + (h / 2)),
                            confidence);

                        if (isPlayerChannel)
                        {
                            playerCandidates.Add(box);
                        }
                        else
                        {
                            otherCandidatesByClass[classIndex].Add(box);
                        }
                    }
                }

                onDiagnostics?.Invoke(maxConfidenceSeen, aboveThresholdCount);

                var players = SuppressOverlapping(playerCandidates, iouThreshold);

                var others = new List<(double, double, double, double, float, string)>();
                for (var classIndex = 0; classIndex < classCount; classIndex++)
                {
                    if (classIndex == playerClassId)
                    {
                        continue;
                    }

                    var suppressed = SuppressOverlapping(otherCandidatesByClass[classIndex], iouThreshold);
                    others.AddRange(suppressed.Select(c => (c.X1, c.Y1, c.X2, c.Y2, c.Confidence, classNames[classIndex])));
                }

                return (players, others);
            });
    }

    public MultiClassDetectionResult Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        var raw = _pipeline.Run((bgra8Pixels.ToArray(), width, height, stride)).Output;
        var transform = LetterboxTransform.Compute(width, height, _inputSize, _inputSize);

        var players = new List<PlayerDetection>(raw.Players.Count);
        foreach (var (x1, y1, x2, y2, confidence) in raw.Players)
        {
            var left = transform.MapToSourceX(x1);
            var top = transform.MapToSourceY(y1);
            var right = transform.MapToSourceX(x2);
            var bottom = transform.MapToSourceY(y2);

            var upperBody = UpperBodyColorSampling.UpperBodyRectangle(left, top, right, bottom);
            var color = upperBody is { } rect
                ? UpperBodyColorSampling.DominantColor(bgra8Pixels, width, height, stride, rect.Left, rect.Top, rect.Right, rect.Bottom)
                : null;

            players.Add(new PlayerDetection(left, top, right, bottom, confidence, color));
        }

        var others = new List<OnCourtObjectDetection>(raw.Others.Count);
        foreach (var (x1, y1, x2, y2, confidence, className) in raw.Others)
        {
            others.Add(new OnCourtObjectDetection(
                transform.MapToSourceX(x1), transform.MapToSourceY(y1), transform.MapToSourceX(x2), transform.MapToSourceY(y2), confidence, className));
        }

        return new MultiClassDetectionResult(players, others);
    }

    private static List<(double X1, double Y1, double X2, double Y2, float Confidence)> SuppressOverlapping(
        List<(double X1, double Y1, double X2, double Y2, float Confidence)> candidates,
        float iouThreshold)
    {
        var kept = new List<(double X1, double Y1, double X2, double Y2, float Confidence)>();

        foreach (var candidate in candidates.OrderByDescending(c => c.Confidence))
        {
            if (!kept.Any(existing => Iou(candidate, existing) > iouThreshold))
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    private static double Iou(
        (double X1, double Y1, double X2, double Y2, float Confidence) a,
        (double X1, double Y1, double X2, double Y2, float Confidence) b)
    {
        var intersectWidth = Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1));
        var intersectHeight = Math.Max(0, Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));
        var intersectArea = intersectWidth * intersectHeight;

        var areaA = Math.Max(0, a.X2 - a.X1) * Math.Max(0, a.Y2 - a.Y1);
        var areaB = Math.Max(0, b.X2 - b.X1) * Math.Max(0, b.Y2 - b.Y1);
        var unionArea = areaA + areaB - intersectArea;

        return unionArea <= 0 ? 0 : intersectArea / unionArea;
    }

    public void Dispose() => _pipeline.Dispose();
}
