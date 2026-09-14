namespace NBA.Capture;

public enum CaptureSourceState
{
    /// <summary>No source has ever been started.</summary>
    NotStarted,

    /// <summary>A capture session is being torn down/stood up for a newly selected source.</summary>
    Starting,

    /// <summary>Frames are being delivered from <see cref="IFrameSource.ActiveSource"/>.</summary>
    Running,

    /// <summary>The active source became unavailable (window closed, monitor disconnected). See frame-capture spec's "Resilience to source becoming unavailable".</summary>
    Lost,

    /// <summary><see cref="IFrameSource.StopAsync"/> was called explicitly.</summary>
    Stopped,
}
