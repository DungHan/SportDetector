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
    public void UpperBodyRectangle_StandingBox_NarrowsToCenteredTorsoBand()
    {
        // Width 20 (10 to 30), height 100 (20 to 120): aspect ratio 20/100 = 0.2, below the standing threshold
        // (0.45), so the standing fractions apply. Side margin 25% of 20 = 5 trimmed off each side -> [15, 25).
        // Top margin 18% of 100 = 18 -> top 38; band to 52% = 52 -> bottom 72.
        var rect = UpperBodyColorSampling.UpperBodyRectangle(left: 10, top: 20, right: 30, bottom: 120);

        Assert.True(rect.HasValue);
        Assert.Equal(15, rect!.Value.Left);
        Assert.Equal(38, rect.Value.Top);
        Assert.Equal(25, rect.Value.Right);
        Assert.Equal(72, rect.Value.Bottom);
    }

    [Fact]
    public void UpperBodyRectangle_StridingBox_UsesWiderTighterBand()
    {
        // Width 100 (0 to 100), height 200 (0 to 200): aspect ratio 100/200 = 0.5, in the striding band
        // (0.45 <= AR < 0.75) - a wide stride widens the box with an outstretched arm/leg, so the sampled
        // rectangle uses a wider side margin (35%) than the standing case to stay centered on the torso rather
        // than spanning the full width (see the type-level doc comment on why full-width sampling pulls in
        // court/crowd background instead of jersey fabric).
        var rect = UpperBodyColorSampling.UpperBodyRectangle(left: 0, top: 0, right: 100, bottom: 200);

        Assert.True(rect.HasValue);
        Assert.Equal(35, rect!.Value.Left);
        Assert.Equal(65, rect.Value.Right);
    }

    [Fact]
    public void UpperBodyRectangle_WideBoxAtOrAboveDiveThreshold_ReturnsNull()
    {
        // Width 150, height 100: aspect ratio 1.5, at/above the dive threshold (0.75) - too motion-blurred to
        // crop reliably, so the caller should skip color sampling for this frame rather than guess.
        var rect = UpperBodyColorSampling.UpperBodyRectangle(left: 0, top: 0, right: 150, bottom: 100);

        Assert.Null(rect);
    }

    [Fact]
    public void UpperBodyRectangle_ZeroHeightBox_ReturnsNull()
    {
        // A degenerate (zero-height) box has no meaningful aspect ratio - treated the same as a dive: skip.
        var rect = UpperBodyColorSampling.UpperBodyRectangle(left: 5, top: 40, right: 15, bottom: 40);

        Assert.Null(rect);
    }

    [Fact]
    public void DominantColor_SolidColorRegionFullyInBounds_ReturnsExactColor()
    {
        var pixels = SolidBgra8(10, 10, b: 10, g: 150, r: 200);

        var color = UpperBodyColorSampling.DominantColor(pixels, width: 10, height: 10, stride: 10 * 4, left: 2, top: 2, right: 8, bottom: 6);

        Assert.True(color.HasValue);
        Assert.Equal(200, color!.Value.R);
        Assert.Equal(150, color.Value.G);
        Assert.Equal(10, color.Value.B);
    }

    [Fact]
    public void DominantColor_RegionPartiallyOutsideFrameBounds_ClampsBeforeSampling()
    {
        var pixels = SolidBgra8(10, 10, b: 40, g: 60, r: 80);

        // Extends past every edge of the 10x10 frame - must clamp to [0,10)x[0,10) rather than reading out of
        // bounds, and still report the (uniform) sampled color.
        var color = UpperBodyColorSampling.DominantColor(pixels, width: 10, height: 10, stride: 10 * 4, left: -5, top: -5, right: 15, bottom: 15);

        Assert.True(color.HasValue);
        Assert.Equal(80, color!.Value.R);
        Assert.Equal(60, color.Value.G);
        Assert.Equal(40, color.Value.B);
    }

    [Fact]
    public void DominantColor_MajorityJerseyColorOutvotesMinorityContaminant_ReturnsMajorityColor()
    {
        // A jersey-sized block of one color plus a smaller patch of a distinctly different one (standing in for
        // skin/shadow/background bleeding into the sampled rectangle) - the majority bucket must win, unlike a
        // plain mean, which would blend the two into a color neither region actually has.
        var pixels = SolidBgra8(10, 10, b: 0, g: 0, r: 200); // "jersey" red fills the whole 10x10 frame.
        for (var y = 0; y < 3; y++) // Overwrite a 3x10 strip with a contaminant color - a clear minority of the 10x10 region.
        {
            for (var x = 0; x < 10; x++)
            {
                var offset = ((y * 10) + x) * 4;
                pixels[offset] = 200; // B
                pixels[offset + 1] = 200; // G
                pixels[offset + 2] = 50; // R - pale blue-gray, nothing like the red jersey.
            }
        }

        var color = UpperBodyColorSampling.DominantColor(pixels, width: 10, height: 10, stride: 10 * 4, left: 0, top: 0, right: 10, bottom: 10);

        Assert.True(color.HasValue);
        Assert.Equal(200, color!.Value.R);
        Assert.Equal(0, color.Value.G);
        Assert.Equal(0, color.Value.B);
    }

    [Fact]
    public void DominantColor_RegionFullyOutsideFrameBounds_ReturnsNullSentinel()
    {
        var pixels = SolidBgra8(10, 10, b: 0, g: 0, r: 0);

        var color = UpperBodyColorSampling.DominantColor(pixels, width: 10, height: 10, stride: 10 * 4, left: 20, top: 20, right: 30, bottom: 30);

        Assert.Null(color);
    }

    [Fact]
    public void DominantColor_ZeroWidthRegion_ReturnsNullSentinel()
    {
        var pixels = SolidBgra8(10, 10, b: 0, g: 0, r: 0);

        var color = UpperBodyColorSampling.DominantColor(pixels, width: 10, height: 10, stride: 10 * 4, left: 5, top: 2, right: 5, bottom: 8);

        Assert.Null(color);
    }
}
