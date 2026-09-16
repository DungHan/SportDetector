using Microsoft.ML.OnnxRuntime;
using NBA.Inference;

namespace NBA.JerseyOcr;

/// <summary>
/// Jersey-number recognizer backed by a closed-set classification ONNX model (per design.md's "closed-set
/// classification, not general sequence-decoding OCR" decision): input is a cropped BGRA8 region resized to
/// <c>inputSize x inputSize</c>, output "output0" is a <c>[1, 101]</c> tensor of per-class logits - 100 number
/// classes (index = the number, 0-99) plus one "no number" class (index 100). The winning class is the argmax;
/// its confidence is the softmax-equivalent score of that class, not the raw logit.
/// </summary>
public sealed class OnnxJerseyNumberRecognizer : IJerseyNumberRecognizer, IDisposable
{
    private const int NoNumberClassId = 100;
    private const string InputName = "input";

    private readonly int _inputSize;
    private readonly float _confidenceThreshold;
    private readonly OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride, int CropX, int CropY, int CropWidth, int CropHeight), (int ClassId, float Confidence)> _pipeline;

    public OnnxJerseyNumberRecognizer(string modelPath, int inputSize = 64, float confidenceThreshold = 0.5f)
    {
        _inputSize = inputSize;
        _confidenceThreshold = confidenceThreshold;

        _pipeline = new OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride, int CropX, int CropY, int CropWidth, int CropHeight), (int, float)>(
            modelPath,
            preprocess: crop =>
            {
                var tensor = ImagePreprocessing.ToNchwTensor(
                    crop.Pixels, crop.Width, crop.Height, crop.Stride,
                    crop.CropX, crop.CropY, crop.CropWidth, crop.CropHeight,
                    inputSize, inputSize);
                return [NamedOnnxValue.CreateFromTensor(InputName, tensor)];
            },
            postprocess: results =>
            {
                var output = results.First().AsTensor<float>();
                var classCount = output.Dimensions[1];

                var bestClassId = 0;
                var bestLogit = float.MinValue;
                for (var c = 0; c < classCount; c++)
                {
                    var logit = output[0, c];
                    if (logit > bestLogit)
                    {
                        bestLogit = logit;
                        bestClassId = c;
                    }
                }

                var sumExp = 0.0;
                for (var c = 0; c < classCount; c++)
                {
                    sumExp += Math.Exp(output[0, c] - bestLogit);
                }

                var confidence = (float)(1.0 / sumExp);
                return (bestClassId, confidence);
            });
    }

    public JerseyNumberRecognitionResult Recognize(
        ReadOnlySpan<byte> bgra8Pixels,
        int width,
        int height,
        int stride,
        double cropLeft,
        double cropTop,
        double cropRight,
        double cropBottom)
    {
        var clamped = CropClamping.Clamp(cropLeft, cropTop, cropRight, cropBottom, width, height);
        if (clamped.IsDegenerate)
        {
            return new JerseyNumberRecognitionResult(Number: null, Confidence: 0f);
        }

        var (classId, confidence) = _pipeline.Run((
            bgra8Pixels.ToArray(), width, height, stride,
            clamped.Left, clamped.Top, clamped.Right - clamped.Left, clamped.Bottom - clamped.Top)).Output;

        var number = classId == NoNumberClassId || confidence < _confidenceThreshold ? (int?)null : classId;
        return new JerseyNumberRecognitionResult(number, confidence);
    }

    public void Dispose() => _pipeline.Dispose();
}
