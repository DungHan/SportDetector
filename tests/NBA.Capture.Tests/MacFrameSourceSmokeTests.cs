using NBA.Capture.Mac;

namespace NBA.Capture.Tests;

/// <summary>
/// Exercises the real CoreGraphics-backed macOS capture backend (<see cref="MacFrameSource"/>/
/// <see cref="MacCaptureSourceEnumerator"/>) end-to-end, against the actual OS - not just the fakes-only
/// contract in <see cref="FrameSourceContractTests"/>. Only meaningful on macOS; each test returns early
/// (passes trivially) elsewhere, mirroring <c>ExecutionProviderSelectorTests</c>'s
/// <c>OperatingSystem.IsWindows()</c> pattern in NBA.Inference.Tests.
///
/// Written after a throwaway verification harness (used to validate the production classes before this test
/// existed) caught a real Start/StopAsync race - see design.md's "Capture: a second CoreGraphics-backed
/// backend for macOS" Decision. This test exists so that race, and the backend's basic ability to deliver
/// real frames, stay covered by the checked-in suite instead of only a one-off manual run.
///
/// Requires this machine/agent to have granted Screen Recording permission to the test runner - if that
/// permission is missing, <see cref="MacFrameSource"/> will report every capture as a failure and (after a
/// few consecutive misses) raise <c>SourceLost</c> instead of delivering frames, which these tests will
/// surface as a timeout/failure rather than silently passing.
/// </summary>
public class MacFrameSourceSmokeTests
{
    [Fact]
    public void EnumerateSources_ReturnsAtLeastTheMainDisplay()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var sources = new MacCaptureSourceEnumerator().EnumerateSources();

        Assert.Contains(sources, s => s.Kind == CaptureSourceKind.Screen);
    }

    [Fact]
    public async Task StartAsync_OnRealDisplay_DeliversRealFrames()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var screen = new MacCaptureSourceEnumerator().EnumerateSources()
            .First(s => s.Kind == CaptureSourceKind.Screen);

        await using var source = new MacFrameSource();
        var frameArrived = new TaskCompletionSource<CapturedFrame>();
        source.FrameArrived += (_, args) => frameArrived.TrySetResult(args.Frame);

        await source.StartAsync(screen);

        var frame = await WaitWithTimeout(frameArrived.Task, TimeSpan.FromSeconds(5));

        Assert.True(frame.Width > 0);
        Assert.True(frame.Height > 0);
        Assert.Equal(FramePixelFormat.Bgra8, frame.Format);
        Assert.True(frame.Stride >= frame.Width * 4);
        Assert.True(frame.Pixels.Length >= frame.Stride * frame.Height);
        Assert.NotNull(source.TryGetLatestFrame());
    }

    [Fact]
    public async Task StartStopRepeatedly_NeverLeavesAStaleFrameAfterStop()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // Regression test for the race the real backend surfaced during initial verification: StopAsync used
        // to clear LatestFrameBuffer/State before the previous poll loop had actually observed cancellation,
        // so an in-flight capture could publish a stale frame just after the "clear". Repeating start/stop in
        // a tight loop is a far more reliable way to reproduce a race like that than a single pass.
        var screen = new MacCaptureSourceEnumerator().EnumerateSources()
            .First(s => s.Kind == CaptureSourceKind.Screen);

        await using var source = new MacFrameSource();

        for (var i = 0; i < 20; i++)
        {
            await source.StartAsync(screen);
            await Task.Delay(20);
            await source.StopAsync();

            Assert.Equal(CaptureSourceState.Stopped, source.State);
            Assert.Null(source.TryGetLatestFrame());
        }
    }

    [Fact]
    public async Task StartAsync_SwitchingSourceWithoutStopFirst_RaisesSourceChangedAndAdoptsTheNewSource()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var sources = new MacCaptureSourceEnumerator().EnumerateSources();
        var screen = sources.First(s => s.Kind == CaptureSourceKind.Screen);
        var window = sources.FirstOrDefault(s => s.Kind == CaptureSourceKind.Window);

        if (window is null)
        {
            // No capturable app windows available on this run (e.g. a bare CI agent) - the screen-only path
            // is already covered above, so there's nothing further this particular test can exercise.
            return;
        }

        await using var source = new MacFrameSource();
        SourceChangedEventArgs? raised = null;
        source.SourceChanged += (_, args) => raised = args;

        await source.StartAsync(window);
        await Task.Delay(200);

        await source.StartAsync(screen); // no StopAsync() call in between
        await Task.Delay(200);

        Assert.NotNull(raised);
        Assert.Equal(window, raised!.PreviousSource);
        Assert.Equal(screen, raised.NewSource);
        Assert.Equal(CaptureSourceState.Running, source.State);
        Assert.Equal(screen, source.ActiveSource);
    }

    private static async Task<CapturedFrame> WaitWithTimeout(Task<CapturedFrame> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        Assert.True(completed == task, "Timed out waiting for a captured frame - is Screen Recording permission granted to the test runner?");
        return await task;
    }
}
