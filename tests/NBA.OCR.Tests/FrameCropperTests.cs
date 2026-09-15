namespace NBA.OCR.Tests;

public class FrameCropperTests
{
    /// <summary>Builds a 4x4 BGRA8 frame where each pixel's blue channel encodes "row*4+col" so cropped output can be checked by exact value, not just size.</summary>
    private static byte[] MakeMarkedFrame(int width, int height, int stride)
    {
        var pixels = new byte[stride * height];
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var offset = (row * stride) + (col * 4);
                pixels[offset] = (byte)((row * width) + col); // B channel = marker
                pixels[offset + 3] = 255; // A
            }
        }

        return pixels;
    }

    [Fact]
    public void Crop_ReturnsExpectedSubRegion_TightlyPacked()
    {
        var frame = MakeMarkedFrame(width: 4, height: 4, stride: 16);

        // Bottom-right 2x2 quadrant: normalized (0.5, 0.5, 0.5, 0.5).
        var cropped = FrameCropper.Crop(frame, width: 4, height: 4, stride: 16, normalizedX: 0.5, normalizedY: 0.5, normalizedWidth: 0.5, normalizedHeight: 0.5);

        Assert.Equal(2, cropped.Width);
        Assert.Equal(2, cropped.Height);
        Assert.Equal(8, cropped.Stride); // 2 * 4, tightly packed - no source row padding carried over

        // Original markers at (row 2, col 2)=10, (row 2, col 3)=11, (row 3, col 2)=14, (row 3, col 3)=15.
        Assert.Equal(10, cropped.Pixels[0]);
        Assert.Equal(11, cropped.Pixels[4]);
        Assert.Equal(14, cropped.Pixels[8]);
        Assert.Equal(15, cropped.Pixels[12]);
    }

    [Fact]
    public void Crop_DefaultScoreboardRegion_IsBottomBandSpanningFullWidth()
    {
        var frame = MakeMarkedFrame(width: 10, height: 20, stride: 40);

        // NormalizedRect.DefaultScoreboardRegion lives in NBA.Vision, which NBA.OCR deliberately doesn't
        // reference (see FrameCropper's doc comment) - this test exercises the same shape (bottom 15% band,
        // full width) directly against FrameCropper to prove the crop math handles it correctly.
        var cropped = FrameCropper.Crop(frame, width: 10, height: 20, stride: 40, normalizedX: 0, normalizedY: 0.85, normalizedWidth: 1.0, normalizedHeight: 0.15);

        Assert.Equal(10, cropped.Width);
        Assert.Equal(3, cropped.Height); // 20 * 0.15 = 3
    }

    [Fact]
    public void Crop_ClampsRegionToFrameBounds()
    {
        var frame = MakeMarkedFrame(width: 4, height: 4, stride: 16);

        // Region starts at 90% and extends another 50% - past the frame edge, so it must clamp to what's left
        // (pixel column/row 3, the last one) rather than throwing or reading out of bounds.
        var cropped = FrameCropper.Crop(frame, width: 4, height: 4, stride: 16, normalizedX: 0.9, normalizedY: 0.9, normalizedWidth: 0.5, normalizedHeight: 0.5);

        Assert.Equal(1, cropped.Width);
        Assert.Equal(1, cropped.Height);
        Assert.Equal(15, cropped.Pixels[0]); // the single remaining pixel is (row 3, col 3) = marker 15
    }
}
