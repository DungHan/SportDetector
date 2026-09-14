using NBA.Capture;
using NBA.Capture.Windows;

namespace NBA.App.Services;

/// <summary>Windows build: uses the real Windows.Graphics.Capture backend. See CapturePlatform.Fallback.cs for the non-Windows counterpart.</summary>
public static class CapturePlatform
{
    public static IFrameSource CreateFrameSource() => new WindowsGraphicsCaptureFrameSource();

    public static ICaptureSourceEnumerator CreateSourceEnumerator() => new WindowsCaptureSourceEnumerator();
}
