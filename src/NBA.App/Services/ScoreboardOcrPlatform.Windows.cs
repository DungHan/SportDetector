using NBA.OCR;
using NBA.OCR.Windows;

namespace NBA.App.Services;

/// <summary>Windows build: uses the real Windows.Media.Ocr backend. See ScoreboardOcrPlatform.Fallback.cs for the non-Windows counterpart.</summary>
public static class ScoreboardOcrPlatform
{
    public static IScoreboardOcrEngine CreateEngine() => new WindowsOcrEngine();
}
