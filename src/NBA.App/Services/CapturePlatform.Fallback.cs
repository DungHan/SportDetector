using NBA.Capture;
using NBA.Capture.Mac;
using NBA.Capture.Testing;

namespace NBA.App.Services;

/// <summary>
/// Non-Windows build TFM (net10.0). On macOS this uses the real CoreGraphics-backed capture in
/// <c>NBA.Capture.Mac</c> (see design.md's Mac capture discussion - originally a Non-Goal, later validated
/// via a P/Invoke spike and implemented as a second real backend alongside Windows.Graphics.Capture). On any
/// other OS this TFM runs on (e.g. Linux, where no capture backend exists), it falls back to the in-memory
/// fakes so the UI shell can still be developed/verified. See CapturePlatform.Windows.cs for the Windows build.
/// </summary>
public static class CapturePlatform
{
    public static IFrameSource CreateFrameSource() =>
        OperatingSystem.IsMacOS() ? new MacFrameSource() : new FakeFrameSource();

    public static ICaptureSourceEnumerator CreateSourceEnumerator() =>
        OperatingSystem.IsMacOS() ? new MacCaptureSourceEnumerator() : new FakeCaptureSourceEnumerator(
        [
            new CaptureSourceDescriptor("fake:demo-window", "Demo Window (fake source - no capture backend)", CaptureSourceKind.Window, 1, "demo"),
            new CaptureSourceDescriptor("fake:demo-screen", "Demo Screen (fake source - no capture backend)", CaptureSourceKind.Screen, 2, null),
        ]);
}
