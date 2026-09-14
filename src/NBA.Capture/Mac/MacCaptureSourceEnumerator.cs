using NBA.Capture.Mac.Interop;

namespace NBA.Capture.Mac;

/// <summary>
/// Enumerates capturable windows and displays via CoreGraphics (<see cref="CoreGraphicsInterop"/>) - the Mac
/// counterpart of <c>WindowsCaptureSourceEnumerator</c>. See design.md's Mac capture discussion: validated by
/// a throwaway P/Invoke spike before being implemented here.
/// </summary>
public sealed class MacCaptureSourceEnumerator : ICaptureSourceEnumerator
{
    public IReadOnlyList<CaptureSourceDescriptor> EnumerateSources()
    {
        var sources = new List<CaptureSourceDescriptor>();
        sources.AddRange(CoreGraphicsInterop.EnumerateWindows());
        sources.AddRange(CoreGraphicsInterop.EnumerateDisplays());
        return sources;
    }
}
