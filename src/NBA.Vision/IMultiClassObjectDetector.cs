namespace NBA.Vision;

/// <summary>
/// Detects players and every other on-court/broadcast-overlay object class from one shared model inference
/// pass - supersedes <see cref="IPlayerDetector"/> at the `MainWindowViewModel` wiring layer (see design.md's
/// "A new IMultiClassObjectDetector interface... supersedes IPlayerDetector" decision), so both `Player` and
/// non-`Player` detections come from a single `Detect(...)` call rather than two separate inference passes.
/// </summary>
public interface IMultiClassObjectDetector
{
    /// <summary>Returns every class's detections above the implementation's confidence threshold, after per-class duplicate-suppression - either list may be empty.</summary>
    MultiClassDetectionResult Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride);
}
