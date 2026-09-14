namespace NBA.Vision;

/// <summary>
/// Orchestrates the court-calibration spec's persistence/invalidation/reuse rules on top of
/// <see cref="HomographyCalibrator"/>: run automatic or manual calibration, persist the result to the
/// source's <see cref="SourceProfile"/> alongside its sport classification, and invalidate/offer reuse per
/// the "Calibration is scoped per capture source and sport" and "Persist and reuse calibration per source"
/// requirements.
/// </summary>
public sealed class CourtCalibrationCoordinator(ISourceProfileStore profileStore)
{
    /// <summary>Returns the calibration currently valid for this source, only if it was computed for <paramref name="currentSport"/>; null otherwise (none saved, or saved for a different sport).</summary>
    public CalibrationData? GetValidCalibration(string sourceKey, SportType currentSport)
    {
        var profile = profileStore.Load(sourceKey);
        return profile?.Calibration is { } calibration && calibration.Sport == currentSport
            ? calibration
            : null;
    }

    /// <summary>Runs automatic calibration via <paramref name="detector"/>; degrades to a "not enough points" failure rather than computing an unreliable homography, and persists on success.</summary>
    public CalibrationResult TryAutoCalibrate(
        string sourceKey,
        SportType sport,
        ICourtKeypointDetector detector,
        ReadOnlySpan<byte> bgra8Pixels,
        int width,
        int height,
        int stride)
    {
        var detected = detector.Detect(bgra8Pixels, width, height, stride);
        if (detected.Count < HomographyCalibrator.MinimumPoints)
        {
            return CalibrationResult.Fail(
                $"Not enough court keypoints detected yet ({detected.Count}/{HomographyCalibrator.MinimumPoints} minimum) - calibration is not possible for this frame.");
        }

        var correspondences = detected.Select(k => new LandmarkCorrespondence(k.Position, k.LandmarkName)).ToList();
        var result = HomographyCalibrator.Compute(sport, correspondences);
        if (result.Success)
        {
            Persist(sourceKey, result.Calibration!);
        }

        return result;
    }

    /// <summary>Runs manual calibration from user-marked points. Unavailable (rather than attempted) when no supported sport is selected for this source.</summary>
    public CalibrationResult ManualCalibrate(string sourceKey, SportType? currentSport, IReadOnlyList<LandmarkCorrespondence> points)
    {
        if (currentSport is not { } sport || !CourtGeometryRegistry.IsSupported(sport))
        {
            return CalibrationResult.Fail("Manual calibration is unavailable until a supported sport is selected for this source.");
        }

        var result = HomographyCalibrator.Compute(sport, points);
        if (result.Success)
        {
            Persist(sourceKey, result.Calibration!);
        }

        return result;
    }

    /// <summary>Discards a saved calibration that no longer applies because the source's sport changed. Call when the capture source or its classified/overridden sport changes.</summary>
    public void InvalidateIfStale(string sourceKey, SportType currentSport)
    {
        var profile = profileStore.Load(sourceKey);
        if (profile?.Calibration is { } calibration && calibration.Sport != currentSport)
        {
            profile.Calibration = null;
            profileStore.Save(profile);
        }
    }

    private void Persist(string sourceKey, CalibrationData calibration)
    {
        var profile = profileStore.Load(sourceKey) ?? new SourceProfile { SourceKey = sourceKey };
        profile.Calibration = calibration;
        profileStore.Save(profile);
    }
}
