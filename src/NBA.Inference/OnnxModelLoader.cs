using Microsoft.ML.OnnxRuntime;

namespace NBA.Inference;

/// <summary>Loads an ONNX model into a session, translating failures into a descriptive <see cref="ModelLoadException"/>.</summary>
public static class OnnxModelLoader
{
    public static InferenceSession Load(string modelPath, SessionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new ModelLoadException(modelPath, $"ONNX model file not found at '{modelPath}'.");
        }

        try
        {
            return new InferenceSession(modelPath, options ?? new SessionOptions());
        }
        catch (Exception ex)
        {
            throw new ModelLoadException(
                modelPath,
                $"Failed to load ONNX model from '{modelPath}': {ex.Message}",
                ex);
        }
    }
}
