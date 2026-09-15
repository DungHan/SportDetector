namespace NBA.JerseyOcr;

/// <summary>
/// Degraded-path recognizer used when no trained jersey-number recognition model file is available yet (see
/// design.md's "No trained jersey-number recognition model exists yet" risk). Always reports "unrecognized",
/// so minimap markers keep falling back to the track ID rather than failing.
/// </summary>
public sealed class NullJerseyNumberRecognizer : IJerseyNumberRecognizer
{
    public JerseyNumberRecognitionResult Recognize(
        ReadOnlySpan<byte> bgra8Pixels,
        int width,
        int height,
        int stride,
        double cropLeft,
        double cropTop,
        double cropRight,
        double cropBottom) => new(Number: null, Confidence: 0f);
}
