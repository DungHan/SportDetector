using NBA.Inference;

namespace NBA.Inference.Tests;

public class ExecutionProviderSelectorTests
{
    [Fact]
    public void CreateSessionOptions_PreferDirectMlFalse_AlwaysReturnsCpu()
    {
        var selection = ExecutionProviderSelector.CreateSessionOptions(preferDirectMl: false);

        Assert.Equal(ExecutionProviderKind.Cpu, selection.Provider);
        selection.Options.Dispose();
    }

    [Fact]
    public void CreateSessionOptions_OnNonWindows_FallsBackToCpu()
    {
        // This test machine is not Windows, so DirectML is never attempted - this exercises exactly the
        // "DirectML unavailable" path from the spec (OperatingSystem.IsWindows() gate), even though it
        // can't exercise the Windows-but-no-capable-GPU variant of that same fallback. Skip gracefully if
        // this ever runs on a Windows CI agent instead.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var selection = ExecutionProviderSelector.CreateSessionOptions(preferDirectMl: true);

        Assert.Equal(ExecutionProviderKind.Cpu, selection.Provider);
        selection.Options.Dispose();
    }
}
