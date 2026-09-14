namespace NBA.Vision;

/// <summary>
/// Degraded-path detector used when no trained player-detection model file is available yet (see design.md's
/// "No trained player-detection model exists yet" risk, in openspec/changes/add-player-detection/). Always
/// returns zero detections, so the raw view simply renders no player boxes rather than failing.
/// </summary>
public sealed class NullPlayerDetector : IPlayerDetector
{
    public IReadOnlyList<PlayerDetection> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) => [];
}
