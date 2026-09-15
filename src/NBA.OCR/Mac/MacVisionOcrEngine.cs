using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenCvSharp;

namespace NBA.OCR.Mac;

/// <summary>
/// macOS Vision framework-backed <see cref="IScoreboardOcrEngine"/>. Vision (<c>VNRecognizeTextRequest</c>) has
/// no C API, so unlike NBA.Capture.Mac's plain-CoreGraphics P/Invoke, this shells out to a tiny standalone Swift
/// command-line tool (<c>Mac/SwiftTool/main.swift</c>) instead of bridging into the Objective-C runtime from C#.
/// The helper is compiled from source on first use and cached, rather than requiring a separate build step - if
/// <c>swiftc</c> isn't installed, or compiling/running the helper fails for any reason, this engine behaves
/// exactly like <see cref="NullScoreboardOcrEngine"/> instead of throwing.
/// </summary>
public sealed class MacVisionOcrEngine : IScoreboardOcrEngine
{
    private readonly string _swiftSourcePath;
    private readonly string _cacheDirectory;
    private readonly Lock _compileLock = new();

    private string? _compiledHelperPath;
    private bool _compilationAttempted;

    public MacVisionOcrEngine(string swiftSourcePath, string? cacheDirectory = null)
    {
        _swiftSourcePath = swiftSourcePath;
        _cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "nba-mac-ocr-helper");
    }

    public IReadOnlyList<ScoreboardOcrLine> Recognize(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        var helperPath = GetOrCompileHelper();
        if (helperPath is null)
        {
            return [];
        }

        var imagePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        try
        {
            EncodePng(bgra8Pixels, width, height, stride, imagePath);
            return RunHelper(helperPath, imagePath);
        }
        catch
        {
            return [];
        }
        finally
        {
            File.Delete(imagePath);
        }
    }

    private string? GetOrCompileHelper()
    {
        lock (_compileLock)
        {
            if (_compiledHelperPath is not null)
            {
                return _compiledHelperPath;
            }

            if (_compilationAttempted)
            {
                return null;
            }

            _compilationAttempted = true;

            if (!File.Exists(_swiftSourcePath))
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                var outputPath = Path.Combine(_cacheDirectory, "scoreboard-ocr");

                var needsCompile = !File.Exists(outputPath)
                    || File.GetLastWriteTimeUtc(_swiftSourcePath) > File.GetLastWriteTimeUtc(outputPath);

                if (needsCompile && !Compile(_swiftSourcePath, outputPath))
                {
                    return null;
                }

                _compiledHelperPath = outputPath;
                return _compiledHelperPath;
            }
            catch
            {
                return null;
            }
        }
    }

    private static bool Compile(string sourcePath, string outputPath)
    {
        var startInfo = new ProcessStartInfo("swiftc") { UseShellExecute = false, RedirectStandardError = true };
        startInfo.ArgumentList.Add("-O");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        process.WaitForExit();
        return process.ExitCode == 0 && File.Exists(outputPath);
    }

    private static IReadOnlyList<ScoreboardOcrLine> RunHelper(string helperPath, string imagePath)
    {
        var startInfo = new ProcessStartInfo(helperPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        startInfo.ArgumentList.Add(imagePath);

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return [];
        }

        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return [];
        }

        var parsed = JsonSerializer.Deserialize<HelperLine[]>(stdout);
        return parsed?.Select(l => new ScoreboardOcrLine(l.Text, l.Confidence)).ToArray() ?? [];
    }

    /// <summary>Encodes a BGRA8 buffer as a PNG file via OpenCvSharp (already a solution dependency - see NBA.Vision.csproj) rather than adding a new image-encoding dependency.</summary>
    private static void EncodePng(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride, string path)
    {
        using var mat = new Mat(height, width, MatType.CV_8UC4);
        var rowBytes = width * 4;
        var rowBuffer = new byte[rowBytes];
        for (var row = 0; row < height; row++)
        {
            bgra8Pixels.Slice(row * stride, rowBytes).CopyTo(rowBuffer);
            Marshal.Copy(rowBuffer, 0, mat.Data + (row * (int)mat.Step()), rowBytes);
        }

        Cv2.ImWrite(path, mat);
    }

    private sealed record HelperLine(string Text, float Confidence);
}
