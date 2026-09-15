namespace NBA.OCR.Tests;

public class NullScoreboardOcrEngineTests
{
    [Fact]
    public void Recognize_AlwaysReturnsNoLines()
    {
        var engine = new NullScoreboardOcrEngine();
        var pixels = new byte[4 * 4 * 4];

        var lines = engine.Recognize(pixels, 4, 4, 4 * 4);

        Assert.Empty(lines);
    }
}
