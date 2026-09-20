namespace NBA.Vision.Tests;

public class SceneCutDetectorTests
{
    private static byte[] SolidBgra8(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    [Fact]
    public void ComputeGridSignature_SolidColorFrame_EveryCellReportsThatColor()
    {
        var pixels = SolidBgra8(64, 64, b: 0, g: 128, r: 255);

        var signature = SceneCutDetector.ComputeGridSignature(pixels, 64, 64, 64 * 4);

        Assert.Equal(SceneCutDetector.GridSize * SceneCutDetector.GridSize * 3, signature.Length);
        for (var i = 0; i < signature.Length; i += 3)
        {
            Assert.Equal(0.0, signature[i], precision: 3); // B
            Assert.Equal(128.0 / 255.0, signature[i + 1], precision: 3); // G
            Assert.Equal(1.0, signature[i + 2], precision: 3); // R
        }
    }

    [Fact]
    public void IsCut_IdenticalSignatures_ReturnsFalse()
    {
        var pixels = SolidBgra8(64, 64, b: 10, g: 20, r: 30);
        var signature = SceneCutDetector.ComputeGridSignature(pixels, 64, 64, 64 * 4);

        Assert.False(SceneCutDetector.IsCut(signature, signature));
    }

    [Fact]
    public void IsCut_SameSceneMinorChange_ReturnsFalse()
    {
        // A small camera pan/lighting flicker changes color slightly, but must not read as a hard cut.
        var previous = SceneCutDetector.ComputeGridSignature(SolidBgra8(64, 64, b: 100, g: 100, r: 100), 64, 64, 64 * 4);
        var current = SceneCutDetector.ComputeGridSignature(SolidBgra8(64, 64, b: 110, g: 105, r: 108), 64, 64, 64 * 4);

        Assert.False(SceneCutDetector.IsCut(previous, current));
    }

    [Fact]
    public void IsCut_CompletelyDifferentScene_ReturnsTrue()
    {
        // A hard cut to a totally different arena/court color scheme (dark wood vs. bright blue) - the kind
        // of change a highlight reel produces between clips of different games.
        var previous = SceneCutDetector.ComputeGridSignature(SolidBgra8(64, 64, b: 20, g: 40, r: 90), 64, 64, 64 * 4);
        var current = SceneCutDetector.ComputeGridSignature(SolidBgra8(64, 64, b: 200, g: 180, r: 30), 64, 64, 64 * 4);

        Assert.True(SceneCutDetector.IsCut(previous, current));
    }

    [Fact]
    public void IsCut_MismatchedSignatureLengths_Throws()
    {
        var full = SceneCutDetector.ComputeGridSignature(SolidBgra8(64, 64, b: 0, g: 0, r: 0), 64, 64, 64 * 4);
        var truncated = full[..^3];

        Assert.Throws<ArgumentException>(() => SceneCutDetector.IsCut(full, truncated));
    }
}
