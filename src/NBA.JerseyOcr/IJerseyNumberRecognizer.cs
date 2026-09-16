namespace NBA.JerseyOcr;

/// <summary>Recognizes a player's printed jersey number from one tracked player's cropped image region - runs on every processed frame, per track (ocr/jersey-number-recognition spec).</summary>
public interface IJerseyNumberRecognizer
{
    /// <summary>
    /// Attempts recognition against the sub-rectangle <paramref name="cropLeft"/>/<paramref name="cropTop"/>/<paramref name="cropRight"/>/<paramref name="cropBottom"/>
    /// (source-space pixel coordinates, not necessarily clamped to the frame) of the given frame. Returns
    /// "unrecognized" (<see cref="JerseyNumberRecognitionResult.Number"/> null) rather than a low-confidence guess.
    /// </summary>
    JerseyNumberRecognitionResult Recognize(
        ReadOnlySpan<byte> bgra8Pixels,
        int width,
        int height,
        int stride,
        double cropLeft,
        double cropTop,
        double cropRight,
        double cropBottom);
}
