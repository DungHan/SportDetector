using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace NBA.OCR.Windows;

/// <summary>
/// Windows.Media.Ocr-backed <see cref="IScoreboardOcrEngine"/> - the OS-provided OCR engine, no extra package
/// or model file needed. NOT verified end-to-end on this machine (no Windows host available to run it) - same
/// caveat as this repo's other Windows-only code, see NBA.Capture's WindowsGraphicsCaptureFrameSource.
/// </summary>
public sealed class WindowsOcrEngine : IScoreboardOcrEngine
{
    private readonly OcrEngine? _engine =
        OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en"));

    public IReadOnlyList<ScoreboardOcrLine> Recognize(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        if (_engine is null || width <= 0 || height <= 0)
        {
            return [];
        }

        // SoftwareBitmap requires a tightly-packed buffer (no row padding) - repack when stride has padding.
        var tightBytes = stride == width * 4 ? bgra8Pixels.ToArray() : PackRows(bgra8Pixels, width, height, stride);

        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(tightBytes.AsBuffer());

        var result = _engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();

        // Windows.Media.Ocr doesn't expose a per-line confidence score, unlike Vision's topCandidates - treat
        // every recognized line as fully confident rather than inventing a number the API doesn't provide.
        return result.Lines.Select(line => new ScoreboardOcrLine(line.Text, Confidence: 1f)).ToArray();
    }

    private static byte[] PackRows(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        var rowBytes = width * 4;
        var packed = new byte[rowBytes * height];
        for (var row = 0; row < height; row++)
        {
            bgra8Pixels.Slice(row * stride, rowBytes).CopyTo(packed.AsSpan(row * rowBytes, rowBytes));
        }

        return packed;
    }
}
