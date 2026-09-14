namespace NBA.Vision.Tests;

public class NullPlayerDetectorTests
{
    [Fact]
    public void Detect_AlwaysReturnsZeroDetections()
    {
        var detector = new NullPlayerDetector();
        var pixels = new byte[4 * 4 * 4];

        var detections = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Empty(detections);
    }
}
