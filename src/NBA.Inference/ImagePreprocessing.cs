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
        int targetHeight) =>
        ToNchwTensor(bgra8Pixels, sourceWidth, sourceHeight, sourceStride, 0, 0, sourceWidth, sourceHeight, targetWidth, targetHeight);

    /// <summary>
    /// Same nearest-neighbor mapping as the full-frame overload above, except target pixels are mapped into
    /// the source-space sub-rectangle <paramref name="cropX"/>/<paramref name="cropY"/>/<paramref name="cropWidth"/>/<paramref name="cropHeight"/>
    /// instead of the full frame - e.g. a tracked player's box, for per-track model inputs. The crop rectangle
    /// is assumed already clamped to <c>[0, sourceWidth) x [0, sourceHeight)</c> by the caller (a track's
    /// motion-predicted box can extend outside the frame during brief occlusion).
    /// </summary>
    public static DenseTensor<float> ToNchwTensor(
        ReadOnlySpan<byte> bgra8Pixels,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int cropX,
        int cropY,
        int cropWidth,
        int cropHeight,
        int targetWidth,
        int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentException("Source width/height must be positive.");
        }

        if (cropWidth <= 0 || cropHeight <= 0)
        {
            throw new ArgumentException("Crop width/height must be positive.");
        }

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException("Target width/height must be positive.");
        }

        var tensor = new DenseTensor<float>([1, 3, targetHeight, targetWidth]);

        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = cropY + Math.Min(cropHeight - 1, y * cropHeight / targetHeight);
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = cropX + Math.Min(cropWidth - 1, x * cropWidth / targetWidth);
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
