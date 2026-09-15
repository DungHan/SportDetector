namespace NBA.Vision;

/// <summary>Detects players in a frame, independent of sport classification or calibration - runs on a configurable cadence of captured frames, not necessarily every frame, and never cached per-source (per vision/player-detection spec).</summary>
public interface IPlayerDetector
{
    /// <summary>Returns only players detected above the implementation's confidence threshold, after duplicate-suppression - may return none.</summary>
    IReadOnlyList<PlayerDetection> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride);
}
