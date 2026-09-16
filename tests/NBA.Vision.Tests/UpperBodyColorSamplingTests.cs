namespace NBA.Vision.Tests;

public class UpperBodyColorSamplingTests
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
    public void UpperBodyRectangle_NormalBox_NarrowsToTopFractionOfHeightFullWidth()
    {
        var rect = UpperBodyColorSampling.UpperBodyRectangle(left: 10, top: 20, right: 30, bottom: 120);

        Assert.Equal(10, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(30, rect.Right);
        Assert.Equal(60, rect.Bottom); // 20 + (100 * 0.4)
    }

    [Fact]
    public void UpperBodyRectangle_ZeroHeightBox_StaysZeroHeight()
    {
        var rect = UpperBodyColorSampling.UpperBodyRectangle(left: 5, top: 40, right: 15, bottom: 40);

        Assert.Equal(40, rect.Top);
        Assert.Equal(40, rect.Bottom);
    }

    [Fact]
    public void MeanColor_SolidColorRegionFullyInBounds_ReturnsExactColor()
    {
        var pixels = SolidBgra8(10, 10, b: 10, g: 150, r: 200);

        var color = UpperBodyColorSampling.MeanColor(pixels, width: 10, height: 10, stride: 10 * 4, left: 2, top: 2, right: 8, bottom: 6);

        Assert.True(color.HasValue);
        Assert.Equal(200, color!.Value.R);
        Assert.Equal(150, color.Value.G);
        Assert.Equal(10, color.Value.B);
    }

    [Fact]
    public void MeanColor_RegionPartiallyOutsideFrameBounds_ClampsBeforeSampling()
    {
        var pixels = SolidBgra8(10, 10, b: 40, g: 60, r: 80);

        // Extends past every edge of the 10x10 frame - must clamp to [0,10)x[0,10) rather than reading out of
        // bounds, and still report the (uniform) sampled color.
        var color = UpperBodyColorSampling.MeanColor(pixels, width: 10, height: 10, stride: 10 * 4, left: -5, top: -5, right: 15, bottom: 15);

        Assert.True(color.HasValue);
        Assert.Equal(80, color!.Value.R);
        Assert.Equal(60, color.Value.G);
        Assert.Equal(40, color.Value.B);
    }

    [Fact]
    public void MeanColor_RegionFullyOutsideFrameBounds_ReturnsNullSentinel()
    {
        var pixels = SolidBgra8(10, 10, b: 0, g: 0, r: 0);

        var color = UpperBodyColorSampling.MeanColor(pixels, width: 10, height: 10, stride: 10 * 4, left: 20, top: 20, right: 30, bottom: 30);

        Assert.Null(color);
    }

    [Fact]
    public void MeanColor_ZeroWidthRegion_ReturnsNullSentinel()
    {
        var pixels = SolidBgra8(10, 10, b: 0, g: 0, r: 0);

        var color = UpperBodyColorSampling.MeanColor(pixels, width: 10, height: 10, stride: 10 * 4, left: 5, top: 2, right: 5, bottom: 8);

        Assert.Null(color);
    }
}
