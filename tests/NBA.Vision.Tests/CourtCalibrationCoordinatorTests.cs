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
        var keypoints = KnownCorrespondences().Select(c => new DetectedKeypoint(c.LandmarkName, c.Image, 0.99f)).ToList();
        var detector = new StubKeypointDetector(SportType.Basketball, keypoints);

        var result = coordinator.TryAutoCalibrate("source-1", SportType.Basketball, detector, new byte[4], 1, 1, 4);

        Assert.True(result.Success, result.FailureReason);
        var loaded = store.Load("source-1");
        Assert.NotNull(loaded?.Calibration);
        Assert.Equal(SportType.Basketball, loaded!.Calibration!.Sport);
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
