namespace NBA.OCR;

/// <summary>
/// A cropped BGRA8 buffer, tightly packed (no row padding) regardless of the source frame's stride.
/// <see cref="Left"/>/<see cref="Top"/> are the pixel offset of this crop within the source frame it came from -
/// callers that run detection against the crop and need to report positions back in the source frame's own
/// coordinate space (rather than the crop's) add these back onto the detector's output.
/// </summary>
public readonly record struct CroppedFrame(byte[] Pixels, int Width, int Height, int Stride, int Left, int Top);

/// <summary>
/// Crops a BGRA8 frame buffer to a normalized (0..1, frame-relative) sub-rectangle - used to isolate the
/// scoreboard region before OCR so recognition doesn't have to search the whole frame. No project in this
/// solution had a crop utility before this (see <c>NBA.Inference/ImagePreprocessing.cs</c>, which only resizes
/// full frames), so this is new rather than reused.
/// </summary>
public static class FrameCropper
{
    public static CroppedFrame Crop(
        ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride,
        double normalizedX, double normalizedY, double normalizedWidth, double normalizedHeight)
    {
        var left = Math.Clamp((int)(normalizedX * width), 0, width);
        var top = Math.Clamp((int)(normalizedY * height), 0, height);
        var right = Math.Clamp((int)((normalizedX + normalizedWidth) * width), left, width);
        var bottom = Math.Clamp((int)((normalizedY + normalizedHeight) * height), top, height);

        var cropWidth = right - left;
        var cropHeight = bottom - top;
        var cropStride = cropWidth * 4;
        var pixels = new byte[Math.Max(0, cropStride * cropHeight)];

        for (var row = 0; row < cropHeight; row++)
        {
            var sourceRow = bgra8Pixels.Slice((top + row) * stride + (left * 4), cropStride);
            sourceRow.CopyTo(pixels.AsSpan(row * cropStride, cropStride));
        }

        return new CroppedFrame(pixels, cropWidth, cropHeight, cropStride, left, top);
    }
}
