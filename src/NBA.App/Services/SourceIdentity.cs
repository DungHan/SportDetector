using NBA.Capture;

namespace NBA.App.Services;

/// <summary>
/// Derives a stable-across-sessions <see cref="NBA.Vision.SourceProfile"/> key from a capture source, since
/// <see cref="CaptureSourceDescriptor.Id"/> wraps a raw OS handle that is only valid for the current
/// enumeration. Uses process name (falling back to display name) + source kind - see design.md's open
/// question on the exact composite key strategy; this is an initial, revisitable choice.
/// </summary>
public static class SourceIdentity
{
    public static string DeriveKey(CaptureSourceDescriptor source) =>
        $"{source.Kind}:{source.ProcessName ?? source.DisplayName}";
}
