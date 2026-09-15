namespace NBA.OCR;

/// <summary>
/// Degraded-path engine used when no platform-native OCR backend is available (unsupported OS, or the Mac
/// helper tool/Windows OCR language pack is missing) - always returns no recognized text, so the scoreboard
/// panel stays at its placeholder rather than failing. Mirrors NBA.Vision's Null*Detector convention.
/// </summary>
public sealed class NullScoreboardOcrEngine : IScoreboardOcrEngine
{
    public IReadOnlyList<ScoreboardOcrLine> Recognize(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) => [];
}
