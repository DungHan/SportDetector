namespace NBA.Inference;

/// <summary>
/// Thrown when an ONNX model cannot be loaded (missing file, invalid/corrupt model). Carries the attempted
/// path so callers can surface a descriptive error instead of crashing - see the inference/onnx-runtime spec's
/// "Load an ONNX model from disk" requirement.
/// </summary>
public sealed class ModelLoadException : Exception
{
    public string ModelPath { get; }

    public ModelLoadException(string modelPath, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ModelPath = modelPath;
    }
}
