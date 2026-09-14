namespace NBA.Capture;

/// <summary>Lists the windows/screens currently available to capture.</summary>
public interface ICaptureSourceEnumerator
{
    /// <summary>
    /// Returns every currently open top-level window and every connected screen. Closed windows from a
    /// previous call are not included - each call reflects current OS state.
    /// </summary>
    IReadOnlyList<CaptureSourceDescriptor> EnumerateSources();
}
