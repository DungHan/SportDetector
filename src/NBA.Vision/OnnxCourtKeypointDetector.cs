using Microsoft.ML.OnnxRuntime;
using NBA.Inference;

namespace NBA.Vision;

/// <summary>
/// Court keypoint detector backed by a YOLO-pose-style ONNX model, bound to one sport's geometry (one model
/// per sport, per design.md). Assumes the model outputs a single tensor named "output" of shape
/// [1, landmarkCount, 3] - (normalizedX, normalizedY, confidence) per landmark, in the same order as the
/// bound <see cref="CourtGeometryDefinition"/>'s <c>Landmarks</c> list. NO TRAINED MODEL EXISTS YET (see
/// design.md's risk entry) - this output convention is a reasonable placeholder assumption for a keypoint
/// regression head, and should be revisited against whatever format the model actually trained for court
/// keypoints ends up using (a heatmap-based pose head, for instance, would need different postprocessing).
/// </summary>
public sealed class OnnxCourtKeypointDetector : ICourtKeypointDetector, IDisposable
{
    private readonly CourtGeometryDefinition _geometry;
    private readonly float _confidenceThreshold;
    private readonly int _inputSize;
    private readonly OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), IReadOnlyList<(int Index, double NormX, double NormY, float Confidence)>> _pipeline;

    public SportType Sport => _geometry.Sport;

    public OnnxCourtKeypointDetector(string modelPath, CourtGeometryDefinition geometry, int inputSize = 224, float confidenceThreshold = 0.5f)
    {
        _geometry = geometry;
        _inputSize = inputSize;
        _confidenceThreshold = confidenceThreshold;

        _pipeline = new OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), IReadOnlyList<(int, double, double, float)>>(
            modelPath,
            preprocess: frame =>
            {
                var tensor = ImagePreprocessing.ToNchwTensor(frame.Pixels, frame.Width, frame.Height, frame.Stride, inputSize, inputSize);
                return [NamedOnnxValue.CreateFromTensor("input", tensor)];
            },
            postprocess: results =>
            {
                var output = results.First().AsTensor<float>();
                var count = Math.Min(_geometry.Landmarks.Count, output.Dimensions[1]);
                var keypoints = new List<(int, double, double, float)>(count);
                for (var i = 0; i < count; i++)
                {
                    keypoints.Add((i, output[0, i, 0], output[0, i, 1], output[0, i, 2]));
                }

                return keypoints;
            });
    }

    public IReadOnlyList<DetectedKeypoint> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        var raw = _pipeline.Run((bgra8Pixels.ToArray(), width, height, stride)).Output;
        var keypoints = new List<DetectedKeypoint>(raw.Count);

        foreach (var (index, normX, normY, confidence) in raw)
        {
            if (confidence < _confidenceThreshold)
            {
                continue;
            }

            var landmark = _geometry.Landmarks[index];
            keypoints.Add(new DetectedKeypoint(landmark.Name, new ImagePoint(normX * width, normY * height), confidence));
        }

        return keypoints;
    }

    public void Dispose() => _pipeline.Dispose();
}
