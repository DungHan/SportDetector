using Avalonia.Headless.XUnit;
using NBA.App.Models;
using NBA.App.ViewModels;
using NBA.Capture;
using NBA.Capture.Testing;
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

        public IReadOnlyList<DetectedKeypoint> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) => keypoints;
    }

    private sealed class StubPlayerDetector : IPlayerDetector
    {
        public IReadOnlyList<PlayerDetection> Detections { get; set; } = [];

        public int CallCount { get; private set; }

        public IReadOnlyList<PlayerDetection> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
        {
            CallCount++;
            return Detections;
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
            new SportClassificationCoordinator(new NullSportClassifier(), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new NullPlayerDetector());

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
            new SportClassificationCoordinator(new NullSportClassifier(), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            new NullPlayerDetector());
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());

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
            new NullPlayerDetector());
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        frameSource.PublishFrame(MakeFrame());
        frameSource.PublishFrame(MakeFrame());

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
        var sourceKey = "Window:processA";
        new CourtCalibrationCoordinator(store).ManualCalibrate(sourceKey, SportType.Basketball, KnownCorrespondences());

        var frameSource = new FakeFrameSource();
        var keypoints = KnownCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        var playerDetector = new StubPlayerDetector { Detections = [new PlayerDetection(1, 2, 3, 4, 0.876f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new StubKeypointDetector(keypoints),
            playerDetector);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());

        Assert.True(viewModel.Minimap.HasValidCalibration);
        Assert.Equal(keypoints.Count + 1, viewModel.RawOverlay.Annotations.Count);
        Assert.Equal(keypoints.Count, viewModel.Minimap.Markers.Count);

        var footAnnotation = Assert.Single(viewModel.RawOverlay.Annotations, a => a.Label == "88%");
        Assert.Equal(OverlayShapeKind.Point, footAnnotation.Shape);
        Assert.Equal([(2.0, 4.0)], footAnnotation.Points); // bottom-center of (1,2,3,4): the detected player's feet
    }

    [AvaloniaFact]
    public async Task FrameArrived_CallsPlayerDetectorOncePerFrame()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerDetector = new StubPlayerDetector();
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new NullSportClassifier(), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            playerDetector);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Assert.Equal(1, playerDetector.CallCount);

        frameSource.PublishFrame(MakeFrame());
        Assert.Equal(2, playerDetector.CallCount); // runs every frame, unlike sport classification's once-per-source caching
    }

    [AvaloniaFact]
    public async Task FrameArrived_MapsPlayerDetectionToFootPointAnnotationWithConfidenceLabel()
    {
        // Deliberately uses NullSportClassifier/NullCourtKeypointDetector so this exercises player detection's
        // sport-independent path (vision/player-detection spec) without depending on calibration being reused.
        // Marked at the bounding box's bottom-center (the player's feet), not as a box outline.
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerDetector = new StubPlayerDetector { Detections = [new PlayerDetection(1, 2, 3, 4, 0.876f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new NullSportClassifier(), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            playerDetector);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());

        var foot = Assert.Single(viewModel.RawOverlay.Annotations);
        Assert.Equal(OverlayShapeKind.Point, foot.Shape);
        Assert.Equal("88%", foot.Label);
        Assert.Equal([(2.0, 4.0)], foot.Points); // bottom-center of (1,2,3,4)
    }

    [AvaloniaFact]
    public async Task FrameArrived_WithNoPlayerDetections_ProducesNoLeftoverBoxAnnotationsFromPriorFrame()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var playerDetector = new StubPlayerDetector { Detections = [new PlayerDetection(0, 0, 1, 1, 0.5f)] };
        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA]),
            new SportClassificationCoordinator(new NullSportClassifier(), store),
            new CourtCalibrationCoordinator(store),
            new NullCourtKeypointDetector(SportType.Basketball),
            playerDetector);
        await Task.Delay(50);

        frameSource.PublishFrame(MakeFrame());
        Assert.Single(viewModel.RawOverlay.Annotations);

        playerDetector.Detections = [];
        frameSource.PublishFrame(MakeFrame());

        Assert.Empty(viewModel.RawOverlay.Annotations);
    }

    [AvaloniaFact]
    public async Task SwitchingSource_ResetsAnnotationsAndMinimapState()
    {
        var frameSource = new FakeFrameSource();
        var store = new FileSourceProfileStore(_directory);
        var keypoints = KnownCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        new CourtCalibrationCoordinator(store).ManualCalibrate("Window:processA", SportType.Basketball, KnownCorrespondences());

        await using var viewModel = new MainWindowViewModel(
            frameSource,
            new FakeCaptureSourceEnumerator([SourceA, SourceB]),
            new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.95f)), store),
            new CourtCalibrationCoordinator(store),
            new StubKeypointDetector(keypoints),
            new NullPlayerDetector());
        await Task.Delay(50);
        frameSource.PublishFrame(MakeFrame());
        Assert.True(viewModel.Minimap.HasValidCalibration);

        viewModel.SourcePicker.SelectedSource = SourceB;
        await Task.Delay(50);

        Assert.Equal(SourceB, frameSource.ActiveSource);
        Assert.Empty(viewModel.RawOverlay.Annotations);
        Assert.Empty(viewModel.Minimap.Markers);
        Assert.False(viewModel.Minimap.HasValidCalibration); // source B has no saved calibration under its own key
        Assert.Equal("Window:processB", viewModel.ManualCalibration.SourceKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
