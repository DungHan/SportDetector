using Microsoft.ML.OnnxRuntime;

namespace NBA.Inference;

/// <summary>
/// Selects an ONNX Runtime execution provider: try DirectML on Windows, fall back to CPU if unavailable.
/// See the inference/onnx-runtime spec's "Select execution provider with fallback" requirement - CPU fallback
/// is a hard requirement (the app must keep working, just slower), not an optimization.
/// </summary>
public static class ExecutionProviderSelector
{
    public readonly record struct Selection(SessionOptions Options, ExecutionProviderKind Provider);

    public static Selection CreateSessionOptions(bool preferDirectMl = true)
    {
        if (preferDirectMl && OperatingSystem.IsWindows())
        {
            var options = NewSessionOptions();
            try
            {
                options.AppendExecutionProvider_DML(deviceId: 0);
                return new Selection(options, ExecutionProviderKind.DirectMl);
            }
            catch
            {
                // No DirectML-capable device/drivers - dispose the half-configured options and fall back to CPU.
                options.Dispose();
            }
        }

        return new Selection(NewSessionOptions(), ExecutionProviderKind.Cpu);
    }

    // ORT_ENABLE_EXTENDED (the ORT default) applies node-fusion optimizations, including one that
    // misidentifies this project's YOLO models' SiLU activations as QuickGelu and fuses them into a node some
    // CPU EP builds can't execute ("GetElementType is not implemented"). Capping at ORT_ENABLE_BASIC skips
    // that fusion pass while keeping the cheaper, uncontroversial optimizations.
    private static SessionOptions NewSessionOptions() =>
        new() { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC };
}
