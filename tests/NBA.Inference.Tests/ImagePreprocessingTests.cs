using NBA.Inference;

namespace NBA.Inference.Tests;

public class ImagePreprocessingTests
{
    private static byte[] MakeSolidBgra8(int width, int height, byte b, byte g, byte r)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    [Theory]
    [InlineData(64, 48)]
    [InlineData(1920, 1080)]
    public void ToNchwTensor_DifferentSourceResolutions_ProduceTargetSizedTensorWithoutError(int sourceWidth, int sourceHeight)
    {
        var pixels = MakeSolidBgra8(sourceWidth, sourceHeight, b: 10, g: 20, r: 30);

        var tensor = ImagePreprocessing.ToNchwTensor(pixels, sourceWidth, sourceHeight, sourceWidth * 4, targetWidth: 224, targetHeight: 224, out _);

        Assert.Equal([1, 3, 224, 224], tensor.Dimensions.ToArray());
    }

    [Fact]
    public void ToNchwTensor_ConvertsBgraToNormalizedRgbChannelOrder()
    {
        var pixels = MakeSolidBgra8(8, 8, b: 0, g: 128, r: 255);

        var tensor = ImagePreprocessing.ToNchwTensor(pixels, 8, 8, 8 * 4, targetWidth: 4, targetHeight: 4, out _);

        // Channel 0 = R, channel 1 = G, channel 2 = B, each normalized to [0, 1].
        Assert.Equal(1f, tensor[0, 0, 0, 0], precision: 3);
        Assert.Equal(128f / 255f, tensor[0, 1, 0, 0], precision: 3);
        Assert.Equal(0f, tensor[0, 2, 0, 0], precision: 3);
    }

    [Fact]
    public void ToNchwTensor_NonSquareSource_LetterboxesInsteadOfStretching()
    {
        // 8-wide x 4-tall solid blue source into a 4x4 square target: scale = min(4/8, 4/4) = 0.5, so the
        // content only occupies the middle 2 rows (y in [1,3)) of the 4x4 target - the remaining rows must be
        // filled with the pad color, not a squashed sample of the source, and the transform must report that
        // padding so callers can invert it correctly.
        var pixels = MakeSolidBgra8(8, 4, b: 255, g: 0, r: 0);

        var tensor = ImagePreprocessing.ToNchwTensor(
            pixels, sourceWidth: 8, sourceHeight: 4, sourceStride: 8 * 4, targetWidth: 4, targetHeight: 4, out var transform);

        Assert.Equal(0.5f, transform.Scale, precision: 3);
        Assert.Equal(0, transform.PadX);
        Assert.Equal(1, transform.PadY);

        // Padding row (y=0): the Ultralytics gray pad value (114/255), not sampled source content.
        Assert.Equal(114f / 255f, tensor[0, 0, 0, 0], precision: 3);
        Assert.Equal(114f / 255f, tensor[0, 1, 0, 0], precision: 3);
        Assert.Equal(114f / 255f, tensor[0, 2, 0, 0], precision: 3);

        // Content row (y=1): sampled from the solid blue source.
        Assert.Equal(0f, tensor[0, 0, 1, 0], precision: 3); // R
        Assert.Equal(0f, tensor[0, 1, 1, 0], precision: 3); // G
        Assert.Equal(1f, tensor[0, 2, 1, 0], precision: 3); // B
    }

    [Fact]
    public void ToNchwTensor_WithCropRectangle_SamplesOnlyFromWithinTheCrop()
    {
        // A 4x4 red frame with a 2x2 green block only inside the crop rectangle (x:[2,4), y:[2,4)) - if the
        // cropped overload mapped from the full frame instead of the sub-rectangle, some sampled pixels would
        // come back red instead of a uniformly green tensor.
        var stride = 4 * 4;
        var pixels = MakeSolidBgra8(4, 4, b: 0, g: 0, r: 255);
        for (var y = 2; y < 4; y++)
        {
            for (var x = 2; x < 4; x++)
            {
                var offset = (y * stride) + (x * 4);
                pixels[offset] = 0; // B
                pixels[offset + 1] = 255; // G
                pixels[offset + 2] = 0; // R
            }
        }

        var tensor = ImagePreprocessing.ToNchwTensor(
            pixels, sourceWidth: 4, sourceHeight: 4, sourceStride: stride,
            cropX: 2, cropY: 2, cropWidth: 2, cropHeight: 2,
            targetWidth: 6, targetHeight: 6);

        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                Assert.Equal(0f, tensor[0, 0, y, x], precision: 3); // R
                Assert.Equal(1f, tensor[0, 1, y, x], precision: 3); // G
                Assert.Equal(0f, tensor[0, 2, y, x], precision: 3); // B
            }
        }
    }
}
