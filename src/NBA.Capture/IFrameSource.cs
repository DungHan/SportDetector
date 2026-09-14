namespace NBA.Capture;

public sealed class FrameArrivedEventArgs(CapturedFrame frame) : EventArgs
{
    public CapturedFrame Frame { get; } = frame;
}

public sealed class SourceChangedEventArgs(CaptureSourceDescriptor? previousSource, CaptureSourceDescriptor newSource) : EventArgs
{
    public CaptureSourceDescriptor? PreviousSource { get; } = previousSource;
    public CaptureSourceDescriptor NewSource { get; } = newSource;
}

/// <summary>
/// A live stream of frames from one capture source (a window or a screen), switchable at runtime.
/// Implements the frame-capture spec: latest-frame-wins delivery, runtime source switching without an
/// application restart, and source-lost detection. See design.md's "Capture: Windows.Graphics.Capture via
/// a pluggable IFrameSource" - NBA.App depends only on this interface, never on the Windows-specific backend.
/// </summary>
public interface IFrameSource : IAsyncDisposable
{
    CaptureSourceState State { get; }

    /// <summary>The source frames are currently (or were most recently) being captured from, or null before the first start.</summary>
    CaptureSourceDescriptor? ActiveSource { get; }

    /// <summary>
    /// Starts capturing from <paramref name="source"/>. If a different source is already active, it is stopped
    /// first and <see cref="SourceChanged"/> is raised - satisfies "Switch capture source at runtime" without
    /// requiring the caller to call <see cref="StopAsync"/> first.
    /// </summary>
    Task StartAsync(CaptureSourceDescriptor source, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>The most recently captured frame, or null if none has arrived yet for the active source.</summary>
    CapturedFrame? TryGetLatestFrame();

    /// <summary>
    /// Raised when a new frame becomes the latest frame. Consumers slower than the capture rate will not see
    /// every raised event processed before the next one supersedes it - always read <see cref="TryGetLatestFrame"/>
    /// rather than assuming the event's frame is still current by the time it's handled.
    /// </summary>
    event EventHandler<FrameArrivedEventArgs>? FrameArrived;

    /// <summary>Raised when <see cref="StartAsync"/> switches the active source while already running.</summary>
    event EventHandler<SourceChangedEventArgs>? SourceChanged;

    /// <summary>Raised when the active source becomes unavailable (window closed, monitor disconnected).</summary>
    event EventHandler? SourceLost;
}
