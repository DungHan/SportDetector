using Avalonia.Headless.XUnit;
using NBA.App.Models;
using NBA.App.Services;
using NBA.App.ViewModels;
using NBA.Capture;
using NBA.Capture.Testing;
using NBA.JerseyOcr;
using NBA.OCR;
using NBA.Tracking;
using NBA.Vision;
using Xunit;

namespace NBA.App.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"nba-profiles-{Guid.NewGuid():N}");

    private static readonly CaptureSourceDescriptor SourceA = new("a", "Source A", CaptureSourceKind.Window, 1, "processA");
    private static readonly CaptureSourceDescriptor SourceB = new("b", "Source B", CaptureSourceKind.Window, 2, "processB");

    private sealed class StubClassifier(SportClassifierOutput output) : ISportClassifier
    {
        public int CallCount { get; private set; }

        public SportClassifierOutput Classify(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
        {
            CallCount++;
            return output;
        }
    }

    private sealed class StubKeypointDetector(IReadOnlyList<DetectedKeypoint> keypoints) : ICourtKeypointDetector
    {
        public SportType Sport => SportType.Basketball;

        public float KeypointConfidenceThreshold { get; set; } = 0.5f;

        public float DetectionConfidenceThreshold { get; set; } = 0.5f;

        public IReadOnlyList<DetectedKeypoint> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) => keypoints;
    }

    private sealed class StubMultiClassObjectDetector : IMultiClassObjectDetector
    {
        public IReadOnlyList<PlayerDetection> Detections { get; set; } = [];

        public IReadOnlyList<OnCourtObjectDetection> OtherDetections { get; set; } = [];

        public int CallCount { get; private set; }

        public int LastWidth { get; private set; }

        public int LastHeight { get; private set; }

        public MultiClassDetectionResult Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
        {
            CallCount++;
            LastWidth = width;
            LastHeight = height;
            return new MultiClassDetectionResult(Detections, OtherDetections);
        }
    }

    private sealed class StubPlayerTracker : IPlayerTracker
    {
        public IReadOnlyList<TrackedPlayer> Tracks { get; set; } = [];

        public int UpdateCallCount { get; private set; }

        public int PredictOnlyCallCount { get; private set; }

        public int ResetCallCount { get; private set; }

        public IReadOnlyList<PlayerDetection> LastDetections { get; private set; } = [];

        public IReadOnlyList<TrackedPlayer> Update(IReadOnlyList<PlayerDetection> detections)
        {
            UpdateCallCount++;
            LastDetections = detections;
            return Tracks;
        }

        public IReadOnlyList<TrackedPlayer> PredictOnly()
        {
            PredictOnlyCallCount++;
            return Tracks;
        }

        public void Reset() => ResetCallCount++;
    }

    private sealed class StubJerseyNumberRecognizer : IJerseyNumberRecognizer
    {
        public JerseyNumberRecognitionResult Result { get; set; } = new(Number: null, Confidence: 0f);

        public int CallCount { get; private set; }

        public JerseyNumberRecognitionResult Recognize(
            ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride,
            double cropLeft, double cropTop, double cropRight, double cropBottom)
        {
            CallCount++;
            return Result;
        }
    }

    private sealed class StubJerseyNumberVoteAggregator : IJerseyNumberVoteAggregator
    {
        public IReadOnlyDictionary<int, int> Resolved { get; set; } = new Dictionary<int, int>();

        public int UpdateCallCount { get; private set; }

        public IReadOnlyDictionary<int, int> Update(IReadOnlyList<(int TrackId, JerseyNumberRecognitionResult Result)> frameResults)
        {
            UpdateCallCount++;
            return Resolved;
        }
    }

    private sealed class StubScoreboardOcrEngine : IScoreboardOcrEngine
    {
        public int LastWidth { get; private set; }

        public int LastHeight { get; private set; }

        public IReadOnlyList<ScoreboardOcrLine> Recognize(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
        {
            LastWidth = width;
            LastHeight = height;
            return [];
        }
    }

    private static CapturedFrame MakeFrame() => new()
    {
        Width = 4,
        Height = 4,
        Format = FramePixelFormat.Bgra8,
        Stride = 16,
        Pixels = new byte[16 * 4],
        Timestamp = DateTimeOffset.UtcNow,
    };

    private static ImagePoint ToImage(CourtPoint court) => new((court.X * 10) + 100, (court.Y * 10) + 50);

    private static IReadOnlyList<LandmarkCorrespondence> KnownCorrespondences() =>
    [
        new(ToImage(new CourtPoint(0, 0)), "BaselineCorner_Left_Near"),
        new(ToImage(new CourtPoint(28.6512, 0)), "BaselineCorner_Right_Near"),
        new(ToImage(new CourtPoint(0, 15.24)), "BaselineCorner_Left_Far"),
        new(ToImage(new CourtPoint(28.6512, 15.24)), "BaselineCorner_Right_Far"),
    ];

    [AvaloniaFact]
    public async Task Construction_StartsCaptureFromFirstEnumeratedSource()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA, SourceB]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new NullMultiClassObjectDetector(),
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));

        // Give the fire-and-forget SelectSourceAsync a chance to complete.
        await Task.Delay(50);

        Assert.Equal(SourceA, frameSource.ActiveSource);
    }

    [AvaloniaFact]
    public async Task FrameArrived_UpdatesRawOverlayBitmap()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new NullMultiClassObjectDetector(),
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(viewModel.RawOverlay.CurrentFrame);
        Assert.Equal(4, viewModel.RawOverlay.CurrentFrame!.PixelSize.Width);
    }

    [AvaloniaFact]
    public async Task FrameArrived_ClassifiesSportOnlyOnce_ForRepeatedFramesOfSameSource()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var classifier = new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.9f));
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(classifier, store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new NullMultiClassObjectDetector(),
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        frameSource.PublishFrame(MakeFrame());
        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, classifier.CallCount);
        Assert.Equal(SportType.Basketball, viewModel.SportIndicator.Current?.Sport);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithReusedCalibration_PopulatesBothRawOverlayAndMinimap_FromSameFrame()
    {
        // Pre-seed a saved calibration for this source+sport, as if the user calibrated it in a previous
        // session - proves the "offer reuse" path (spec's "Persist and reuse calibration per source") and
        // that both views update concurrently from one frame (spec's "Both views can run concurrently").
        var store = new FileSourceProfileStore(_directory);
        var sourceKey = SourceIdentity.DeriveKey(SourceA);
        var calibrationResult = new CourtCalibrationCoordinator(store).ManualCalibrate(sourceKey, SportType.Basketball, KnownCorrespondences());

        var frameSource = new FakeFrameSource();
        var keypoints = KnownCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        var playerDetector = new StubMultiClassObjectDetector { Detections = [new PlayerDetection(1, 2, 3, 4, 0.876f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new StubKeypointDetector(keypoints),
            playerDetector,
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.Minimap.HasValidCalibration);
        Assert.Equal(keypoints.Count + 1, viewModel.RawOverlay.Annotations.Count);
        Assert.Equal(keypoints.Count + 1, viewModel.Minimap.Markers.Count); // keypoint markers + one player marker

        var trackAnnotation = Assert.Single(viewModel.RawOverlay.Annotations, a => a.Label == "#1");
        Assert.Equal(OverlayShapeKind.Box, trackAnnotation.Shape);
        Assert.Equal([(1.0, 2.0), (3.0, 4.0)], trackAnnotation.Points); // the detected+tracked player's box

        // Foot point of the box above ((1,2)-(3,4)) is its bottom-center, (2, 4).
        var expectedFootPoint = PointProjector.Project(calibrationResult.Calibration!, new ImagePoint(2, 4));
        var playerMarker = Assert.Single(viewModel.Minimap.Markers, m => m.StyleKey == "player");
        Assert.Equal("#1", playerMarker.Label);
        Assert.Equal(expectedFootPoint.X, playerMarker.X, precision: 6);
        Assert.Equal(expectedFootPoint.Y, playerMarker.Y, precision: 6);
    }

    [AvaloniaFact]
    public async Task FrameArrived_NoResolvedJerseyNumber_MinimapMarkerStillShowsTrackIdLabel()
    {
        var store = new FileSourceProfileStore(_directory);
        var sourceKey = SourceIdentity.DeriveKey(SourceA);
        new CourtCalibrationCoordinator(store).ManualCalibrate(sourceKey, SportType.Basketball, KnownCorrespondences());

        var frameSource = new FakeFrameSource();
        var keypoints = KnownCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        var playerTracker = new StubPlayerTracker { Tracks = [new TrackedPlayer(1, 0, 0, 2, 2, 0.9f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new StubKeypointDetector(keypoints),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new StubJerseyNumberVoteAggregator(), // Resolved defaults to empty - no track has a resolved number
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var playerMarker = Assert.Single(viewModel.Minimap.Markers, m => m.StyleKey == "player");
        Assert.Equal("#1", playerMarker.Label);

        var trackAnnotation = Assert.Single(viewModel.RawOverlay.Annotations, a => a.Shape == OverlayShapeKind.Box);
        Assert.Equal("#1", trackAnnotation.Label);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithResolvedJerseyNumber_MinimapMarkerShowsResolvedNumber_RawOverlayLabelUnaffected()
    {
        var store = new FileSourceProfileStore(_directory);
        var sourceKey = SourceIdentity.DeriveKey(SourceA);
        new CourtCalibrationCoordinator(store).ManualCalibrate(sourceKey, SportType.Basketball, KnownCorrespondences());

        var frameSource = new FakeFrameSource();
        var keypoints = KnownCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        var playerTracker = new StubPlayerTracker { Tracks = [new TrackedPlayer(1, 0, 0, 2, 2, 0.9f)] };
        var jerseyNumberVoteAggregator = new StubJerseyNumberVoteAggregator { Resolved = new Dictionary<int, int> { [1] = 23 } };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new StubKeypointDetector(keypoints),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            jerseyNumberVoteAggregator,
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var playerMarker = Assert.Single(viewModel.Minimap.Markers, m => m.StyleKey == "player");
        Assert.Equal("#23", playerMarker.Label);

        // The raw-overlay box label stays the track ID regardless (design.md's Non-Goals).
        var trackAnnotation = Assert.Single(viewModel.RawOverlay.Annotations, a => a.Shape == OverlayShapeKind.Box);
        Assert.Equal("#1", trackAnnotation.Label);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WhenTrackDisappears_MinimapDropsBackToJustKeypointMarkers()
    {
        var store = new FileSourceProfileStore(_directory);
        var sourceKey = SourceIdentity.DeriveKey(SourceA);
        new CourtCalibrationCoordinator(store).ManualCalibrate(sourceKey, SportType.Basketball, KnownCorrespondences());

        var frameSource = new FakeFrameSource();
        var keypoints = KnownCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        var playerTracker = new StubPlayerTracker { Tracks = [new TrackedPlayer(1, 0, 0, 2, 2, 0.9f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new StubKeypointDetector(keypoints),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.Minimap.HasValidCalibration);
        Assert.Equal(keypoints.Count + 1, viewModel.Minimap.Markers.Count); // keypoint markers + the one live track

        playerTracker.Tracks = [];
        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(keypoints.Count, viewModel.Minimap.Markers.Count); // player marker gone, keypoint markers remain
        Assert.DoesNotContain(viewModel.Minimap.Markers, m => m.StyleKey == "player");
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithoutValidCalibration_AddsNoPlayerMarkersToMinimap()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerTracker = new StubPlayerTracker { Tracks = [new TrackedPlayer(1, 0, 0, 2, 2, 0.9f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store), // no calibration ever saved for this source
            new StubKeypointDetector([]),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(viewModel.Minimap.HasValidCalibration);
        Assert.Empty(viewModel.Minimap.Markers);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithCadenceOfOne_CallsPlayerDetectorOnEveryFrame()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerDetector = new StubMultiClassObjectDetector();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            playerDetector,
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store),
            detectionIntervalFrames: 1);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Assert.Equal(1, playerDetector.CallCount);

        frameSource.PublishFrame(MakeFrame());
        Assert.Equal(2, playerDetector.CallCount); // cadence of 1 means every frame, unlike sport classification's once-per-source caching
    }

    [AvaloniaFact]
    public async Task FrameArrived_OnDetectionFrame_CallsMultiClassDetectorExactlyOnce()
    {
        // Regression guard for this change's core "one inference pass" goal: players and every other on-court
        // class must come from a single Detect(...) call, never one call for players plus a separate call for
        // everything else.
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var detector = new StubMultiClassObjectDetector
        {
            Detections = [new PlayerDetection(1, 1, 2, 2, 0.9f)],
            OtherDetections = [new OnCourtObjectDetection(3, 3, 4, 4, 0.8f, "Ball")],
        };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            detector,
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store),
            detectionIntervalFrames: 1);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());

        Assert.Equal(1, detector.CallCount);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithCadenceOfOne_CallsPlayerTrackerUpdateOnEveryFrame()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerTracker = new StubPlayerTracker();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store),
            detectionIntervalFrames: 1);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Assert.Equal(1, playerTracker.UpdateCallCount);

        frameSource.PublishFrame(MakeFrame());
        Assert.Equal(2, playerTracker.UpdateCallCount);
        Assert.Equal(0, playerTracker.PredictOnlyCallCount);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithPersistedPlaybackRegion_CropsDetectorInputAndOffsetsDetectionsBackToFullFrame()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        store.Save(new SourceProfile
        {
            SourceKey = SourceIdentity.DeriveKey(SourceA),
            PlaybackRegion = new NormalizedRect(0.25, 0.25, 0.5, 0.5),
        });

        // Reported as if found within the crop's own pixel space - the assertions below check it comes back
        // offset into full-frame space, not left as-is (PlaybackRegionCoordinator/FrameCropper wiring in
        // MainWindowViewModel.OnFrameArrived).
        var playerDetector = new StubMultiClassObjectDetector { Detections = [new PlayerDetection(1, 1, 2, 2, 0.9f)] };
        var playerTracker = new StubPlayerTracker();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            playerDetector,
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store),
            detectionIntervalFrames: 1);
        await Task.Delay(50);

        // Region (0.25, 0.25, 0.5, 0.5) of an 8x8 frame is pixels [2, 6) x [2, 6): a 4x4 crop at offset (2, 2).
        frameSource.PublishFrame(new CapturedFrame
        {
            Width = 8,
            Height = 8,
            Format = FramePixelFormat.Bgra8,
            Stride = 32,
            Pixels = new byte[32 * 8],
            Timestamp = DateTimeOffset.UtcNow,
        });

        Assert.Equal(4, playerDetector.LastWidth);
        Assert.Equal(4, playerDetector.LastHeight);

        var detection = Assert.Single(playerTracker.LastDetections);
        Assert.Equal(new PlayerDetection(3, 3, 4, 4, 0.9f), detection);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithPersistedPlaybackRegion_PositionsScoreboardOcrRelativeToThatRegion()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        store.Save(new SourceProfile
        {
            SourceKey = SourceIdentity.DeriveKey(SourceA),
            PlaybackRegion = new NormalizedRect(0.25, 0.25, 0.5, 0.5),
        });

        var scoreboardOcr = new StubScoreboardOcrEngine();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new NullMultiClassObjectDetector(),
            new ByteTrackPlayerTracker(),
            scoreboardOcr,
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        // Playback region (0.25, 0.25, 0.5, 0.5) of an 8x8 frame is a 4x4 crop. DefaultScoreboardRegion (bottom
        // 15%, full width) of that 4x4 crop is a 4x1 slice - not the 8x2 slice it would be against the full
        // frame, which is what proves the scoreboard region is positioned relative to the playback region
        // rather than the full frame.
        frameSource.PublishFrame(new CapturedFrame
        {
            Width = 8,
            Height = 8,
            Format = FramePixelFormat.Bgra8,
            Stride = 32,
            Pixels = new byte[32 * 8],
            Timestamp = DateTimeOffset.UtcNow,
        });

        Assert.Equal(4, scoreboardOcr.LastWidth);
        Assert.Equal(1, scoreboardOcr.LastHeight);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithCadenceOfThree_DetectsOnFirstFrameAndPredictsOnlyOnTheNextTwo()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerDetector = new StubMultiClassObjectDetector();
        var playerTracker = new StubPlayerTracker();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            playerDetector,
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store),
            detectionIntervalFrames: 3);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame()); // frame index 0 - detection frame
        frameSource.PublishFrame(MakeFrame()); // frame index 1 - predict-only
        frameSource.PublishFrame(MakeFrame()); // frame index 2 - predict-only

        Assert.Equal(1, playerDetector.CallCount);
        Assert.Equal(1, playerTracker.UpdateCallCount);
        Assert.Equal(2, playerTracker.PredictOnlyCallCount);
    }

    [AvaloniaFact]
    public async Task FrameArrived_CallsJerseyNumberRecognizerOncePerTrackedPlayer_AndAggregatorOncePerFrame()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerTracker = new StubPlayerTracker { Tracks = [new TrackedPlayer(1, 0, 0, 2, 2, 0.9f), new TrackedPlayer(2, 2, 2, 4, 4, 0.9f)] };
        var jerseyNumberRecognizer = new StubJerseyNumberRecognizer();
        var jerseyNumberVoteAggregator = new StubJerseyNumberVoteAggregator();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            jerseyNumberRecognizer,
            jerseyNumberVoteAggregator,
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());

        Assert.Equal(2, jerseyNumberRecognizer.CallCount); // one per tracked player
        Assert.Equal(1, jerseyNumberVoteAggregator.UpdateCallCount);
    }

    [AvaloniaFact]
    public async Task FrameArrived_MapsTrackedPlayerToBoxAnnotationWithTrackIdLabel()
    {
        // Uses NullCourtKeypointDetector and no persisted calibration, so this exercises player tracking's
        // calibration-independent path (tracking/player-tracking spec) - it still needs a Confident sport
        // classification to get past the gate in OnFrameArrived, but nothing beyond that. Rendered as the
        // track's full box, labeled with its track ID - not a confidence-labeled foot-point.
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerTracker = new StubPlayerTracker { Tracks = [new TrackedPlayer(7, 1, 2, 3, 4, 0.876f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var box = Assert.Single(viewModel.RawOverlay.Annotations);
        Assert.Equal(OverlayShapeKind.Box, box.Shape);
        Assert.Equal("#7", box.Label);
        Assert.Equal([(1.0, 2.0), (3.0, 4.0)], box.Points);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithNonPlayerDetections_AddsOneOverlayAnnotationPerDetection()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var detector = new StubMultiClassObjectDetector
        {
            OtherDetections =
            [
                new OnCourtObjectDetection(1, 2, 3, 4, 0.9f, "Ball"),
                new OnCourtObjectDetection(5, 6, 7, 8, 0.8f, "Hoop"),
            ],
        };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            detector,
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store),
            detectionIntervalFrames: 1);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, viewModel.RawOverlay.Annotations.Count);

        var ball = Assert.Single(viewModel.RawOverlay.Annotations, a => a.Label == "Ball");
        Assert.Equal(OverlayShapeKind.Box, ball.Shape);
        Assert.Equal("Ball", ball.StyleKey);
        Assert.Equal([(1.0, 2.0), (3.0, 4.0)], ball.Points);

        var hoop = Assert.Single(viewModel.RawOverlay.Annotations, a => a.Label == "Hoop");
        Assert.Equal("Hoop", hoop.StyleKey);
        Assert.Equal([(5.0, 6.0), (7.0, 8.0)], hoop.Points);
    }

    [AvaloniaFact]
    public async Task FrameArrived_MultipleScoreboardRelatedDetections_UsesUnionBoundingBoxAsOcrCropRegion()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var detector = new StubMultiClassObjectDetector
        {
            OtherDetections =
            [
                new OnCourtObjectDetection(20, 5, 60, 25, 0.9f, "Period"),
                new OnCourtObjectDetection(10, 15, 40, 45, 0.9f, "Time Remaining"),
            ],
        };
        var scoreboardOcr = new StubScoreboardOcrEngine();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            detector,
            new ByteTrackPlayerTracker(),
            scoreboardOcr,
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store),
            detectionIntervalFrames: 1);
        await Task.Delay(50);

        frameSource.PublishFrame(new CapturedFrame
        {
            Width = 100,
            Height = 50,
            Format = FramePixelFormat.Bgra8,
            Stride = 400,
            Pixels = new byte[400 * 50],
            Timestamp = DateTimeOffset.UtcNow,
        });

        // Union of (20,5,60,25) and (10,15,40,45) is (10,5,60,45): a 50x40 crop. Naively taking only the max
        // right/bottom edges while assuming left/top are 0 would instead produce a 60x45 crop - the differing
        // width/height here proves both the min and max edges are used.
        Assert.Equal(50, scoreboardOcr.LastWidth);
        Assert.Equal(40, scoreboardOcr.LastHeight);
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithNoTracks_ProducesNoLeftoverBoxAnnotationsFromPriorFrame()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerTracker = new StubPlayerTracker { Tracks = [new TrackedPlayer(1, 0, 0, 1, 1, 0.9f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Single(viewModel.RawOverlay.Annotations);

        playerTracker.Tracks = [];
        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Empty(viewModel.RawOverlay.Annotations);
    }

    [AvaloniaFact]
    public async Task SwitchingSource_ResetsPlayerTrackerState()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerTracker = new StubPlayerTracker();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA, SourceB]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new StubMultiClassObjectDetector(),
            playerTracker,
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);

        Assert.Equal(1, playerTracker.ResetCallCount); // initial source selection also switches "into" source A

        viewModel.SourcePicker.SelectedSource = SourceB;
        await Task.Delay(50);

        Assert.Equal(2, playerTracker.ResetCallCount);
    }

    [AvaloniaFact]
    public async Task SwitchingSource_MidCadenceCycle_ResetsFrameCounterSoNewSourceDetectsImmediately()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerDetector = new StubMultiClassObjectDetector();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA, SourceB]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            playerDetector,
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store),
            detectionIntervalFrames: 3);
        await Task.Delay(50);

        // Frame index 0 on source A: detection frame. Frame index 1: predict-only - leaves the counter
        // mid-cycle (not back at a multiple of 3) when the source switch happens.
        frameSource.PublishFrame(MakeFrame());
        frameSource.PublishFrame(MakeFrame());
        Assert.Equal(1, playerDetector.CallCount);

        viewModel.SourcePicker.SelectedSource = SourceB;
        await Task.Delay(50);

        // Source B's first frame must be a real detection frame (counter reset to 0), not a predict-only
        // frame left over from wherever source A's cycle happened to stop.
        frameSource.PublishFrame(MakeFrame());
        Assert.Equal(2, playerDetector.CallCount);
    }

    [AvaloniaFact]
    public async Task SwitchingSource_ResetsAnnotationsAndMinimapState()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var keypoints = KnownCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        new CourtCalibrationCoordinator(store).ManualCalibrate(SourceIdentity.DeriveKey(SourceA), SportType.Basketball, KnownCorrespondences());

        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA, SourceB]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new StubKeypointDetector(keypoints),
            new NullMultiClassObjectDetector(),
            new ByteTrackPlayerTracker(),
            new NullScoreboardOcrEngine(),
            store,
            new NullJerseyNumberRecognizer(),
            new PluralityJerseyNumberVoteAggregator(),
            new PlaybackRegionCoordinator(store));
        await Task.Delay(50);
        frameSource.PublishFrame(MakeFrame());
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewModel.Minimap.HasValidCalibration);

        viewModel.SourcePicker.SelectedSource = SourceB;
        await Task.Delay(50);

        Assert.Equal(SourceB, frameSource.ActiveSource);
        Assert.Empty(viewModel.RawOverlay.Annotations);
        Assert.Empty(viewModel.Minimap.Markers);
        Assert.False(viewModel.Minimap.HasValidCalibration); // source B has no saved calibration under its own key
        Assert.Equal(SourceIdentity.DeriveKey(SourceB), viewModel.ManualCalibration.SourceKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
