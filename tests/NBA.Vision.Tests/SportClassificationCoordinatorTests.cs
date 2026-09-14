using NBA.Vision;

namespace NBA.Vision.Tests;

public class SportClassificationCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"nba-profiles-{Guid.NewGuid():N}");

    private sealed class StubClassifier(SportClassifierOutput output) : ISportClassifier
    {
        public int CallCount { get; private set; }

        public SportClassifierOutput Classify(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
        {
            CallCount++;
            return output;
        }
    }

    private static SportClassificationCoordinator.FrameSnapshot MakeFrame() => new([1, 2, 3, 4], 1, 1, 4);

    [Fact]
    public void ClassifyOrGetCached_FirstCall_InvokesClassifier()
    {
        var classifier = new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.9f));
        var coordinator = new SportClassificationCoordinator(classifier, new FileSourceProfileStore(_directory));

        var result = coordinator.ClassifyOrGetCached("source-1", MakeFrame);

        Assert.Equal(1, classifier.CallCount);
        Assert.Equal(SportType.Basketball, result.Sport);
        Assert.Equal(SportClassificationStatus.Confident, result.Status);
    }

    [Fact]
    public void ClassifyOrGetCached_SecondCallForSameSource_ReusesCache_DoesNotReclassify()
    {
        var classifier = new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.9f));
        var coordinator = new SportClassificationCoordinator(classifier, new FileSourceProfileStore(_directory));

        coordinator.ClassifyOrGetCached("source-1", MakeFrame);
        coordinator.ClassifyOrGetCached("source-1", MakeFrame);
        coordinator.ClassifyOrGetCached("source-1", MakeFrame);

        Assert.Equal(1, classifier.CallCount); // still just the first call - classify-once-per-source-selection
    }

    [Fact]
    public void ClassifyOrGetCached_ReselectingSourceAfterProcessRestart_ReusesPersistedCache()
    {
        // Simulates reselecting a source in a *new* coordinator instance (e.g. after an app restart) -
        // the cache must be the persisted SourceProfile, not merely an in-memory dictionary.
        var store = new FileSourceProfileStore(_directory);
        var classifier = new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.9f));
        new SportClassificationCoordinator(classifier, store).ClassifyOrGetCached("source-1", MakeFrame);

        var freshClassifier = new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.9f));
        var freshCoordinator = new SportClassificationCoordinator(freshClassifier, store);
        freshCoordinator.ClassifyOrGetCached("source-1", MakeFrame);

        Assert.Equal(0, freshClassifier.CallCount);
    }

    [Fact]
    public void BelowThreshold_ReportsUnknown_NotACommittedGuess()
    {
        var classifier = new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.3f));
        var coordinator = new SportClassificationCoordinator(classifier, new FileSourceProfileStore(_directory), confidenceThreshold: 0.6f);

        var result = coordinator.ClassifyOrGetCached("source-1", MakeFrame);

        Assert.Equal(SportType.Unknown, result.Sport);
        Assert.Equal(SportClassificationStatus.Unknown, result.Status);
    }

    [Fact]
    public void ConfidentButUnregisteredSport_ReportsRecognizedButUnsupported_NotBasketballConfiguration()
    {
        var classifier = new StubClassifier(new SportClassifierOutput(new SportType("soccer"), 0.95f));
        var coordinator = new SportClassificationCoordinator(classifier, new FileSourceProfileStore(_directory));

        var result = coordinator.ClassifyOrGetCached("source-1", MakeFrame);

        Assert.Equal(new SportType("soccer"), result.Sport);
        Assert.Equal(SportClassificationStatus.RecognizedButUnsupported, result.Status);
    }

    [Fact]
    public void SetManualOverride_TakesPrecedenceOverCachedAutomaticResult()
    {
        var classifier = new StubClassifier(new SportClassifierOutput(SportType.Unknown, 0.1f));
        var store = new FileSourceProfileStore(_directory);
        var coordinator = new SportClassificationCoordinator(classifier, store);
        coordinator.ClassifyOrGetCached("source-1", MakeFrame); // caches "unknown"

        var overridden = coordinator.SetManualOverride("source-1", SportType.Basketball);

        Assert.Equal(SportType.Basketball, overridden.Sport);
        Assert.True(overridden.IsManualOverride);

        // Persisted and takes precedence on the next lookup too, without reclassifying.
        var subsequent = coordinator.ClassifyOrGetCached("source-1", MakeFrame);
        Assert.Equal(SportType.Basketball, subsequent.Sport);
        Assert.True(subsequent.IsManualOverride);
        Assert.Equal(1, classifier.CallCount); // only the original automatic call, not re-invoked after override
    }

    [Fact]
    public void SetManualOverride_PersistsAcrossCoordinatorInstances()
    {
        var store = new FileSourceProfileStore(_directory);
        new SportClassificationCoordinator(new StubClassifier(new SportClassifierOutput(SportType.Unknown, 0f)), store)
            .SetManualOverride("source-1", SportType.Basketball);

        var loaded = store.Load("source-1");

        Assert.NotNull(loaded?.Sport);
        Assert.Equal(SportType.Basketball, loaded!.Sport!.Sport);
        Assert.True(loaded.Sport.IsManualOverride);
    }

    [Fact]
    public void Reclassify_BypassesCache_InvokesClassifierAgain()
    {
        var classifier = new StubClassifier(new SportClassifierOutput(SportType.Basketball, 0.9f));
        var coordinator = new SportClassificationCoordinator(classifier, new FileSourceProfileStore(_directory));
        coordinator.ClassifyOrGetCached("source-1", MakeFrame);

        coordinator.Reclassify("source-1", MakeFrame());

        Assert.Equal(2, classifier.CallCount);
    }

    [Fact]
    public void NullSportClassifier_AlwaysReportsUnknown()
    {
        var coordinator = new SportClassificationCoordinator(new NullSportClassifier(), new FileSourceProfileStore(_directory));

        var result = coordinator.ClassifyOrGetCached("source-1", MakeFrame);

        Assert.Equal(SportType.Unknown, result.Sport);
        Assert.Equal(SportClassificationStatus.Unknown, result.Status);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
