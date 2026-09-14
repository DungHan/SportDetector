namespace NBA.Capture;

/// <summary>
/// Identifies one capturable window or screen, as returned by <see cref="ICaptureSourceEnumerator"/>.
/// <see cref="Id"/> is only stable for the lifetime of the process/enumeration (it wraps a raw OS handle);
/// callers that need a stable cross-session identity (e.g. NBA.Vision's SourceProfile) should derive their
/// own key from <see cref="ProcessName"/>/<see cref="DisplayName"/> rather than persisting <see cref="Id"/>.
/// </summary>
/// <param name="Id">Ephemeral, enumeration-scoped identifier (e.g. "window:12345").</param>
/// <param name="DisplayName">Human-readable label for source-picker UIs (window title or monitor name).</param>
/// <param name="Kind">Whether this is a window or a full screen/monitor.</param>
/// <param name="Handle">The raw OS handle (HWND for a window, HMONITOR for a screen) backing this source.</param>
/// <param name="ProcessName">Best-effort owning process name, when known (e.g. "chrome", "steam").</param>
public sealed record CaptureSourceDescriptor(
    string Id,
    string DisplayName,
    CaptureSourceKind Kind,
    nint Handle,
    string? ProcessName = null);
