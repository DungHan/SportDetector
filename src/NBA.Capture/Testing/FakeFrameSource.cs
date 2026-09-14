namespace NBA.Capture.Testing;

/// <summary>
/// In-memory <see cref="IFrameSource"/> for tests, and for developing/verifying NBA.App's UI on platforms
/// where <c>WindowsGraphicsCaptureFrameSource</c> cannot run (e.g. this repo's macOS dev machine). Production
/// code always uses the Windows.Graphics.Capture-backed source, selected at composition-root/DI time by
/// platform - this type is never registered outside of Debug/test configurations.
/// </summary>
public sealed class FakeFrameSource : IFrameSource
{
    private readonly LatestFrameBuffer<CapturedFrame> _buffer = new();

    public CaptureSourceState State { get; private set; } = CaptureSourceState.NotStarted;

    public CaptureSourceDescriptor? ActiveSource { get; private set; }

    public event EventHandler<FrameArrivedEventArgs>? FrameArrived;

    public event EventHandler<SourceChangedEventArgs>? SourceChanged;

    public event EventHandler? SourceLost;

    public Task StartAsync(CaptureSourceDescriptor source, CancellationToken cancellationToken = default)
    {
        var previous = ActiveSource;
        var wasRunning = State is CaptureSourceState.Running or CaptureSourceState.Lost;

        ActiveSource = source;
        State = CaptureSourceState.Running;
        _buffer.Clear();

        if (wasRunning && previous is not null)
        {
            SourceChanged?.Invoke(this, new SourceChangedEventArgs(previous, source));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        State = CaptureSourceState.Stopped;
        _buffer.Clear();
        return Task.CompletedTask;
    }

    public CapturedFrame? TryGetLatestFrame() => _buffer.TryGetLatest();

    /// <summary>Test/dev hook: injects a frame as if it had just been captured from the active source.</summary>
    public void PublishFrame(CapturedFrame frame)
    {
        _buffer.Publish(frame);
        FrameArrived?.Invoke(this, new FrameArrivedEventArgs(frame));
    }

    /// <summary>Test/dev hook: simulates the active source becoming unavailable (window closed, monitor disconnected).</summary>
    public void SimulateSourceLost()
    {
        if (State != CaptureSourceState.Running)
        {
            return;
        }

        State = CaptureSourceState.Lost;
        SourceLost?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        State = CaptureSourceState.Stopped;
        return ValueTask.CompletedTask;
    }
}
