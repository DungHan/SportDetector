namespace NBA.Vision;

public readonly record struct DetectedKeypoint(string LandmarkName, ImagePoint Position, float Confidence);

/// <summary>Detects a sport's court landmarks in a frame, bound to one sport's geometry at construction (one model = one sport, per design.md).</summary>
public interface ICourtKeypointDetector
{
    SportType Sport { get; }

    /// <summary>Per-landmark confidence floor - live-adjustable so the UI can loosen/tighten detection without re-loading the model.</summary>
    float KeypointConfidenceThreshold { get; set; }

    /// <summary>Whole-court "is there a court in this frame at all" confidence floor - live-adjustable, same rationale as <see cref="KeypointConfidenceThreshold"/>.</summary>
    float DetectionConfidenceThreshold { get; set; }

    /// <summary>Returns only landmarks detected above the implementation's confidence threshold - may return fewer than the geometry's full landmark set, including none.</summary>
    IReadOnlyList<DetectedKeypoint> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride);
}
