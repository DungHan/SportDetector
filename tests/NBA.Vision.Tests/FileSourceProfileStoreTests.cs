using NBA.Vision;

namespace NBA.Vision.Tests;

public class FileSourceProfileStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"nba-profiles-{Guid.NewGuid():N}");

    [Fact]
    public void Load_WithNoSavedProfile_ReturnsNull()
    {
        var store = new FileSourceProfileStore(_directory);

        Assert.Null(store.Load("nonexistent-source"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSportClassification()
    {
        var store = new FileSourceProfileStore(_directory);
        var profile = new SourceProfile
        {
            SourceKey = "chrome:youtube.com",
            Sport = new SportClassification(SportType.Basketball, 0.92f, SportClassificationStatus.Confident, IsManualOverride: false),
        };

        store.Save(profile);
        var loaded = store.Load("chrome:youtube.com");

        Assert.NotNull(loaded);
        Assert.Equal(SportType.Basketball, loaded!.Sport!.Sport);
        Assert.Equal(0.92f, loaded.Sport.Confidence, precision: 3);
        Assert.Equal(SportClassificationStatus.Confident, loaded.Sport.Status);
        Assert.False(loaded.Sport.IsManualOverride);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsCalibrationHomography()
    {
        var store = new FileSourceProfileStore(_directory);
        var homography = new double[] { 1, 0, 10, 0, 1, 20, 0, 0, 1 };
        var profile = new SourceProfile
        {
            SourceKey = "steam:nba2k",
            Calibration = new CalibrationData(SportType.Basketball, homography, DateTimeOffset.UtcNow),
        };

        store.Save(profile);
        var loaded = store.Load("steam:nba2k");

        Assert.NotNull(loaded?.Calibration);
        Assert.Equal(homography, loaded!.Calibration!.HomographyRowMajor);
        Assert.Equal(SportType.Basketball, loaded.Calibration.Sport);
    }

    [Fact]
    public void Save_OverwritesPreviousValueForSameKey()
    {
        var store = new FileSourceProfileStore(_directory);
        store.Save(new SourceProfile
        {
            SourceKey = "obs:1",
            Sport = new SportClassification(SportType.Unknown, 0f, SportClassificationStatus.Unknown, false),
        });

        store.Save(new SourceProfile
        {
            SourceKey = "obs:1",
            Sport = new SportClassification(SportType.Basketball, 0.8f, SportClassificationStatus.Confident, false),
        });

        var loaded = store.Load("obs:1");
        Assert.Equal(SportType.Basketball, loaded!.Sport!.Sport);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
