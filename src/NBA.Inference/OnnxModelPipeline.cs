using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;

namespace NBA.Inference;

/// <summary>
/// Generic preprocess -> run -> postprocess pipeline around one loaded ONNX model. Every model this project
/// loads (court keypoints today; player/ball/jersey/OCR models in later phases) gets its own instance of this
/// with model-specific pre/postprocess delegates, reusing the same session management, execution-provider
/// fallback, and latency measurement. See design.md's "Inference: ONNX Runtime with DirectML→CPU fallback,
/// one model = one pipeline instance".
/// </summary>
public sealed class OnnxModelPipeline<TInput, TOutput> : IDisposable
{
    private readonly InferenceSession _session;
    private readonly Func<TInput, IReadOnlyCollection<NamedOnnxValue>> _preprocess;
    private readonly Func<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>, TOutput> _postprocess;

    public ExecutionProviderKind Provider { get; }

    public OnnxModelPipeline(
        string modelPath,
        Func<TInput, IReadOnlyCollection<NamedOnnxValue>> preprocess,
        Func<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>, TOutput> postprocess,
        bool preferDirectMl = true)
    {
        var selection = ExecutionProviderSelector.CreateSessionOptions(preferDirectMl);
        Provider = selection.Provider;
        _session = OnnxModelLoader.Load(modelPath, selection.Options);
        _preprocess = preprocess;
        _postprocess = postprocess;
    }

    /// <summary>Test-only entry point: runs the pre/postprocess pipeline against an already-constructed session.</summary>
    internal OnnxModelPipeline(
        InferenceSession session,
        ExecutionProviderKind provider,
        Func<TInput, IReadOnlyCollection<NamedOnnxValue>> preprocess,
        Func<IDisposableReadOnlyCollection<DisposableNamedOnnxValue>, TOutput> postprocess)
    {
        _session = session;
        Provider = provider;
        _preprocess = preprocess;
        _postprocess = postprocess;
    }

    public InferenceResult<TOutput> Run(TInput input)
    {
        var inputs = _preprocess(input);

        var stopwatch = Stopwatch.StartNew();
        using var results = _session.Run(inputs);
        stopwatch.Stop();

        var output = _postprocess(results);
        return new InferenceResult<TOutput> { Output = output, Latency = stopwatch.Elapsed };
    }

    public void Dispose() => _session.Dispose();
}
