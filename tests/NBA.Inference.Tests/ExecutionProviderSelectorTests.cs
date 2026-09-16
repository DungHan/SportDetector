using NBA.Inference;

namespace NBA.Inference.Tests;

public class ExecutionProviderSelectorTests
{
    [Fact]
    public void CreateSessionOptions_PreferGpuFalse_AlwaysReturnsCpu()
    {
        var selection = ExecutionProviderSelector.CreateSessionOptions(preferGpu: false);

        Assert.Equal(ExecutionProviderKind.Cpu, selection.Provider);
        selection.Options.Dispose();
    }

    [Fact]
    public void CreateSessionOptions_OnMacOS_PrefersCoreMl()
    {
        // This test machine is macOS, and the base ONNX Runtime package bundles CoreML EP support for
        // osx-arm64/osx-x64, so this exercises the "CoreML available" path from the spec.
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var selection = ExecutionProviderSelector.CreateSessionOptions(preferGpu: true);

        Assert.Equal(ExecutionProviderKind.CoreMl, selection.Provider);
        selection.Options.Dispose();
    }

    [Fact]
    public void CreateSessionOptions_OnNeitherMacNorWindows_FallsBackToCpu()
    {
        // Linux (and any other non-macOS, non-Windows platform) has no GPU EP branch today, so it always
        // falls straight to CPU regardless of preferGpu.
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsWindows())
        {
            return;
        }

        var selection = ExecutionProviderSelector.CreateSessionOptions(preferGpu: true);

        Assert.Equal(ExecutionProviderKind.Cpu, selection.Provider);
        selection.Options.Dispose();
    }
}
