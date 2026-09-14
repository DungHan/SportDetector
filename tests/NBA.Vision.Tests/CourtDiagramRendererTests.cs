using NBA.Vision;

namespace NBA.Vision.Tests;

public class CourtDiagramRendererTests
{
    private const double PixelsPerMeter = 10;

    [Theory]
    [InlineData("basketball")]
    [InlineData("soccer")]
    [InlineData("tennis")]
    [InlineData("american_football")]
    public void Render_ProducesImageSizedToSurfaceDimensions(string sportId)
    {
        Assert.True(CourtDiagramRegistry.TryGet(new SportType(sportId), out var spec));

        var image = CourtDiagramRenderer.Render(spec, PixelsPerMeter);

        Assert.Equal((int)Math.Round(spec.SurfaceLengthMeters * PixelsPerMeter), image.Width);
        Assert.Equal((int)Math.Round(spec.SurfaceWidthMeters * PixelsPerMeter), image.Height);
        Assert.Equal(image.Width * 4, image.Stride);
        Assert.Equal(image.Stride * image.Height, image.BgraPixels.Length);
    }

    [Theory]
    [InlineData("basketball")]
    [InlineData("soccer")]
    [InlineData("tennis")]
    [InlineData("american_football")]
    public void Render_DrawsBothSurfaceAndLineColors(string sportId)
    {
        Assert.True(CourtDiagramRegistry.TryGet(new SportType(sportId), out var spec));

        var image = CourtDiagramRenderer.Render(spec, PixelsPerMeter);

        Assert.True(ContainsPixel(image, spec.Colors.SurfaceHex), "Expected the surface color to appear somewhere in the rendered image.");
        Assert.True(ContainsPixel(image, spec.Colors.LineHex), "Expected the line color to appear somewhere in the rendered image.");
    }

    private static bool ContainsPixel(CourtDiagramImage image, string hex)
    {
        var (r, g, b) = (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));
        var pixels = image.BgraPixels;
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i] == b && pixels[i + 1] == g && pixels[i + 2] == r)
            {
                return true;
            }
        }

        return false;
    }
}
