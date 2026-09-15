namespace NBA.Vision;

/// <summary>
/// Runs a <see cref="PlaybackRegionDetector"/> per capture source until it finds a region, then persists it into
/// that source's <see cref="SourceProfile"/> so it's computed once and reused - mirrors the "compute once, cache
/// in profile" pattern used by <see cref="SportClassificationCoordinator"/> and <see cref="CourtCalibrationCoordinator"/>.
/// Callers are expected to stop calling <see cref="Accumulate"/> for a source once they have a non-null region
/// for it (e.g. by keeping their own cached copy of the last returned/persisted region) - calling it again
/// afterwards just restarts accumulation from scratch for no benefit.
/// </summary>
public sealed class PlaybackRegionCoordinator(ISourceProfileStore profileStore)
{
    private readonly Dictionary<string, PlaybackRegionDetector> _detectors = new();

    /// <summary>Returns the region persisted for this source from an earlier call/session, if any.</summary>
    public NormalizedRect? GetPersistedRegion(string sourceKey) => profileStore.Load(sourceKey)?.PlaybackRegion;

    /// <summary>Folds one more frame into this source's in-progress detection. Returns the region the moment it's found (and persists it); null while still accumulating.</summary>
    public NormalizedRect? Accumulate(string sourceKey, ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        if (!_detectors.TryGetValue(sourceKey, out var detector))
        {
            detector = new PlaybackRegionDetector();
            _detectors[sourceKey] = detector;
        }

        detector.Accumulate(bgra8Pixels, width, height, stride);
        if (!detector.TryGetRegion(out var region))
        {
            return null;
        }

        _detectors.Remove(sourceKey);

        var profile = profileStore.Load(sourceKey) ?? new SourceProfile { SourceKey = sourceKey };
        profile.PlaybackRegion = region;
        profileStore.Save(profile);
        return region;
    }

    /// <summary>Discards in-progress accumulation and any persisted region for this source - call when a stale region would crop the wrong area (e.g. the same window now shows different content).</summary>
    public void Reset(string sourceKey)
    {
        _detectors.Remove(sourceKey);
        var profile = profileStore.Load(sourceKey);
        if (profile?.PlaybackRegion is not null)
        {
            profile.PlaybackRegion = null;
            profileStore.Save(profile);
        }
    }
}
