using NBA.Capture;
using NBA.Capture.Testing;

namespace NBA.App.Services;

/// <summary>
/// Non-Windows build (e.g. this repo's macOS dev machine): capture is genuinely unavailable (design.md's
/// "Cross-platform capture" Non-Goal), so the app runs against the in-memory fakes instead of not building at
/// all - lets the UI shell itself be developed/verified without a Windows host. See CapturePlatform.Windows.cs
/// for the real counterpart.
/// </summary>
public static class CapturePlatform
{
    public static IFrameSource CreateFrameSource() => new FakeFrameSource();

    public static ICaptureSourceEnumerator CreateSourceEnumerator() => new FakeCaptureSourceEnumerator(
    [
        new CaptureSourceDescriptor("fake:demo-window", "Demo Window (fake source - no Windows host)", CaptureSourceKind.Window, 1, "demo"),
        new CaptureSourceDescriptor("fake:demo-screen", "Demo Screen (fake source - no Windows host)", CaptureSourceKind.Screen, 2, null),
    ]);
}
