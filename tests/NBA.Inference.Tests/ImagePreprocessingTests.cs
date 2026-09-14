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

        var tensor = ImagePreprocessing.ToNchwTensor(pixels, sourceWidth, sourceHeight, sourceWidth * 4, targetWidth: 224, targetHeight: 224);

        Assert.Equal([1, 3, 224, 224], tensor.Dimensions.ToArray());
    }

    [Fact]
    public void ToNchwTensor_ConvertsBgraToNormalizedRgbChannelOrder()
    {
        var pixels = MakeSolidBgra8(8, 8, b: 0, g: 128, r: 255);

        var tensor = ImagePreprocessing.ToNchwTensor(pixels, 8, 8, 8 * 4, targetWidth: 4, targetHeight: 4);

        // Channel 0 = R, channel 1 = G, channel 2 = B, each normalized to [0, 1].
        Assert.Equal(1f, tensor[0, 0, 0, 0], precision: 3);
        Assert.Equal(128f / 255f, tensor[0, 1, 0, 0], precision: 3);
        Assert.Equal(0f, tensor[0, 2, 0, 0], precision: 3);
    }
}
