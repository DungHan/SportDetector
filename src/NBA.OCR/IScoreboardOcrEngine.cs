namespace NBA.OCR;

/// <summary>
/// Recognizes text within an already-cropped scoreboard region. Implementations wrap whatever OCR facility the
/// host OS provides - see <c>Mac/MacVisionOcrEngine.cs</c> and <c>Windows/WindowsOcrEngine.cs</c> - so this
/// project has no ML model of its own and no basketball-specific knowledge (that lives in NBA.State).
/// </summary>
public interface IScoreboardOcrEngine
{
    IReadOnlyList<ScoreboardOcrLine> Recognize(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride);
}
