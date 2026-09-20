using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NBA.Inference;

namespace NBA.Vision;

/// <summary>One registered sport's fixed prompt, reduced to its precomputed, L2-normalized CLIP/SigLIP text embedding.</summary>
public readonly record struct ClipSportPrompt(SportType Sport, IReadOnlyList<float> Embedding);

/// <summary>
/// Sport classifier backed by a CLIP/SigLIP-family vision encoder run zero-shot against a fixed set of
/// precomputed text-prompt embeddings - see design.md's "Sport classification: CLIP/SigLIP zero-shot, no
/// fine-tuning, cached per source" decision (supersedes <see cref="OnnxSportClassifier"/>'s fine-tuned-classifier
/// approach). Only the vision encoder is loaded/run here: prompt embeddings are computed offline by running the
/// model's text encoder once over the fixed prompt list (e.g. "a basketball game") - see
/// <see cref="ClipPromptEmbeddings"/> - and are passed in already L2-normalized. No text encoder or tokenizer
/// ships or runs in this app.
/// </summary>
public sealed class ClipZeroShotSportClassifier : ISportClassifier, IDisposable
{
    // CLIP's standard input normalization constants (ImageNet-derived; used by OpenAI's released CLIP
    // checkpoints and most CLIP/SigLIP-family exports). Applied on top of ImagePreprocessing.ToNchwTensor's
    // [0,1]-scaled RGB output, which itself has no mean/std normalization baked in.
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Std = [0.26862954f, 0.26130258f, 0.27577711f];

    private readonly IReadOnlyList<ClipSportPrompt> _prompts;
    private readonly float _logitScale;
    private readonly OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), float[]> _pipeline;

    /// <summary>Which ONNX Runtime execution provider this instance's session actually ended up on (see <see cref="ExecutionProviderSelector"/>).</summary>
    public ExecutionProviderKind Provider => _pipeline.Provider;

    /// <param name="visionEncoderModelPath">Path to a CLIP/SigLIP vision-encoder-only ONNX export (no text tower).</param>
    /// <param name="prompts">One entry per registered sport, each an already L2-normalized text embedding computed offline (see <see cref="ClipPromptEmbeddings"/>).</param>
    /// <param name="inputSize">Vision encoder's expected square input resolution (224 for CLIP ViT-B/32 and most siblings).</param>
    /// <param name="logitScale">
    /// Temperature applied to cosine similarities before softmax, matching the scale the embeddings were
    /// trained under. OpenAI's released CLIP checkpoints use a learned <c>logit_scale</c> whose exp() is ~100 -
    /// that is the default here, but it should be read from whatever checkpoint's config the model actually
    /// came from rather than assumed, especially for SigLIP checkpoints (different training objective/scale).
    /// </param>
    /// <param name="inputName">Vision encoder's input tensor name - varies by export tool; "pixel_values" is the common Hugging Face/optimum convention.</param>
    private readonly Action<IReadOnlyList<(SportType Sport, float Probability)>>? _onScored;

    /// <param name="onScored">
    /// Optional diagnostic hook invoked after every <see cref="Classify"/> call with *every* prompt's
    /// probability (not just the winner), sorted highest-first - purely for calibration/debugging (e.g.
    /// logging why a frame landed on "unknown": low confidence across the board vs. the catch-all prompt
    /// confidently winning). Never affects the returned <see cref="SportClassifierOutput"/>.
    /// </param>
    public ClipZeroShotSportClassifier(
        string visionEncoderModelPath,
        IReadOnlyList<ClipSportPrompt> prompts,
        int inputSize = 224,
        float logitScale = 100f,
        string inputName = "pixel_values",
        Action<IReadOnlyList<(SportType Sport, float Probability)>>? onScored = null)
    {
        if (prompts.Count < 2)
        {
            // With a single prompt, softmax over one class always outputs 1.0 regardless of actual similarity -
            // the confidence signal (and everything SportClassificationCoordinator's threshold/registry logic
            // relies on it for) would be meaningless. At least two prompts - ideally several sports plus a
            // catch-all "not a sports broadcast" entry mapped to SportType.Unknown - are required for this to work.
            throw new ArgumentException("At least two sport prompt embeddings are required for a meaningful confidence signal.", nameof(prompts));
        }

        _prompts = prompts;
        _logitScale = logitScale;
        _onScored = onScored;

        _pipeline = new OnnxModelPipeline<(byte[] Pixels, int Width, int Height, int Stride), float[]>(
            visionEncoderModelPath,
            preprocess: frame =>
            {
                var tensor = ImagePreprocessing.ToNchwTensor(frame.Pixels, frame.Width, frame.Height, frame.Stride, inputSize, inputSize, out _);
                NormalizeInPlace(tensor);
                return [NamedOnnxValue.CreateFromTensor(inputName, tensor)];
            },
            // Takes the first output regardless of name, since export tools disagree on it (e.g.
            // "image_embeds" for Hugging Face's CLIPVisionModelWithProjection vs. an unnamed pooled output
            // for others) - unlike the input name, there's no single common convention worth hardcoding.
            postprocess: results => results.First().AsTensor<float>().ToArray());
    }

    public SportClassifierOutput Classify(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        var rawEmbedding = _pipeline.Run((bgra8Pixels.ToArray(), width, height, stride)).Output;
        var imageEmbedding = L2Normalize(rawEmbedding);

        var similarities = new float[_prompts.Count];
        for (var i = 0; i < _prompts.Count; i++)
        {
            var promptEmbedding = _prompts[i].Embedding;
            if (promptEmbedding.Count != imageEmbedding.Length)
            {
                throw new InvalidOperationException(
                    $"Prompt embedding for sport '{_prompts[i].Sport}' has dimension {promptEmbedding.Count}, " +
                    $"but the vision encoder produced dimension {imageEmbedding.Length} - the model and the " +
                    "precomputed prompt embeddings are mismatched.");
            }

            similarities[i] = _logitScale * Dot(imageEmbedding, promptEmbedding);
        }

        var probabilities = Softmax(similarities);
        var bestIndex = Array.IndexOf(probabilities, probabilities.Max());

        if (_onScored is not null)
        {
            var ranked = _prompts
                .Select((p, i) => (p.Sport, Probability: probabilities[i]))
                .OrderByDescending(p => p.Probability)
                .ToList();
            _onScored(ranked);
        }

        return new SportClassifierOutput(_prompts[bestIndex].Sport, probabilities[bestIndex]);
    }

    private static void NormalizeInPlace(DenseTensor<float> tensor)
    {
        var height = tensor.Dimensions[2];
        var width = tensor.Dimensions[3];
        for (var c = 0; c < 3; c++)
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    tensor[0, c, y, x] = (tensor[0, c, y, x] - Mean[c]) / Std[c];
                }
            }
        }
    }

    private static float[] L2Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        return norm < 1e-12f ? vector : vector.Select(v => v / norm).ToArray();
    }

    private static float Dot(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        var sum = 0f;
        for (var i = 0; i < a.Count; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    private static float[] Softmax(float[] logits)
    {
        var max = logits.Max();
        var exps = logits.Select(l => MathF.Exp(l - max)).ToArray();
        var sum = exps.Sum();
        return exps.Select(e => e / sum).ToArray();
    }

    public void Dispose() => _pipeline.Dispose();
}
