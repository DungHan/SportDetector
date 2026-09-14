using NBA.Capture;

namespace NBA.App.Services;

/// <summary>
/// Derives a stable-across-sessions <see cref="NBA.Vision.SourceProfile"/> key from a capture source, since
/// <see cref="CaptureSourceDescriptor.Id"/> wraps a raw OS handle that is only valid for the current
/// enumeration. Uses kind + process name + display name (title) - see design.md's open question on the exact
/// composite key strategy; this is an initial, revisitable choice.
/// </summary>
public static class SourceIdentity
{
    /// <remarks>
    /// Process name alone (the original composite) collapses every window of the same app into one cache
    /// entry - e.g. every Chrome window/tab, however different its content, shared "Window:Google Chrome" and
    /// so reused whichever sport classification/calibration happened to be cached from the *first* Chrome
    /// window ever selected, silently skipping reclassification for every different one after that. Confirmed
    /// live: selecting a Chrome window showing an NBA broadcast reused a stale "unknown" classification cached
    /// from an earlier, unrelated Chrome window. DisplayName (the window title) is included specifically to
    /// distinguish windows within the same app - see design.md's known trade-off ("titles can change, e.g. a
    /// YouTube tab title changes with the video") - a changed title causing a harmless reclassification is
    /// preferable to two different windows/tabs silently sharing one cached result.
    /// </remarks>
    public static string DeriveKey(CaptureSourceDescriptor source) =>
        $"{source.Kind}:{source.ProcessName ?? "unknown-process"}:{source.DisplayName}";
}
