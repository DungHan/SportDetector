using NBA.Capture.Testing;
using Xunit;

namespace NBA.Capture.Tests;

/// <summary>
/// Exercises the <see cref="NBA.Capture.IFrameSource"/> contract (runtime source switching, source-lost
/// reporting, latest-frame-only delivery) against <see cref="FakeFrameSource"/>. This does not verify
/// <c>WindowsGraphicsCaptureFrameSource</c> itself (Windows-only, unrunnable on this machine - see
/// tasks.md), but pins the behavioral contract every <see cref="NBA.Capture.IFrameSource"/> implementation,
/// including the real one, must satisfy per the frame-capture spec.
/// </summary>
public class FrameSourceContractTests
{
    private static CaptureSourceDescriptor MakeSource(string id) =>
        new(id, DisplayName: id, CaptureSourceKind.Window, Handle: 1);

    [Fact]
    public async Task StartAsync_SetsActiveSourceAndRunningState()
    {
        await using var source = new FakeFrameSource();
        var descriptor = MakeSource("window:1");

        await source.StartAsync(descriptor);

        Assert.Equal(descriptor, source.ActiveSource);
        Assert.Equal(CaptureSourceState.Running, source.State);
    }

    [Fact]
    public async Task StartAsync_WhileAlreadyRunning_SwitchesSourceWithoutRequiringStopFirst()
    {
        await using var source = new FakeFrameSource();
        var first = MakeSource("window:1");
        var second = MakeSource("window:2");
        await source.StartAsync(first);

        SourceChangedEventArgs? raised = null;
        source.SourceChanged += (_, args) => raised = args;

        await source.StartAsync(second); // no StopAsync() call in between

        Assert.Equal(second, source.ActiveSource);
        Assert.Equal(CaptureSourceState.Running, source.State);
        Assert.NotNull(raised);
        Assert.Equal(first, raised!.PreviousSource);
        Assert.Equal(second, raised.NewSource);
    }

    [Fact]
    public async Task SwitchingSource_ClearsThePreviousSourcesStaleFrame()
    {
        await using var source = new FakeFrameSource();
        await source.StartAsync(MakeSource("window:1"));
        source.PublishFrame(MakeFrame());

        await source.StartAsync(MakeSource("window:2"));

        Assert.Null(source.TryGetLatestFrame());
    }

    [Fact]
    public async Task SourceLost_ReportsLostState_InsteadOfRepeatingLastFrame()
    {
        await using var source = new FakeFrameSource();
        await source.StartAsync(MakeSource("window:1"));
        source.PublishFrame(MakeFrame());

        var raised = false;
        source.SourceLost += (_, _) => raised = true;

        source.SimulateSourceLost();

        Assert.True(raised);
        Assert.Equal(CaptureSourceState.Lost, source.State);
    }

    [Fact]
    public async Task StopAsync_ClearsCurrentFrame()
    {
        await using var source = new FakeFrameSource();
        await source.StartAsync(MakeSource("window:1"));
        source.PublishFrame(MakeFrame());

        await source.StopAsync();

        Assert.Equal(CaptureSourceState.Stopped, source.State);
        Assert.Null(source.TryGetLatestFrame());
    }

    private static CapturedFrame MakeFrame() => new()
    {
        Width = 4,
        Height = 4,
        Format = FramePixelFormat.Bgra8,
        Stride = 16,
        Pixels = new byte[16 * 4],
        Timestamp = DateTimeOffset.UtcNow,
    };
}
