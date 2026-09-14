using NBA.Capture.Mac.Interop;

namespace NBA.Capture.Mac;

/// <summary>
/// CoreGraphics-backed <see cref="IFrameSource"/> for macOS (see design.md's Mac capture discussion and the
/// spike that validated this approach). Unlike <c>WindowsGraphicsCaptureFrameSource</c>, CoreGraphics has no
/// push-based frame-arrived callback - <see cref="NativeMethods.CGWindowListCreateImage"/>/
/// <see cref="NativeMethods.CGDisplayCreateImage"/> each return one snapshot per call, so this polls on a
/// timer (~30fps) instead. Still satisfies the same frame-capture contract as the Windows backend: runtime
/// source switching without requiring <see cref="StopAsync"/> first, source-lost detection, and
/// latest-frame-wins delivery via <see cref="LatestFrameBuffer{T}"/>.
/// </summary>
public sealed class MacFrameSource : IFrameSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(33); // ~30fps
    private const int ConsecutiveFailuresBeforeSourceLost = 3;

    private readonly LatestFrameBuffer<CapturedFrame> _buffer = new();
    private readonly Lock _lifecycleLock = new();

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public CaptureSourceState State { get; private set; } = CaptureSourceState.NotStarted;

    public CaptureSourceDescriptor? ActiveSource { get; private set; }

    public event EventHandler<FrameArrivedEventArgs>? FrameArrived;

    public event EventHandler<SourceChangedEventArgs>? SourceChanged;

    public event EventHandler? SourceLost;

    public async Task StartAsync(CaptureSourceDescriptor source, CancellationToken cancellationToken = default)
    {
        CaptureSourceDescriptor? previousSource;
        bool wasRunning;

        lock (_lifecycleLock)
        {
            previousSource = ActiveSource;
            wasRunning = State is CaptureSourceState.Running or CaptureSourceState.Lost;
        }

        // Fully tear down the previous poll loop - including waiting for its last in-flight iteration to
        // observe cancellation - before clearing the buffer or starting the new one. Clearing/starting first
        // and cancelling after left a window where the old loop could still call _buffer.Publish() with a
        // stale frame after the "clear" had already run, so a fresh TryGetLatestFrame() right after this
        // method returned could see the previous source's frame (caught by a verification run against the
        // real capture backend, not by the fast synchronous FakeFrameSource contract tests).
        await StopPollLoopAsync().ConfigureAwait(false);

        lock (_lifecycleLock)
        {
            State = CaptureSourceState.Starting;
            ActiveSource = source;
            _buffer.Clear();

            _pollCts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(source, _pollCts.Token));

            State = CaptureSourceState.Running;
        }

        if (wasRunning && previousSource is not null)
        {
            SourceChanged?.Invoke(this, new SourceChangedEventArgs(previousSource, source));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopPollLoopAsync().ConfigureAwait(false);

        lock (_lifecycleLock)
        {
            State = CaptureSourceState.Stopped;
            _buffer.Clear();
        }
    }

    /// <summary>Cancels and awaits the current poll loop (if any), so no more <see cref="_buffer"/> writes are in flight once this returns.</summary>
    private async Task StopPollLoopAsync()
    {
        CancellationTokenSource? cts;
        Task? task;

        lock (_lifecycleLock)
        {
            cts = _pollCts;
            task = _pollTask;
            _pollCts = null;
            _pollTask = null;
        }

        cts?.Cancel();
        if (task is not null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the poll loop observes cancellation and unwinds.
            }
        }

        cts?.Dispose();
    }

    public CapturedFrame? TryGetLatestFrame() => _buffer.TryGetLatest();

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task PollLoopAsync(CaptureSourceDescriptor source, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        var consecutiveFailures = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var frame = source.Kind == CaptureSourceKind.Window
                    ? CoreGraphicsInterop.CaptureWindow((uint)source.Handle)
                    : CoreGraphicsInterop.CaptureDisplay((uint)source.Handle);

                if (frame is null)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= ConsecutiveFailuresBeforeSourceLost)
                    {
                        ReportSourceLost();
                        return;
                    }

                    continue;
                }

                consecutiveFailures = 0;
                _buffer.Publish(frame);
                FrameArrived?.Invoke(this, new FrameArrivedEventArgs(frame));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync()/StartAsync() switching away from this source.
        }
    }

    private void ReportSourceLost()
    {
        lock (_lifecycleLock)
        {
            if (State != CaptureSourceState.Running)
            {
                return;
            }

            State = CaptureSourceState.Lost;
        }

        SourceLost?.Invoke(this, EventArgs.Empty);
    }
}
