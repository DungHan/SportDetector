namespace NBA.Vision;

/// <summary>
/// Degraded-path detector used when no trained player-detection model file is available yet - mirrors
/// <see cref="NullPlayerDetector"/>'s posture. Always returns zero detections for every class, so neither the
/// raw view's player/tracked boxes nor its non-`Player` overlay boxes render anything rather than failing.
/// </summary>
public sealed class NullMultiClassObjectDetector : IMultiClassObjectDetector
{
    public MultiClassDetectionResult Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) =>
        new(Players: [], Others: []);
}
