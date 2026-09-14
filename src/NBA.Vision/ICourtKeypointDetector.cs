namespace NBA.Vision;

public readonly record struct DetectedKeypoint(string LandmarkName, ImagePoint Position, float Confidence);

/// <summary>Detects a sport's court landmarks in a frame, bound to one sport's geometry at construction (one model = one sport, per design.md).</summary>
public interface ICourtKeypointDetector
{
    SportType Sport { get; }

    /// <summary>Returns only landmarks detected above the implementation's confidence threshold - may return fewer than the geometry's full landmark set, including none.</summary>
    IReadOnlyList<DetectedKeypoint> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride);
}
