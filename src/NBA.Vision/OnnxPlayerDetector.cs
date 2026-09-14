using Microsoft.ML.OnnxRuntime;
using NBA.Inference;

namespace NBA.Vision;

/// <summary>
/// Player detector backed by a YOLO-style object-detection ONNX model. Assumes the model outputs a single
/// tensor named "output" of shape [1, N, 6] - (x1, y1, x2, y2, confidence, classId) per candidate box, with
/// box coordinates normalized to [0, 1]. NO TRAINED MODEL EXISTS YET (see design.md's risk entry in
/// openspec/changes/add-player-detection/) - this output convention matches common YOLO ONNX export shapes
/// and is a placeholder to revisit against whatever format the actually-adopted model uses. Non-max
/// suppression always runs in postprocessing regardless of whether the exported model already performs it
/// internally - a no-op on an already-deduplicated set, so this is safe either way.
/// </summary>
public sealed class OnnxPlayerDetector : IPlayerDetector, IDisposable
{
    private readonly OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), IReadOnlyList<(double X1, double Y1, double X2, double Y2, float Confidence)>> _pipeline;

    public OnnxPlayerDetector(
        string modelPath,
        int inputSize = 640,
        float confidenceThreshold = 0.5f,
        float iouThreshold = 0.45f,
        int personClassId = 0)
    {
        _pipeline = new OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), IReadOnlyList<(double, double, double, double, float)>>(
            modelPath,
            preprocess: frame =>
            {
                var tensor = ImagePreprocessing.ToNchwTensor(frame.Pixels, frame.Width, frame.Height, frame.Stride, inputSize, inputSize);
                return [NamedOnnxValue.CreateFromTensor("input", tensor)];
            },
            postprocess: results =>
            {
                var output = results.First().AsTensor<float>();
                var candidates = new List<(double X1, double Y1, double X2, double Y2, float Confidence)>();

                for (var i = 0; i < output.Dimensions[1]; i++)
                {
                    var confidence = output[0, i, 4];
                    var classId = (int)MathF.Round(output[0, i, 5]);
                    if (confidence < confidenceThreshold || classId != personClassId)
                    {
                        continue;
                    }

                    candidates.Add((output[0, i, 0], output[0, i, 1], output[0, i, 2], output[0, i, 3], confidence));
                }

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
