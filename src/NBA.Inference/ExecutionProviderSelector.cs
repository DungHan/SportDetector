using Microsoft.ML.OnnxRuntime;

namespace NBA.Inference;

/// <summary>
/// Selects an ONNX Runtime execution provider per platform, always falling back to CPU if the preferred
/// provider is unavailable: macOS -> CoreML (Apple Neural Engine/Metal GPU), Windows -> CUDA (NVIDIA GPU) then
/// DirectML (Intel/AMD/any DX12 GPU). CPU fallback is a hard requirement (the app must keep working, just
/// slower), not an optimization - see the inference/onnx-runtime spec's "Select execution provider with
/// fallback" requirement.
/// </summary>
public static class ExecutionProviderSelector
{
    public readonly record struct Selection(SessionOptions Options, ExecutionProviderKind Provider);

    public static Selection CreateSessionOptions(bool preferGpu = true)
    {
        if (preferGpu && OperatingSystem.IsMacOS())
        {
            var options = NewSessionOptions();
            try
            {
                // MLProgram is Apple's modern model format and is required to schedule work onto the Neural
                // Engine; without it CoreML EP falls back to the legacy NeuralNetwork format, which is
                // GPU/CPU-only.
                options.AppendExecutionProvider_CoreML(CoreMLFlags.COREML_FLAG_CREATE_MLPROGRAM);
                return new Selection(options, ExecutionProviderKind.CoreMl);
            }
            catch
            {
                // No CoreML support in this ONNX Runtime build - dispose the half-configured options and fall
                // back further down.
                options.Dispose();
            }
        }
        else if (preferGpu && OperatingSystem.IsWindows())
        {
            var cudaOptions = NewSessionOptions();
            try
            {
                cudaOptions.AppendExecutionProvider_CUDA(deviceId: 0);
                return new Selection(cudaOptions, ExecutionProviderKind.Cuda);
            }
            catch
            {
                // No NVIDIA GPU/CUDA runtime present - dispose and try DirectML instead.
                cudaOptions.Dispose();
            }

            var dmlOptions = NewSessionOptions();
            try
            {
                dmlOptions.AppendExecutionProvider_DML(deviceId: 0);
                return new Selection(dmlOptions, ExecutionProviderKind.DirectMl);
            }
            catch
            {
                // No DirectML-capable device/drivers - dispose the half-configured options and fall back to CPU.
                dmlOptions.Dispose();
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
