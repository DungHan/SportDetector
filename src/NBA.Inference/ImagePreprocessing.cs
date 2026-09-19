using Microsoft.ML.OnnxRuntime.Tensors;

namespace NBA.Inference;

/// <summary>
/// Describes how <see cref="ImagePreprocessing.ToNchwTensor(ReadOnlySpan{byte}, int, int, int, int, int, out LetterboxTransform)"/>
/// mapped a source-resolution frame into a (typically square) model input, so callers that decode pixel
/// coordinates out of the model's output can map them back into source-frame pixel space. Ultralytics-style
/// YOLO models are trained against letterboxed inputs (uniformly scaled to fit, centered, padded to the target
/// shape) rather than a non-uniform stretch - feeding a stretched image at inference time distorts the aspect
/// ratio the model was trained to expect, which silently degrades every decoded coordinate for any
/// non-square source frame (e.g. a 16:9 capture into a square model input).
/// </summary>
public readonly record struct LetterboxTransform(float Scale, int PadX, int PadY, int ResizedWidth, int ResizedHeight)
{
    public static LetterboxTransform Compute(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var scale = Math.Min((float)targetWidth / sourceWidth, (float)targetHeight / sourceHeight);
        var resizedWidth = (int)Math.Round(sourceWidth * scale);
        var resizedHeight = (int)Math.Round(sourceHeight * scale);
        return new LetterboxTransform(scale, (targetWidth - resizedWidth) / 2, (targetHeight - resizedHeight) / 2, resizedWidth, resizedHeight);
    }

    /// <summary>Maps an X coordinate in the letterboxed model-input space back to source-frame pixel space.</summary>
    public double MapToSourceX(double targetX) => (targetX - PadX) / Scale;

    /// <summary>Maps a Y coordinate in the letterboxed model-input space back to source-frame pixel space.</summary>
    public double MapToSourceY(double targetY) => (targetY - PadY) / Scale;
}

/// <summary>
/// Resizes/normalizes a raw BGRA8 image into the NCHW float tensor layout most ONNX vision models expect,
/// regardless of the source frame's resolution. Deliberately independent of NBA.Capture's <c>CapturedFrame</c>
/// type (operates on primitive width/height/stride/bytes) so NBA.Inference stays reusable without depending
/// on the capture project - see the inference/onnx-runtime spec's "Frame does not match expected model input"
/// requirement.
/// </summary>
public static class ImagePreprocessing
{
    // Ultralytics' own letterbox pad color (mid-gray, RGB 114/114/114) - matched here so the padding a real
    // trained model sees at inference is the same padding it saw during training, not an arbitrary color.
    private const float LetterboxPadValue = 114f / 255f;

    /// <summary>
    /// Letterbox-resizes a BGRA8 image into a [1, 3, targetHeight, targetWidth] tensor (RGB channel order,
    /// each value scaled to [0, 1]): scaled uniformly (preserving aspect ratio) to fit within the target
    /// shape, centered, with any remaining border filled with <see cref="LetterboxPadValue"/> - matching
    /// Ultralytics YOLO's own preprocessing rather than a non-uniform stretch, which would distort the aspect
    /// ratio of every line/corner the model was trained to recognize. <paramref name="transform"/> lets the
    /// caller map any pixel coordinate decoded from the model's output back into source-frame pixel space.
    /// </summary>
    public static DenseTensor<float> ToNchwTensor(
        ReadOnlySpan<byte> bgra8Pixels,
        int sourceWidth,
        int sourceHeight,
        int sourceStride,
        int targetWidth,
        int targetHeight,
        out LetterboxTransform transform)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentException("Source width/height must be positive.");
        }

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException("Target width/height must be positive.");
        }

        transform = LetterboxTransform.Compute(sourceWidth, sourceHeight, targetWidth, targetHeight);
        var tensor = new DenseTensor<float>([1, 3, targetHeight, targetWidth]);

        for (var y = 0; y < targetHeight; y++)
        {
            var withinContentRowRange = y >= transform.PadY && y < transform.PadY + transform.ResizedHeight;
            var sourceY = withinContentRowRange ? Math.Min(sourceHeight - 1, (int)((y - transform.PadY) / transform.Scale)) : 0;

            for (var x = 0; x < targetWidth; x++)
            {
                float r, g, b;
                var withinContentColumnRange = x >= transform.PadX && x < transform.PadX + transform.ResizedWidth;
                if (withinContentRowRange && withinContentColumnRange)
                {
                    var sourceX = Math.Min(sourceWidth - 1, (int)((x - transform.PadX) / transform.Scale));
                    var pixelOffset = (sourceY * sourceStride) + (sourceX * 4);
                    b = bgra8Pixels[pixelOffset] / 255f;
                    g = bgra8Pixels[pixelOffset + 1] / 255f;
                    r = bgra8Pixels[pixelOffset + 2] / 255f;
                }
                else
                {
                    r = g = b = LetterboxPadValue;
                }

                tensor[0, 0, y, x] = r;
                tensor[0, 1, y, x] = g;
                tensor[0, 2, y, x] = b;
            }
        }

        return tensor;
    }

    /// <summary>
    /// Nearest-neighbor resizes (non-uniform stretch, unlike the letterboxing full-frame overload above) the
    /// source-space sub-rectangle <paramref name="cropX"/>/<paramref name="cropY"/>/<paramref name="cropWidth"/>/<paramref name="cropHeight"/>
    /// into the target shape - e.g. a tracked player's box, for per-track model inputs where the target is a
    /// fixed-size classifier input and no pixel coordinate ever needs to be decoded back out of model space.
    /// The crop rectangle is assumed already clamped to <c>[0, sourceWidth) x [0, sourceHeight)</c> by the
    /// caller (a track's motion-predicted box can extend outside the frame during brief occlusion).
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
