using NBA.OCR;
using NBA.OCR.Mac;

namespace NBA.App.Services;

/// <summary>
/// Non-Windows build TFM (net10.0), mirroring <see cref="CapturePlatform"/>. On macOS this uses the real
/// Vision-framework-backed <see cref="MacVisionOcrEngine"/>; on any other OS this TFM runs on (e.g. Linux),
/// no native OCR backend exists, so it falls back to <see cref="NullScoreboardOcrEngine"/>. See
/// ScoreboardOcrPlatform.Windows.cs for the Windows build.
/// </summary>
public static class ScoreboardOcrPlatform
{
    public static IScoreboardOcrEngine CreateEngine() =>
        OperatingSystem.IsMacOS() ? new MacVisionOcrEngine(GetSwiftSourcePath()) : new NullScoreboardOcrEngine();

    private static string GetSwiftSourcePath() =>
        Path.Combine(AppContext.BaseDirectory, "native", "mac-ocr", "main.swift");
}
