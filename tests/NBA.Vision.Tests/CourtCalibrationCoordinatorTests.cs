using NBA.Vision;

namespace NBA.Vision.Tests;

public class CourtCalibrationCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"nba-profiles-{Guid.NewGuid():N}");

    private static ImagePoint ToImage(CourtPoint court) => new((court.X * 10) + 100, (court.Y * 10) + 50);

    private static IReadOnlyList<LandmarkCorrespondence> KnownCorrespondences() =>
    [
        new(ToImage(new CourtPoint(0, 0)), "BaselineCorner_Left_Near"),
        new(ToImage(new CourtPoint(28.6512, 0)), "BaselineCorner_Right_Near"),
        new(ToImage(new CourtPoint(0, 15.24)), "BaselineCorner_Left_Far"),
        new(ToImage(new CourtPoint(28.6512, 15.24)), "BaselineCorner_Right_Far"),
    ];

    // The 9 landmarks BasketballGeometry actually assigns a KeypointIndex to - i.e. the ones a real keypoint
    // detector (or its stub below) could ever report - spread across both axes so they also clear
    // CourtCalibrationCoordinator's degenerate-input guard, not just its point-count minimum.
    private static IReadOnlyList<LandmarkCorrespondence> DetectableCorrespondences() =>
    [
        new(ToImage(new CourtPoint(14.3256, 7.62)), "CenterCourt"),
        new(ToImage(new CourtPoint(5.7912, 7.62)), "FreeThrowLineCenter_Left"),
        new(ToImage(new CourtPoint(22.86, 7.62)), "FreeThrowLineCenter_Right"),
        new(ToImage(new CourtPoint(14.3256, 0)), "MidCourtLine_SidelineA"),
        new(ToImage(new CourtPoint(14.3256, 15.24)), "MidCourtLine_SidelineB"),
        new(ToImage(new CourtPoint(0, 5.1816)), "PaintCorner_Left_A"),
        new(ToImage(new CourtPoint(0, 10.0584)), "PaintCorner_Left_B"),
        new(ToImage(new CourtPoint(28.6512, 5.1816)), "PaintCorner_Right_A"),
        new(ToImage(new CourtPoint(28.6512, 10.0584)), "PaintCorner_Right_B"),
    ];

    private sealed class StubKeypointDetector(SportType sport, IReadOnlyList<DetectedKeypoint> keypoints) : ICourtKeypointDetector
    {
        public SportType Sport { get; } = sport;

        public IReadOnlyList<DetectedKeypoint> Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) => keypoints;
    }

    [Fact]
    public void TryAutoCalibrate_WithPlaceholderNullDetector_ReportsNotEnoughPoints_NotAnUnreliableHomography()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));
        var detector = new NullCourtKeypointDetector(SportType.Basketball);

        var result = coordinator.TryAutoCalibrate("source-1", SportType.Basketball, detector, new byte[4], 1, 1, 4);

        Assert.False(result.Success);
        Assert.Contains("Not enough", result.FailureReason);
    }

    [Fact]
    public void TryAutoCalibrate_WithEnoughDetectedKeypoints_SucceedsAndPersists()
    {
        var store = new FileSourceProfileStore(_directory);
        var coordinator = new CourtCalibrationCoordinator(store);
        var keypoints = DetectableCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        var detector = new StubKeypointDetector(SportType.Basketball, keypoints);

        var result = coordinator.TryAutoCalibrate("source-1", SportType.Basketball, detector, new byte[4], 1, 1, 4);

        Assert.True(result.Success, result.FailureReason);
        var loaded = store.Load("source-1");
        Assert.NotNull(loaded?.Calibration);
        Assert.Equal(SportType.Basketball, loaded!.Calibration!.Sport);
    }

    [Fact]
    public void TryCalibrateFromKeypoints_FewerThanAutoCalibrateMinimum_FailsEvenThoughAboveHomographyMinimum()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));
        // 4 points clears HomographyCalibrator.MinimumPoints but not the stricter automatic-path threshold.
        var keypoints = DetectableCorrespondences().Take(4).Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();

        var result = coordinator.TryCalibrateFromKeypoints("source-1", SportType.Basketball, keypoints);

        Assert.False(result.Success);
        Assert.Contains("Not enough", result.FailureReason);
    }

    [Fact]
    public void TryCalibrateFromKeypoints_LandmarksAllFromOneCourtRegion_FailsAsInsufficientCoverage()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));
        // 3 distinct, non-collinear left-side-only landmarks (real court span: only ~20% of length, ~32% of
        // width), duplicated to clear the point-count minimum without adding any real coverage - a homography
        // fit only to this corner of the court would extrapolate badly for anything past it.
        var leftSideOnly = new[] { "PaintCorner_Left_A", "PaintCorner_Left_B", "FreeThrowLineCenter_Left" }
            .Select(name => DetectableCorrespondences().First(c => c.LandmarkName == name))
            .ToList();
        var keypoints = leftSideOnly.Concat(leftSideOnly)
            .Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f))
            .ToList();

        var result = coordinator.TryCalibrateFromKeypoints("source-1", SportType.Basketball, keypoints);

        Assert.False(result.Success);
        Assert.Contains("cover", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCalibrateFromKeypoints_PointsClusteredAlongOneAxis_FailsAsDegenerate()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));
        // Enough points to clear the count minimum, but all sharing the same court Y (a straight line) -
        // mathematically degenerate for a homography regardless of image resolution/scale.
        var keypoints = Enumerable.Range(0, 6)
            .Select(i => new DetectedKeypoint($"Landmark{i}", ToImage(new CourtPoint(i * 3.0, 7.62)), 0.99f))
            .ToList();

        var result = coordinator.TryCalibrateFromKeypoints("source-1", SportType.Basketball, keypoints);

        Assert.False(result.Success);
        Assert.Contains("clustered", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualCalibrate_FewerThanMinimumPoints_Fails()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));

        var result = coordinator.ManualCalibrate("source-1", SportType.Basketball, KnownCorrespondences().Take(2).ToList());

        Assert.False(result.Success);
    }

    [Fact]
    public void ManualCalibrate_NoSportSelected_ReportsUnavailable()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));

        var result = coordinator.ManualCalibrate("source-1", currentSport: null, KnownCorrespondences());

        Assert.False(result.Success);
        Assert.Contains("unavailable", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualCalibrate_UnsupportedSport_ReportsUnavailable_DoesNotAttempt()
    {
        var coordinator = new CourtCalibrationCoordinator(new FileSourceProfileStore(_directory));

        var result = coordinator.ManualCalibrate("source-1", new SportType("soccer"), KnownCorrespondences());

        Assert.False(result.Success);
        Assert.Contains("unavailable", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualCalibrate_Success_PersistsToSourceProfile()
    {
        var store = new FileSourceProfileStore(_directory);
        var coordinator = new CourtCalibrationCoordinator(store);

        var result = coordinator.ManualCalibrate("source-1", SportType.Basketball, KnownCorrespondences());

        Assert.True(result.Success, result.FailureReason);
        Assert.NotNull(store.Load("source-1")?.Calibration);
    }

    [Fact]
    public void GetValidCalibration_SameSourceAndSport_ReturnsSavedCalibration_OffersReuse()
    {
        var store = new FileSourceProfileStore(_directory);
        var coordinator = new CourtCalibrationCoordinator(store);
        coordinator.ManualCalibrate("source-1", SportType.Basketball, KnownCorrespondences());

        var calibration = coordinator.GetValidCalibration("source-1", SportType.Basketball);

        Assert.NotNull(calibration);
    }

    [Fact]
    public void GetValidCalibration_DifferentSportThanSaved_ReturnsNull_NotReused()
    {
        var store = new FileSourceProfileStore(_directory);
        var coordinator = new CourtCalibrationCoordinator(store);
        coordinator.ManualCalibrate("source-1", SportType.Basketball, KnownCorrespondences());

        var calibration = coordinator.GetValidCalibration("source-1", new SportType("soccer"));

        Assert.Null(calibration);
    }

    [Fact]
    public void InvalidateIfStale_SportChanged_DiscardsSavedCalibration()
    {
        var store = new FileSourceProfileStore(_directory);
        var coordinator = new CourtCalibrationCoordinator(store);
        coordinator.ManualCalibrate("source-1", SportType.Basketball, KnownCorrespondences());

        coordinator.InvalidateIfStale("source-1", new SportType("soccer"));

        Assert.Null(store.Load("source-1")?.Calibration);
    }

    [Fact]
    public void InvalidateIfStale_SameSport_KeepsSavedCalibration()
    {
        var store = new FileSourceProfileStore(_directory);
        var coordinator = new CourtCalibrationCoordinator(store);
        coordinator.ManualCalibrate("source-1", SportType.Basketball, KnownCorrespondences());

        coordinator.InvalidateIfStale("source-1", SportType.Basketball);

        Assert.NotNull(store.Load("source-1")?.Calibration);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
