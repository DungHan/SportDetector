namespace NBA.Vision.Tests;

public class NullMultiClassObjectDetectorTests
{
    [Fact]
    public void Detect_AlwaysReturnsEmptyPlayersAndOthers()
    {
        var detector = new NullMultiClassObjectDetector();
        var pixels = new byte[4 * 4 * 4];

        var result = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Empty(result.Players);
        Assert.Empty(result.Others);
    }
}
