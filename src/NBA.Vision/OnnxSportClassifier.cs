using Microsoft.ML.OnnxRuntime;
using NBA.Inference;

namespace NBA.Vision;

/// <summary>
/// Sport classifier backed by a fine-tuned lightweight ONNX classifier (NOT zero-shot ImageNet - see
/// design.md's "Sport classification: fine-tuned lightweight classifier" decision for why). The model is
/// expected to output raw per-class logits; this applies softmax and reports the top class + its probability.
/// </summary>
public sealed class OnnxSportClassifier : ISportClassifier, IDisposable
{
    private readonly OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), SportClassifierOutput> _pipeline;

    public OnnxSportClassifier(string modelPath, IReadOnlyList<string> labelsInOutputOrder, int inputSize = 224)
    {
        _pipeline = new OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), SportClassifierOutput>(
            modelPath,
            preprocess: frame =>
            {
                var tensor = ImagePreprocessing.ToNchwTensor(frame.Pixels, frame.Width, frame.Height, frame.Stride, inputSize, inputSize);
                return [NamedOnnxValue.CreateFromTensor("input", tensor)];
            },
            postprocess: results =>
            {
                var logits = results.First().AsTensor<float>().ToArray();
                var probabilities = Softmax(logits);
                var bestIndex = Array.IndexOf(probabilities, probabilities.Max());
                return new SportClassifierOutput(new SportType(labelsInOutputOrder[bestIndex]), probabilities[bestIndex]);
            });
    }

    public SportClassifierOutput Classify(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) =>
        _pipeline.Run((bgra8Pixels.ToArray(), width, height, stride)).Output;

    private static float[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exps = logits.Select(l => MathF.Exp(l - max)).ToArray();
        var sum = exps.Sum();
        return exps.Select(e => e / sum).ToArray();
    }

    public void Dispose() => _pipeline.Dispose();
}
