using NBA.Vision;

namespace NBA.Vision.Tests;

public class PlaybackRegionDetectorTests
{
    private const int Width = 320;
    private const int Height = 180;
    private const int Stride = Width * 4;

    // 320x180 with the default 32x18 grid gives exactly 10px-square cells, so rectangles aligned to multiples
    // of 10 land on whole cells with no partial-cell rounding to account for in the expected bounds below.
    private static byte[] CreateFrame(byte backgroundValue, params (int X, int Y, int W, int H, byte Value)[] regions)
    {
        var pixels = new byte[Stride * Height];
        Array.Fill(pixels, backgroundValue);

        foreach (var (x, y, w, h, value) in regions)
        {
            for (var row = y; row < y + h; row++)
            {
                for (var col = x; col < x + w; col++)
                {
                    var offset = (row * Stride) + (col * 4);
                    pixels[offset] = value;
                    pixels[offset + 1] = value;
                    pixels[offset + 2] = value;
                    pixels[offset + 3] = 255;
                }
            }
        }

        return pixels;
    }

    private static void AccumulateFlickeringRegion(PlaybackRegionDetector detector, int frameCount, params (int X, int Y, int W, int H)[] regions)
    {
        for (var i = 0; i < frameCount; i++)
        {
            var value = (byte)(i % 2 == 0 ? 0 : 255);
            var frame = CreateFrame(backgroundValue: 20, Array.ConvertAll(regions, r => (r.X, r.Y, r.W, r.H, value)));
            detector.Accumulate(frame, Width, Height, Stride);
        }
    }

    [Fact]
    public void TryGetRegion_BeforeMinimumFrames_ReturnsFalse()
    {
        var detector = new PlaybackRegionDetector(gridWidth: 32, gridHeight: 18, minimumFrames: 10);

        AccumulateFlickeringRegion(detector, frameCount: 5, (100, 50, 100, 60));

        Assert.False(detector.TryGetRegion(out _));
    }

    [Fact]
    public void TryGetRegion_WithFlickeringRegion_DetectsItsBounds()
    {
        var detector = new PlaybackRegionDetector(gridWidth: 32, gridHeight: 18, minimumFrames: 10);

        AccumulateFlickeringRegion(detector, frameCount: 20, (100, 50, 100, 60));

        Assert.True(detector.TryGetRegion(out var region));
        Assert.Equal(100.0 / Width, region.X, precision: 3);
        Assert.Equal(50.0 / Height, region.Y, precision: 3);
        Assert.Equal(100.0 / Width, region.Width, precision: 3);
        Assert.Equal(60.0 / Height, region.Height, precision: 3);
    }

    [Fact]
    public void TryGetRegion_IgnoresIsolatedFlickeringCell_NotConnectedToMainRegion()
    {
        var detector = new PlaybackRegionDetector(gridWidth: 32, gridHeight: 18, minimumFrames: 10);

        // Main region: 10x6 cells. Isolated "blinking icon" elsewhere: a single, disconnected cell.
        AccumulateFlickeringRegion(detector, frameCount: 20, (100, 50, 100, 60), (300, 170, 10, 10));

        Assert.True(detector.TryGetRegion(out var region));
        Assert.Equal(100.0 / Width, region.X, precision: 3);
        Assert.Equal(50.0 / Height, region.Y, precision: 3);
        Assert.Equal(100.0 / Width, region.Width, precision: 3);
        Assert.Equal(60.0 / Height, region.Height, precision: 3);
    }

    [Fact]
    public void TryGetRegion_QuietPatchInsideMovingRegion_DoesNotShrinkBounds()
    {
        var detector = new PlaybackRegionDetector(gridWidth: 32, gridHeight: 18, minimumFrames: 10);

        for (var i = 0; i < 20; i++)
        {
            var value = (byte)(i % 2 == 0 ? 0 : 255);

            // Main 10x6-cell region flickers every frame, except a 2x2-cell patch painted a fixed value on top
            // each frame - e.g. an on-court player who's momentarily still - so that patch alone contributes no
            // frame-to-frame energy. Its row/column still get energy from the rest of the region flickering
            // through them, so the projection-based bounds shouldn't shrink or fracture around it.
            var frame = CreateFrame(
                backgroundValue: 20,
                (100, 50, 100, 60, value),
                (140, 70, 20, 20, 128));
            detector.Accumulate(frame, Width, Height, Stride);
        }

        Assert.True(detector.TryGetRegion(out var region));
        Assert.Equal(100.0 / Width, region.X, precision: 3);
        Assert.Equal(50.0 / Height, region.Y, precision: 3);
        Assert.Equal(100.0 / Width, region.Width, precision: 3);
        Assert.Equal(60.0 / Height, region.Height, precision: 3);
    }

    [Fact]
    public void TryGetRegion_ShorterSeparateActiveRun_LosesToLongerMainRun()
    {
        var detector = new PlaybackRegionDetector(gridWidth: 32, gridHeight: 18, minimumFrames: 10);

        // Main region: 10x6 cells. A separate flickering block (3x3 cells) sits well apart from it - large
        // enough to individually clear the activity threshold on its own, but its run is shorter than the main
        // region's on both axes, so the longest-run selection keeps only the main region's bounds.
        AccumulateFlickeringRegion(detector, frameCount: 20, (100, 50, 100, 60), (250, 120, 30, 30));

        Assert.True(detector.TryGetRegion(out var region));
        Assert.Equal(100.0 / Width, region.X, precision: 3);
        Assert.Equal(50.0 / Height, region.Y, precision: 3);
        Assert.Equal(100.0 / Width, region.Width, precision: 3);
        Assert.Equal(60.0 / Height, region.Height, precision: 3);
    }

    [Fact]
    public void TryGetRegion_WithNoMotionAtAll_ReturnsFalse()
    {
        var detector = new PlaybackRegionDetector(gridWidth: 32, gridHeight: 18, minimumFrames: 5);

        for (var i = 0; i < 10; i++)
        {
            detector.Accumulate(CreateFrame(backgroundValue: 20), Width, Height, Stride);
        }

        Assert.False(detector.TryGetRegion(out _));
    }

    [Fact]
    public void Reset_ClearsAccumulatedFrames()
    {
        var detector = new PlaybackRegionDetector(gridWidth: 32, gridHeight: 18, minimumFrames: 5);

        AccumulateFlickeringRegion(detector, frameCount: 10, (100, 50, 100, 60));

        detector.Reset();

        Assert.Equal(0, detector.FramesAccumulated);
        Assert.False(detector.TryGetRegion(out _));
    }
}
