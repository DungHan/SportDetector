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
///
/// Capturing and raising <see cref="FrameArrived"/> run as two independent loops (<see cref="PollLoopAsync"/>
/// and <see cref="DispatchLoopAsync"/>) rather than one - a subscriber whose handler is slower than 33ms (e.g.
/// running ONNX inference) used to block this poll loop's next <c>CGWindowListCreateImage</c>/
/// <c>CGDisplayCreateImage</c> call until it returned, so capture itself slowed down to match the slowest
/// subscriber and a backlog of screen-staleness built up continuously during playback (only visibly "catching
/// up" once the on-screen content stopped changing, e.g. the user pausing video, and the backlog stopped
/// growing). Splitting the two means capture always runs at the full poll rate and <see cref="DispatchLoopAsync"/>
/// always hands a busy subscriber the *latest* buffered frame once it's free, silently dropping whatever was
/// captured in between rather than queuing it - so the subscriber is always at most one poll interval stale,
/// never an accumulating backlog.
/// </summary>
public sealed class MacFrameSource : IFrameSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(33); // ~30fps
    private const int ConsecutiveFailuresBeforeSourceLost = 3;

    private readonly LatestFrameBuffer<CapturedFrame> _buffer = new();
    private readonly Lock _lifecycleLock = new();

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private Task? _dispatchTask;

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
            _dispatchTask = Task.Run(() => DispatchLoopAsync(_pollCts.Token));

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

    /// <summary>Cancels and awaits the current poll and dispatch loops (if any), so no more <see cref="_buffer"/> writes or <see cref="FrameArrived"/> raises are in flight once this returns.</summary>
    private async Task StopPollLoopAsync()
    {
        CancellationTokenSource? cts;
        Task? pollTask;
        Task? dispatchTask;

        lock (_lifecycleLock)
        {
            cts = _pollCts;
            pollTask = _pollTask;
            dispatchTask = _dispatchTask;
            _pollCts = null;
            _pollTask = null;
            _dispatchTask = null;
        }

        cts?.Cancel();

        foreach (var task in new[] { pollTask, dispatchTask })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the loop observes cancellation and unwinds.
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
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync()/StartAsync() switching away from this source.
        }
    }

    /// <summary>
    /// Raises <see cref="FrameArrived"/> once per available frame, independent of <see cref="PollLoopAsync"/>'s
    /// capture cadence (see type-level doc comment). Always dispatches whatever is currently latest in
    /// <see cref="_buffer"/> rather than a queue: if a subscriber is still busy with the previous frame when
    /// several new ones land, it picks up the newest one next and the rest are silently dropped - the same
    /// latest-frame-wins contract <see cref="LatestFrameBuffer{T}"/> already gives <see cref="TryGetLatestFrame"/>,
    /// now also applied to event delivery. Falls back to <see cref="LatestFrameBuffer{T}.WaitForNextAsync"/>
    /// (rather than busy-polling) whenever it has already caught up to the newest frame, so an idle subscriber
    /// isn't woken redundantly for a frame it already saw.
    /// </summary>
    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        CapturedFrame? lastDispatched = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = _buffer.TryGetLatest();
                if (frame is null || ReferenceEquals(frame, lastDispatched))
                {
                    frame = await _buffer.WaitForNextAsync(cancellationToken).ConfigureAwait(false);
                }

                lastDispatched = frame;

                try
                {
                    FrameArrived?.Invoke(this, new FrameArrivedEventArgs(frame));
                }
                catch (Exception ex)
                {
                    // A subscriber (e.g. a detector's inference call) throwing must not kill this dispatch loop -
                    // there's no push-based recapture on macOS, so once this loop exits, frames stop arriving
                    // for good until the source is restarted. See MainWindowViewModel.OnFrameArrived, which
                    // guards its own detector calls but can't guard against subscribers added elsewhere.
                    Console.WriteLine($"[frame-arrived] subscriber threw, continuing dispatch loop: {ex.Message}");
                }
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
