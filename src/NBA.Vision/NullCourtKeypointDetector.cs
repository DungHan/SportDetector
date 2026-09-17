namespace NBA.Vision;

/// <summary>
/// Degraded-path detector used when no trained court-keypoint model file is available yet for a sport (see
/// design.md's "No trained court keypoint model exists yet" risk). Always returns zero keypoints, which
/// <see cref="CourtCalibrationCoordinator"/> then reports as "not enough points" - the manual calibration
/// fallback remains available.
/// </summary>
public sealed class NullCourtKeypointDetector(SportType sport) : ICourtKeypointDetector
{
    public SportType Sport { get; } = sport;

    public float KeypointConfidenceThreshold { get; set; } = 0.5f;

    public float DetectionConfidenceThreshold { get; set; } = 0.5f;

    public IReadOnlyList<DetectedKeypoint> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) => [];
}
