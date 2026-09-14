using Microsoft.ML.OnnxRuntime.Tensors;

namespace NBA.Inference;

/// <summary>
/// Resizes/normalizes a raw BGRA8 image into the NCHW float tensor layout most ONNX vision models expect,
/// regardless of the source frame's resolution. Deliberately independent of NBA.Capture's <c>CapturedFrame</c>
/// type (operates on primitive width/height/stride/bytes) so NBA.Inference stays reusable without depending
/// on the capture project - see the inference/onnx-runtime spec's "Frame does not match expected model input"
/// requirement.
/// </summary>
public static class ImagePreprocessing
{
    /// <summary>
    /// Nearest-neighbor resizes a BGRA8 image to <paramref name="targetWidth"/> x <paramref name="targetHeight"/>
    /// and returns it as a [1, 3, targetHeight, targetWidth] tensor in RGB channel order, each value scaled to [0, 1].
    /// </summary>
    public static DenseTensor<float> ToNchwTensor(
        ReadOnlySpan<byte> bgra8Pixels,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int targetWidth,
        int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentException("Source width/height must be positive.");
        }

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException("Target width/height must be positive.");
        }

        var tensor = new DenseTensor<float>([1, 3, targetHeight, targetWidth]);

        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / targetHeight);
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / targetWidth);
                var pixelOffset = (sourceY * sourceStride) + (sourceX * 4);

                var b = bgra8Pixels[pixelOffset] / 255f;
                var g = bgra8Pixels[pixelOffset + 1] / 255f;
                var r = bgra8Pixels[pixelOffset + 2] / 255f;

                tensor[0, 0, y, x] = r;
                tensor[0, 1, y, x] = g;
                tensor[0, 2, y, x] = b;
            }
        }

        return tensor;
    }
}
