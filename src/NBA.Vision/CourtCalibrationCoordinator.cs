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
    /// <summary>
    /// Stricter than <see cref="HomographyCalibrator.MinimumPoints"/> - manual calibration has a human
    /// deliberately picking 4 well-spread points, but the automatic path just takes whatever the detector's
    /// confidence threshold happened to clear on one frame, with no say over which points those are. Confirmed
    /// live: a homography computed from exactly 4 confidently-detected points was mathematically valid but
    /// projected real, spread-out tracked players into a tight cluster near mid-court - degenerate in practice,
    /// not just in theory. This value (5) and the reasoning above predate BasketballGeometry.cs's full
    /// 33-keypoint mapping (previously only 9 landmarks had a KeypointIndex at all); "a clear majority of a
    /// sport's detectable landmarks" no longer describes 5 out of 33, and <see cref="MinimumCourtCoverageFraction"/>
    /// below is now the primary defense against a clustered-but-numerous fit (e.g. 5+ points that are all real,
    /// confidently-detected, and yet all sit in one corner's now-denser landmark cluster - lane corners, corner
    /// three, three-point transition, all within a few feet of each other). Revisit both constants against real
    /// footage once the full mapping has been exercised - this was not re-tuned as part of the mapping fix.
    /// </summary>
    public const int MinimumAutoCalibratePoints = 5;

    /// <summary>
    /// Minimum fraction of the court's real length/width the *court-space* landmarks (not the image-space
    /// points) must span. Raising <see cref="MinimumAutoCalibratePoints"/> alone still let a homography fit
    /// tightly to landmarks bunched in one half of the court (e.g. only left-side paint/free-throw/center-line
    /// points) - a locally fine fit that extrapolates badly for anything outside that region, which is exactly
    /// where real players stand. Confirmed live: 6+ non-collinear points still produced players bunched near
    /// mid-court instead of spread across it. This is checked in real court meters (known exactly from the
    /// sport's registered geometry), not image pixels, so it doesn't depend on the source's resolution/crop.
    /// Lowered from 0.4 to 0.3 alongside <see cref="MinimumAutoCalibratePoints"/> for the same reason - still
    /// meaningfully stricter than no coverage check at all.
    /// </summary>
    private const double MinimumCourtCoverageFraction = 0.3;

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
        return TryCalibrateFromKeypoints(sourceKey, sport, detected);
    }

    /// <summary>
    /// Same automatic-calibration rules as <see cref="TryAutoCalibrate"/>, but from keypoints the caller already
    /// detected (e.g. reused from the raw-overlay's own detection pass) instead of running inference again.
    /// </summary>
    public CalibrationResult TryCalibrateFromKeypoints(string sourceKey, SportType sport, IReadOnlyList<DetectedKeypoint> keypoints)
    {
        if (keypoints.Count < MinimumAutoCalibratePoints)
        {
            return CalibrationResult.Fail(
                $"Not enough court keypoints detected yet for automatic calibration ({keypoints.Count}/{MinimumAutoCalibratePoints} minimum) - calibration is not possible for this frame.");
        }

        if (IsTooClusteredToTrust(keypoints))
        {
            return CalibrationResult.Fail("Detected keypoints are clustered along one axis - too degenerate to calibrate reliably from this frame.");
        }

        if (!CourtGeometryRegistry.TryGet(sport, out var geometry))
        {
            return CalibrationResult.Fail($"Sport '{sport}' has no registered court geometry - calibration is unavailable for it.");
        }

        if (!CoversEnoughOfTheCourt(geometry, keypoints))
        {
            return CalibrationResult.Fail(
                $"Detected keypoints only cover one region of the court (need landmarks spanning at least {MinimumCourtCoverageFraction:P0} of its length and width) - waiting for a frame with a wider spread before trusting a homography from it.");
        }

        var correspondences = keypoints.Select(k => new LandmarkCorrespondence(k.Position, k.LandmarkName)).ToList();
        var result = HomographyCalibrator.Compute(sport, correspondences);
        if (result.Success)
        {
            Persist(sourceKey, result.Calibration!);
        }

        return result;
    }

    /// <summary>Cheap, scale-invariant degenerate-input guard: a homography needs points spread across both
    /// image axes, not clustered along a single line - true regardless of the source's actual resolution.</summary>
    private static bool IsTooClusteredToTrust(IReadOnlyList<DetectedKeypoint> keypoints)
    {
        var xs = keypoints.Select(k => k.Position.X).ToList();
        var ys = keypoints.Select(k => k.Position.Y).ToList();
        return (xs.Max() - xs.Min()) < 1.0 || (ys.Max() - ys.Min()) < 1.0;
    }

    /// <summary>Whether the *landmarks'* known real-world positions (not their detected image positions) span
    /// enough of the court to make the resulting homography trustworthy outside the exact points it was fit
    /// from. Unrecognized landmark names are ignored here - <see cref="HomographyCalibrator.Compute"/> is what
    /// reports those as a real failure, once this check has already passed or failed on the recognized ones.</summary>
    private static bool CoversEnoughOfTheCourt(CourtGeometryDefinition geometry, IReadOnlyList<DetectedKeypoint> keypoints)
    {
        var courtPoints = keypoints
            .Select(k => geometry.FindLandmark(k.LandmarkName))
            .Where(landmark => landmark is not null)
            .Cast<CourtLandmark>()
            .ToList();

        if (courtPoints.Count == 0)
        {
            return false;
        }

        var spanX = courtPoints.Max(l => l.X) - courtPoints.Min(l => l.X);
        var spanY = courtPoints.Max(l => l.Y) - courtPoints.Min(l => l.Y);

        return spanX >= geometry.SurfaceLengthMeters * MinimumCourtCoverageFraction
            && spanY >= geometry.SurfaceWidthMeters * MinimumCourtCoverageFraction;
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
