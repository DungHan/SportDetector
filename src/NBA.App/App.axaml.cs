using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NBA.App.Services;
using NBA.App.ViewModels;
using NBA.App.Views;
using NBA.JerseyOcr;
using NBA.Tracking;
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

            // See models/README.md's "Naming convention" - values here come from models/models.json when
            // present, falling back to today's defaults (see ModelsConfig) when the file, a section, or an
            // individual field is absent, so swapping a retrained model only means editing that JSON file.
            var modelsConfig = ModelsConfig.LoadFromJsonOrDefault(Path.Combine(GetModelsDirectory(), "models.json"));

            var sportClassifierModelPath = Path.Combine(GetModelsDirectory(), modelsConfig.SportClassifier.ModelPath);
            var sportPromptsPath = Path.Combine(GetModelsDirectory(), modelsConfig.SportClassifier.PromptsPath);
            var keypointModelPath = Path.Combine(GetModelsDirectory(), modelsConfig.CourtKeypoints.ModelPath);
            var playerDetectionModelPath = Path.Combine(GetModelsDirectory(), modelsConfig.PlayerDetection.ModelPath);
            var ballDetectionModelPath = Path.Combine(GetModelsDirectory(), modelsConfig.BallDetection.ModelPath);
            var jerseyNumberModelPath = Path.Combine(GetModelsDirectory(), modelsConfig.JerseyNumber.ModelPath);

            // No trained keypoint model is shipped in this change yet (see design.md's risk entries) - fall
            // back to the degraded/manual-only path rather than failing to start. The sport classifier now has
            // both a real CLIP vision encoder and real prompt embeddings (design.md's "Sport classification:
            // CLIP/SigLIP zero-shot" decision, which superseded OnnxSportClassifier's fine-tuned approach) -
            // still falls back to NullSportClassifier if either file is missing.
            ISportClassifier sportClassifier = File.Exists(sportClassifierModelPath) && File.Exists(sportPromptsPath)
                ? new ClipZeroShotSportClassifier(
                    sportClassifierModelPath,
                    ClipPromptEmbeddings.LoadFromJson(sportPromptsPath),
                    inputSize: modelsConfig.SportClassifier.InputSize,
                    // Temporary calibration aid (see design.md's "must recalibrate the confidence threshold"
                    // note) - logs every prompt's score so we can see *why* a frame landed on "unknown" instead
                    // of just that it did. Remove once the threshold/prompt list are actually calibrated.
                    onScored: ranked => Console.WriteLine("[sport-classify] " + string.Join(", ", ranked.Select(r => $"{r.Sport}={r.Probability:P1}"))))
                : new NullSportClassifier();

            if (sportClassifier is ClipZeroShotSportClassifier sportClassifierWithProvider)
            {
                Console.WriteLine($"[onnx] sport-classifier provider={sportClassifierWithProvider.Provider}");
            }

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
                ? new OnnxCourtKeypointDetector(
                    keypointModelPath,
                    BasketballGeometryDefinition(),
                    // Values come from models/models.json (see ModelsConfig) - the active model
                    // (models/README.md's "basketball_nba_court-keypoints_960_yolov8m-pose.onnx" section) is a
                    // 960x960 export, so the library's 640 default would letterbox/resize to the wrong size
                    // against this file, scrambling every decoded keypoint's pixel position.
                    inputSize: modelsConfig.CourtKeypoints.InputSize,
                    keypointConfidenceThreshold: modelsConfig.CourtKeypoints.KeypointConfidenceThreshold,
                    detectionConfidenceThreshold: modelsConfig.CourtKeypoints.DetectionConfidenceThreshold)
                : new NullCourtKeypointDetector(SportType.Basketball);

            if (keypointDetector is OnnxCourtKeypointDetector keypointDetectorWithProvider)
            {
                Console.WriteLine($"[onnx] court-keypoints provider={keypointDetectorWithProvider.Provider}");
            }

            // No trained player-detection model is shipped in this change yet (see design.md's risk entries
            // in openspec/changes/add-player-detection/) - falls back to the degraded zero-detections path.
            IMultiClassObjectDetector multiClassObjectDetector = File.Exists(playerDetectionModelPath)
                ? new OnnxMultiClassObjectDetector(
                    playerDetectionModelPath,
                    // Values come from models/models.json (see ModelsConfig), defaulting to this model's own
                    // embedded class-name metadata (models/README.md's
                    // "basketball_nba_player-detection_640_yolov8m-fp16.onnx.onnx" section, verified via the
                    // file's own onnx.metadata_props `names` key) - the order here must match that metadata's
                    // index order exactly, since classIndex 4+i's channel is decoded positionally, not by name.
                    classNames: modelsConfig.PlayerDetection.ClassNames,
                    // Defaults to this specific model's export size (models/README.md's
                    // "basketball_nba_player-detection_640_yolov8m-fp16.onnx.onnx" section: verified
                    // imgsz=[640,640] via InferenceSession.InputMetadata against the actual file, after an
                    // earlier re-export under this same file name had turned out to still be fixed at 960x960
                    // despite its name/config) - passing the wrong size here would letterbox/resize to the
                    // wrong size against this model, scrambling every box's decoded coordinates.
                    inputSize: modelsConfig.PlayerDetection.InputSize,
                    // Defaults lowered from the library defaults (confidenceThreshold: 0.5, iouThreshold: 0.45):
                    // real gameplay footage was visibly under-detecting crowded/distant players. A lower
                    // confidence floor keeps more real (if less certain) boxes - ByteTrackPlayerTracker's own
                    // two-stage matching (see its type-level doc comment) already treats sub-highConfidenceThreshold
                    // detections as "low confidence" and only lets them continue existing tracks, so this
                    // doesn't let stray noise spawn new tracks. A higher IoU threshold makes NMS less eager to
                    // treat two adjacent players (e.g. in a crowded paint) as duplicate boxes for the same one.
                    confidenceThreshold: modelsConfig.PlayerDetection.ConfidenceThreshold,
                    iouThreshold: modelsConfig.PlayerDetection.IouThreshold,
                    // Defaults to this model's own embedded class-name metadata (models/README.md) confirming
                    // `Player` is class index 0, distinct from `Ref` (1) - this export has no other on-court
                    // object classes (Ball/Hoop/scoreboard elements are no longer part of this model at all,
                    // see ballDetectionModelPath below and BallDetectionConfig).
                    playerClassId: modelsConfig.PlayerDetection.PlayerClassId,
                    // TEMPORARY diagnostic (remove once real-world confidence is calibrated): logs the max
                    // raw player-confidence seen across all candidates each frame, and how many passed the
                    // threshold, so a "nothing renders" report can be told apart from "genuinely below
                    // threshold" vs "always ~0, likely a wiring bug" without guessing.
                    onDiagnostics: (maxConfidence, aboveThresholdCount) =>
                        Console.WriteLine($"[player-detect] maxConfidence={maxConfidence:P1} aboveThreshold={aboveThresholdCount}"))
                : new NullMultiClassObjectDetector();

            if (multiClassObjectDetector is OnnxMultiClassObjectDetector playerDetectorWithProvider)
            {
                Console.WriteLine($"[onnx] player-detection provider={playerDetectorWithProvider.Provider}");
            }

            // A separate trained model and a separate inference pass from multiClassObjectDetector above -
            // the player-detection export above no longer includes a "Ball" class (it only ever has
            // Player/Ref), so ball detection needs its own model and its own Detect(...) call.
            // MainWindowViewModel merges this instance's Others list with multiClassObjectDetector's Others
            // list every detection frame rather than picking one or the other.
            IMultiClassObjectDetector ballDetector = File.Exists(ballDetectionModelPath)
                ? new OnnxMultiClassObjectDetector(
                    ballDetectionModelPath,
                    // This export's only class (models/README.md's
                    // "basketball_nba_ball-detection_960_yolov8m.onnx" section) - named "Ball" here (rather
                    // than the model's own embedded "item" label) so it round-trips through
                    // RawOverlayView.ClassBoxBrushes' existing "Ball" color entry unchanged.
                    classNames: modelsConfig.BallDetection.ClassNames,
                    inputSize: modelsConfig.BallDetection.InputSize,
                    confidenceThreshold: modelsConfig.BallDetection.ConfidenceThreshold,
                    iouThreshold: modelsConfig.BallDetection.IouThreshold,
                    // This model has no Player channel at all - every detection it produces belongs on the
                    // Others list, never fed into PlayerDetection/ByteTrackPlayerTracker.
                    playerClassId: null)
                : new NullMultiClassObjectDetector();

            if (ballDetector is OnnxMultiClassObjectDetector ballDetectorWithProvider)
            {
                Console.WriteLine($"[onnx] ball-detection provider={ballDetectorWithProvider.Provider}");
            }

            // No missing-model degraded path needed here (unlike the detectors above) - ByteTrackPlayerTracker
            // is a pure algorithm over already-in-memory boxes, not backed by an external model file (see
            // design.md in openspec/changes/add-bytetrack-tracking/), so it's always wired.
            IPlayerTracker playerTracker = new ByteTrackPlayerTracker(
                // Raised above the round-1 (highConfidenceIouThreshold) default: lowering the detector's
                // confidenceThreshold above let a lot more low-confidence noise (crowd/bench/referee boxes
                // misclassified at 0.35-0.6) into round 2's matching pool. At the same 0.3 IoU as round 1,
                // that noise was loose enough to hijack a track away from its predicted position - and unlike
                // a track that's genuinely unmatched, a hijacked track's LostFrames resets to 0, so it isn't
                // caught by ToVisiblePlayers' coasting cutoff. Round 2 exists to recover through real
                // occlusion/blur, not to accept any loose overlap, so it should require tighter spatial
                // agreement than round 1's cleaner high-confidence detections do.
                lowConfidenceIouThreshold: 0.5,
                // Tuned alongside detectionIntervalFrames below: the default of 5 was sized for a detection
                // cadence of every 3rd frame, where 5 missed Update() calls already span ~15 raw frames. At
                // the faster cadence below, that default would let a stale track coast on pure motion
                // prediction for several consecutive real frames before being hidden - long enough for a bad
                // velocity estimate to visibly drift. Tightened to bail out after 1 miss.
                maxVisibleLostFrames: 1);

            // No trained jersey-number recognition model is shipped in this change yet (see design.md's risk
            // entries) - falls back to the degraded always-unrecognized path. PluralityJerseyNumberVoteAggregator
            // is a pure algorithm over already-in-memory results, not backed by an external model file, so it's
            // always wired unconditionally (same posture as ByteTrackPlayerTracker above).
            IJerseyNumberRecognizer jerseyNumberRecognizer = File.Exists(jerseyNumberModelPath)
                ? new OnnxJerseyNumberRecognizer(
                    jerseyNumberModelPath,
                    inputSize: modelsConfig.JerseyNumber.InputSize,
                    confidenceThreshold: modelsConfig.JerseyNumber.ConfidenceThreshold)
                : new NullJerseyNumberRecognizer();
            IJerseyNumberVoteAggregator jerseyNumberVoteAggregator = new PluralityJerseyNumberVoteAggregator();

            if (jerseyNumberRecognizer is OnnxJerseyNumberRecognizer jerseyNumberRecognizerWithProvider)
            {
                Console.WriteLine($"[onnx] jersey-number provider={jerseyNumberRecognizerWithProvider.Provider}");
            }

            var mainViewModel = new MainWindowViewModel(
                CapturePlatform.CreateFrameSource(),
                CapturePlatform.CreateSourceEnumerator(),
                new SportClassificationCoordinator(sportClassifier, profileStore),
                new CourtCalibrationCoordinator(profileStore),
                keypointDetector,
                multiClassObjectDetector,
                playerTracker,
                ScoreboardOcrPlatform.CreateEngine(),
                profileStore,
                jerseyNumberRecognizer,
                jerseyNumberVoteAggregator,
                new PlaybackRegionCoordinator(profileStore),
                ballDetector: ballDetector,
                // Confirmed (by testing detectionIntervalFrames: 1) that PredictOnly()'s pure motion
                // extrapolation between detections was the main source of "flying" boxes - every-frame
                // detection fixed it but was too expensive (visible lag). Settling on every-2nd-frame as a
                // middle ground: half the extrapolation-only frames of the old default of 3, at less added
                // detector cost than running every frame.
                detectionIntervalFrames: 2);

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
