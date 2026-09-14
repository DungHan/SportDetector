using Microsoft.ML.OnnxRuntime;
using NBA.Inference;

namespace NBA.Vision;

/// <summary>
/// Player detector backed by a YOLOv8-style object-detection ONNX model - specifically the raw (no-NMS)
/// export shape Ultralytics' own exporter produces (verified against a real `yolov8n.onnx` export: input
/// "images" of shape [1, 3, 640, 640], output "output0" of shape [1, 4 + numClasses, numCandidates] - 4
/// box channels (cx, cy, w, h, in the model's input-pixel coordinate space, i.e. [0, inputSize]) followed by
/// one confidence channel per class, indexed by class - not a combined confidence+classId pair). Only the
/// <see cref="personClassId"/> channel is read (COCO class 0, "person", for a stock COCO-pretrained export),
/// so multi-class models work without decoding every class. Non-max suppression always runs in
/// postprocessing, since this raw export shape has none baked in.
/// </summary>
public sealed class OnnxPlayerDetector : IPlayerDetector, IDisposable
{
    private readonly OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), IReadOnlyList<(double X1, double Y1, double X2, double Y2, float Confidence)>> _pipeline;

    public OnnxPlayerDetector(
        string modelPath,
        int inputSize = 640,
        float confidenceThreshold = 0.5f,
        float iouThreshold = 0.45f,
        int personClassId = 0,
        string inputName = "images",
        Action<float, int>? onDiagnostics = null)
    {
        _pipeline = new OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), IReadOnlyList<(double, double, double, double, float)>>(
            modelPath,
            preprocess: frame =>
            {
                var tensor = ImagePreprocessing.ToNchwTensor(frame.Pixels, frame.Width, frame.Height, frame.Stride, inputSize, inputSize);
                return [NamedOnnxValue.CreateFromTensor(inputName, tensor)];
            },
            postprocess: results =>
            {
                var output = results.First().AsTensor<float>();
                var personChannel = 4 + personClassId;
                var candidateCount = output.Dimensions[2];
                var candidates = new List<(double X1, double Y1, double X2, double Y2, float Confidence)>();
                var maxConfidenceSeen = 0f;
                var aboveThresholdCount = 0;

                for (var i = 0; i < candidateCount; i++)
                {
                    var confidence = output[0, personChannel, i];
                    if (confidence > maxConfidenceSeen)
                    {
                        maxConfidenceSeen = confidence;
                    }

                    if (confidence < confidenceThreshold)
                    {
                        continue;
                    }

                    aboveThresholdCount++;

                    var cx = output[0, 0, i];
                    var cy = output[0, 1, i];
                    var w = output[0, 2, i];
                    var h = output[0, 3, i];

                    candidates.Add((
                        (cx - (w / 2)) / inputSize,
                        (cy - (h / 2)) / inputSize,
                        (cx + (w / 2)) / inputSize,
                        (cy + (h / 2)) / inputSize,
                        confidence));
                }

                onDiagnostics?.Invoke(maxConfidenceSeen, aboveThresholdCount);

                return SuppressOverlapping(candidates, iouThreshold);
            });
    }

    public IReadOnlyList<PlayerDetection> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        var raw = _pipeline.Run((bgra8Pixels.ToArray(), width, height, stride)).Output;
        var detections = new List<PlayerDetection>(raw.Count);

        foreach (var (x1, y1, x2, y2, confidence) in raw)
        {
            detections.Add(new PlayerDetection(x1 * width, y1 * height, x2 * width, y2 * height, confidence));
        }

        return detections;
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
