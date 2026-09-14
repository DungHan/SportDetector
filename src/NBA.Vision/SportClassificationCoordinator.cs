namespace NBA.Vision;

/// <summary>
/// Orchestrates the sport-classification spec's caching rules on top of a raw <see cref="ISportClassifier"/>:
/// classify at most once per source selection (reusing whatever is cached in the source's <see cref="SourceProfile"/>),
/// apply the confidence threshold and registry-support check, and let a manual override take precedence and persist.
/// </summary>
public sealed class SportClassificationCoordinator(
    ISportClassifier classifier,
    ISourceProfileStore profileStore,
    float confidenceThreshold = 0.6f)
{
    public readonly record struct FrameSnapshot(byte[] Pixels, int Width, int Height, int Stride);

    /// <summary>Returns the cached classification for <paramref name="sourceKey"/> if one exists; otherwise classifies <paramref name="frame"/> once and caches the result.</summary>
    public SportClassification ClassifyOrGetCached(string sourceKey, Func<FrameSnapshot> frame)
    {
        var profile = profileStore.Load(sourceKey) ?? new SourceProfile { SourceKey = sourceKey };
        if (profile.Sport is { } cached)
        {
            return cached;
        }

        return ClassifyAndCache(profile, frame());
    }

    /// <summary>Re-runs the automatic classifier for <paramref name="sourceKey"/>, bypassing any cached result (but a subsequent manual override still wins - see <see cref="SetManualOverride"/>).</summary>
    public SportClassification Reclassify(string sourceKey, FrameSnapshot frame)
    {
        var profile = profileStore.Load(sourceKey) ?? new SourceProfile { SourceKey = sourceKey };
        return ClassifyAndCache(profile, frame);
    }

    /// <summary>Sets and persists a manually chosen sport for <paramref name="sourceKey"/>, taking precedence over the automatic classifier.</summary>
    public SportClassification SetManualOverride(string sourceKey, SportType sport)
    {
        var profile = profileStore.Load(sourceKey) ?? new SourceProfile { SourceKey = sourceKey };
        var status = CourtGeometryRegistry.IsSupported(sport)
            ? SportClassificationStatus.Confident
            : SportClassificationStatus.RecognizedButUnsupported;

        var classification = new SportClassification(sport, Confidence: 1f, status, IsManualOverride: true);
        profile.Sport = classification;
        profileStore.Save(profile);
        return classification;
    }

    private SportClassification ClassifyAndCache(SourceProfile profile, FrameSnapshot frame)
    {
        var raw = classifier.Classify(frame.Pixels, frame.Width, frame.Height, frame.Stride);
        var classification = Evaluate(raw);
        profile.Sport = classification;
        profileStore.Save(profile);
        return classification;
    }

    private SportClassification Evaluate(SportClassifierOutput raw)
    {
        // raw.Sport == Unknown is checked before the registry lookup below, not just the confidence threshold:
        // a classifier can report Unknown confidently by design (e.g. ClipZeroShotSportClassifier's dedicated
        // catch-all prompt for "not a sports broadcast at all" - see design.md), and Unknown itself is never a
        // registered sport, so without this check it would incorrectly fall through to RecognizedButUnsupported.
        if (raw.Sport == SportType.Unknown || raw.Confidence < confidenceThreshold)
        {
            return new SportClassification(SportType.Unknown, raw.Confidence, SportClassificationStatus.Unknown, IsManualOverride: false);
        }

        var status = CourtGeometryRegistry.IsSupported(raw.Sport)
            ? SportClassificationStatus.Confident
            : SportClassificationStatus.RecognizedButUnsupported;

        return new SportClassification(raw.Sport, raw.Confidence, status, IsManualOverride: false);
    }
}
