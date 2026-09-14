using Microsoft.ML.OnnxRuntime;
using NBA.Inference;

namespace NBA.Vision;

/// <summary>
/// Court keypoint detector backed by a YOLOv8-pose ONNX model, bound to one sport's geometry (one model per
/// sport, per design.md). Only landmarks with a non-null <see cref="CourtLandmark.KeypointIndex"/> are
/// detectable - see <see cref="BasketballGeometry"/> for which ones and why.
///
/// Matches Ultralytics' standard YOLOv8-pose export layout (verified against a real trained model's ONNX
/// graph, not assumed): input tensor "images" [1,3,inputSize,inputSize]; output tensor "output0" of shape
/// [1, 4 + 1 + 3*K, A] - per anchor (A of them, e.g. 8400 for a 640 input): 4 box values (unused here, this
/// detector only wants keypoints), 1 already-sigmoid class confidence, then K keypoint triples
/// (x, y already decoded to input-pixel-space, already-sigmoid visibility). Since exactly one "court" object
/// is ever expected per frame, this picks the single highest-confidence anchor rather than running NMS.
/// </summary>
public sealed class OnnxCourtKeypointDetector : ICourtKeypointDetector, IDisposable
{
    private readonly CourtGeometryDefinition _geometry;
    private readonly float _keypointConfidenceThreshold;
    private readonly float _detectionConfidenceThreshold;
    private readonly int _inputSize;
    private readonly OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), (float DetectionConfidence, float[] KeypointValues)> _pipeline;

    public SportType Sport => _geometry.Sport;

    public OnnxCourtKeypointDetector(
        string modelPath,
        CourtGeometryDefinition geometry,
        int inputSize = 640,
        float keypointConfidenceThreshold = 0.5f,
        float detectionConfidenceThreshold = 0.5f)
    {
        _geometry = geometry;
        _inputSize = inputSize;
        _keypointConfidenceThreshold = keypointConfidenceThreshold;
        _detectionConfidenceThreshold = detectionConfidenceThreshold;

        _pipeline = new OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), (float, float[])>(
            modelPath,
            preprocess: frame =>
            {
                var tensor = ImagePreprocessing.ToNchwTensor(frame.Pixels, frame.Width, frame.Height, frame.Stride, inputSize, inputSize);
                return [NamedOnnxValue.CreateFromTensor("images", tensor)];
            },
            postprocess: results =>
            {
                var output = results.First(r => r.Name == "output0").AsTensor<float>();
                var channelCount = output.Dimensions[1];
                var anchorCount = output.Dimensions[2];

                var bestAnchor = 0;
                var bestConfidence = float.MinValue;
                for (var a = 0; a < anchorCount; a++)
                {
                    var confidence = output[0, 4, a];
                    if (confidence > bestConfidence)
                    {
                        bestConfidence = confidence;
                        bestAnchor = a;
                    }
                }

                var keypointValues = new float[channelCount - 5];
                for (var c = 5; c < channelCount; c++)
                {
                    keypointValues[c - 5] = output[0, c, bestAnchor];
                }

                return (bestConfidence, keypointValues);
            });
    }

    public IReadOnlyList<DetectedKeypoint> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        var (detectionConfidence, keypointValues) = _pipeline.Run((bgra8Pixels.ToArray(), width, height, stride)).Output;
        if (detectionConfidence < _detectionConfidenceThreshold)
        {
            return [];
        }

        var keypoints = new List<DetectedKeypoint>();
        foreach (var landmark in _geometry.Landmarks)
        {
            if (landmark.KeypointIndex is not { } keypointIndex)
            {
                continue;
            }

            var offset = keypointIndex * 3;
            if (offset + 2 >= keypointValues.Length)
            {
                continue;
            }

            var (pixelX, pixelY, confidence) = (keypointValues[offset], keypointValues[offset + 1], keypointValues[offset + 2]);
            if (confidence < _keypointConfidenceThreshold)
            {
                continue;
            }

            var normX = pixelX / _inputSize;
            var normY = pixelY / _inputSize;
            keypoints.Add(new DetectedKeypoint(landmark.Name, new ImagePoint(normX * width, normY * height), confidence));
        }

        return keypoints;
    }

    public void Dispose() => _pipeline.Dispose();
}
