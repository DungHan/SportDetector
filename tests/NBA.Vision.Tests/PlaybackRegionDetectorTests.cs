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
    public void TryGetRegion_BridgesSmallQuietGapBetweenTwoMovingRegions_ReturnsUnionBounds()
    {
        var detector = new PlaybackRegionDetector(gridWidth: 32, gridHeight: 18, minimumFrames: 10);

        // Two flickering blocks separated by a 2-cell (20px) quiet gap, small enough to represent an on-court
        // subject that's briefly still rather than a genuinely separate motion source - see
        // PlaybackRegionDetector.ConnectivityBridgeRadius's doc comment.
        AccumulateFlickeringRegion(detector, frameCount: 20, (100, 50, 60, 60), (180, 50, 60, 60));

        Assert.True(detector.TryGetRegion(out var region));
        Assert.Equal(100.0 / Width, region.X, precision: 3);
        Assert.Equal(50.0 / Height, region.Y, precision: 3);
        Assert.Equal(140.0 / Width, region.Width, precision: 3);
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
