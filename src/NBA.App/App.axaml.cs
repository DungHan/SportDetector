using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NBA.App.Services;
using NBA.App.ViewModels;
using NBA.App.Views;
using NBA.Vision;

namespace NBA.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var profileStore = new FileSourceProfileStore(GetProfileDirectory());

            var sportClassifierModelPath = Path.Combine(GetModelsDirectory(), "sport-classifier.onnx");
            var sportPromptsPath = Path.Combine(GetModelsDirectory(), "sport-classifier-prompts.clip.json");
            var keypointModelPath = Path.Combine(GetModelsDirectory(), "court-keypoints.basketball.onnx");
            var playerDetectionModelPath = Path.Combine(GetModelsDirectory(), "player-detection.onnx");

            // No trained keypoint model is shipped in this change yet (see design.md's risk entries) - fall
            // back to the degraded/manual-only path rather than failing to start. The sport classifier now has
            // both a real CLIP vision encoder and real prompt embeddings (design.md's "Sport classification:
            // CLIP/SigLIP zero-shot" decision, which superseded OnnxSportClassifier's fine-tuned approach) -
            // still falls back to NullSportClassifier if either file is missing.
            ISportClassifier sportClassifier = File.Exists(sportClassifierModelPath) && File.Exists(sportPromptsPath)
                ? new ClipZeroShotSportClassifier(
                    sportClassifierModelPath,
                    ClipPromptEmbeddings.LoadFromJson(sportPromptsPath),
                    // Temporary calibration aid (see design.md's "must recalibrate the confidence threshold"
                    // note) - logs every prompt's score so we can see *why* a frame landed on "unknown" instead
                    // of just that it did. Remove once the threshold/prompt list are actually calibrated.
                    onScored: ranked => Console.WriteLine("[sport-classify] " + string.Join(", ", ranked.Select(r => $"{r.Sport}={r.Probability:P1}"))))
                : new NullSportClassifier();

            // TEMPORARY diagnostic (remove after calibration): dumps the exact raw BGRA8 frame handed to the
            // classifier on the first real classification, so we can independently re-run preprocessing in
            // Python and check whether a flat/uncertain score distribution is a real CLIP-zero-shot limitation
            // or a bug in ImagePreprocessing/capture stride handling.
            var dumpPath = Environment.GetEnvironmentVariable("NBA_CLIP_DEBUG_DUMP_PATH");
            if (dumpPath is not null)
            {
                sportClassifier = new DebugPixelDumpClassifier(sportClassifier, dumpPath);
            }

            ICourtKeypointDetector keypointDetector = File.Exists(keypointModelPath)
                ? new OnnxCourtKeypointDetector(keypointModelPath, BasketballGeometryDefinition())
                : new NullCourtKeypointDetector(SportType.Basketball);

            // No trained player-detection model is shipped in this change yet (see design.md's risk entries
            // in openspec/changes/add-player-detection/) - falls back to the degraded zero-detections path.
            IPlayerDetector playerDetector = File.Exists(playerDetectionModelPath)
                ? new OnnxPlayerDetector(
                    playerDetectionModelPath,
                    // TEMPORARY diagnostic (remove once real-world confidence is calibrated): logs the max
                    // raw person-confidence seen across all 8400 candidates each frame, and how many passed
                    // the threshold, so a "nothing renders" report can be told apart from "genuinely below
                    // threshold" vs "always ~0, likely a wiring bug" without guessing.
                    onDiagnostics: (maxConfidence, aboveThresholdCount) =>
                        Console.WriteLine($"[player-detect] maxConfidence={maxConfidence:P1} aboveThreshold={aboveThresholdCount}"))
                : new NullPlayerDetector();

            var mainViewModel = new MainWindowViewModel(
                CapturePlatform.CreateFrameSource(),
                CapturePlatform.CreateSourceEnumerator(),
                new SportClassificationCoordinator(sportClassifier, profileStore),
                new CourtCalibrationCoordinator(profileStore),
                keypointDetector,
                playerDetector,
                ScoreboardOcrPlatform.CreateEngine(),
                profileStore);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static CourtGeometryDefinition BasketballGeometryDefinition() =>
        CourtGeometryRegistry.TryGet(SportType.Basketball, out var geometry)
            ? geometry
            : throw new InvalidOperationException("Basketball geometry is expected to always be registered.");

    private static string GetModelsDirectory() => Path.Combine(AppContext.BaseDirectory, "models");

    private static string GetProfileDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NBA", "source-profiles");
}

/// <summary>TEMPORARY (calibration-diagnosis only, see App.axaml.cs) - dumps the first real frame's raw BGRA8 bytes + dimensions to a file, then delegates unchanged.</summary>
file sealed class DebugPixelDumpClassifier(ISportClassifier inner, string dumpPath) : ISportClassifier
{
    private bool _dumped;

    public SportClassifierOutput Classify(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        if (!_dumped)
        {
            _dumped = true;
            using var stream = File.Create(dumpPath);
            using var writer = new BinaryWriter(stream);
            writer.Write(width);
            writer.Write(height);
            writer.Write(stride);
            writer.Write(bgra8Pixels);
            Console.WriteLine($"[sport-classify] dumped raw frame ({width}x{height}, stride {stride}) to {dumpPath}");
        }

        return inner.Classify(bgra8Pixels, width, height, stride);
    }
}
