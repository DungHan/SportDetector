using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NBA.Capture;
using NBA.Vision;

namespace NBA.App.Services;

/// <summary>Converts BGRA8 pixel buffers (captured frames, rendered court diagrams) into Avalonia-renderable bitmaps.</summary>
public static class FrameBitmapConverter
{
    public static WriteableBitmap ToWriteableBitmap(CapturedFrame frame) =>
        ToWriteableBitmap(frame.Pixels.Span, frame.Width, frame.Height, frame.Stride);

    public static WriteableBitmap ToWriteableBitmap(CourtDiagramImage image) =>
        ToWriteableBitmap(image.BgraPixels, image.Width, image.Height, image.Stride);

    private static WriteableBitmap ToWriteableBitmap(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var lockedBuffer = bitmap.Lock();

        unsafe
        {
            var destination = (byte*)lockedBuffer.Address;
            for (var row = 0; row < height; row++)
            {
                var sourceRow = pixels.Slice(row * stride, width * 4);
                var destinationRow = new Span<byte>(destination + (row * lockedBuffer.RowBytes), width * 4);
                sourceRow.CopyTo(destinationRow);
            }
        }

        return bitmap;
    }
}
