namespace NBA.Capture.Testing;

/// <summary>Fixed-list <see cref="ICaptureSourceEnumerator"/> for tests and non-Windows UI development - see <see cref="FakeFrameSource"/>.</summary>
public sealed class FakeCaptureSourceEnumerator(IReadOnlyList<CaptureSourceDescriptor> sources) : ICaptureSourceEnumerator
{
    public IReadOnlyList<CaptureSourceDescriptor> EnumerateSources() => sources;
}
