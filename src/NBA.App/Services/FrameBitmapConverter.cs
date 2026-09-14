using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NBA.Capture;

namespace NBA.App.Services;

/// <summary>Converts a captured BGRA8 frame into an Avalonia-renderable bitmap for the raw overlay view.</summary>
public static class FrameBitmapConverter
{
    public static WriteableBitmap ToWriteableBitmap(CapturedFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var lockedBuffer = bitmap.Lock();
        var span = frame.Pixels.Span;

        unsafe
        {
            var destination = (byte*)lockedBuffer.Address;
            for (var row = 0; row < frame.Height; row++)
            {
                var sourceRow = span.Slice(row * frame.Stride, frame.Width * 4);
                var destinationRow = new Span<byte>(destination + (row * lockedBuffer.RowBytes), frame.Width * 4);
                sourceRow.CopyTo(destinationRow);
            }
        }

        return bitmap;
    }
}
