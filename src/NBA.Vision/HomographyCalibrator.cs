using OpenCvSharp;

namespace NBA.Vision;

/// <summary>
/// Computes a homography from image-space/landmark correspondences via OpenCvSharp4, using the given sport's
/// registered geometry for the landmark side. Used by both the automatic (keypoint-model) and manual
/// calibration paths - see the court-calibration spec's "Compute homography from detected court keypoints"
/// and "Manual calibration fallback" requirements, which share this same minimum-4-points, sport-geometry-driven
/// computation.
/// </summary>
public static class HomographyCalibrator
{
    public const int MinimumPoints = 4;

    /// <summary>
    /// Reprojection-error threshold (in court-space meters, since <see cref="Compute"/> fits image points onto
    /// court points) for RANSAC to accept a correspondence as an inlier rather than discarding it. Without
    /// RANSAC, <c>Cv2.FindHomography</c> defaults to a plain least-squares fit over every correspondence given -
    /// with 5+ auto-calibration points (see <see cref="CourtCalibrationCoordinator.MinimumAutoCalibratePoints"/>)
    /// that means a single bad one (e.g. the keypoint model misfiring on one landmark during a fast camera pan,
    /// or - per BasketballGeometry's "Unverified risk" note - a mirrored left/right pair whose index turns out
    /// swapped) drags the *entire* homography toward garbage instead of being outvoted by the good points, which
    /// is consistent with markers scattering specifically when the camera is moving fastest (crossing
    /// half-court on a fast break). 0.6m (~2ft) is tight relative to real landmark spacing (the tightest, e.g.
    /// paint corner to corner-three, is ~1.5m) but loose enough for ordinary detector pixel jitter at broadcast
    /// resolution.
    /// </summary>
    private const double RansacReprojectionThresholdMeters = 0.6;

    public static CalibrationResult Compute(SportType sport, IReadOnlyList<LandmarkCorrespondence> correspondences)
    {
        if (!CourtGeometryRegistry.TryGet(sport, out var geometry))
        {
            return CalibrationResult.Fail($"Sport '{sport}' has no registered court geometry - calibration is unavailable for it.");
        }

        if (correspondences.Count < MinimumPoints)
        {
            return CalibrationResult.Fail($"At least {MinimumPoints} points are required to calibrate ({correspondences.Count} provided).");
        }

        var imagePoints = new List<Point2f>(correspondences.Count);
        var courtPoints = new List<Point2f>(correspondences.Count);

        foreach (var correspondence in correspondences)
        {
            var landmark = geometry.FindLandmark(correspondence.LandmarkName);
            if (landmark is null)
            {
                return CalibrationResult.Fail($"'{correspondence.LandmarkName}' is not a known landmark for sport '{sport}'.");
            }

            imagePoints.Add(new Point2f((float)correspondence.Image.X, (float)correspondence.Image.Y));
            courtPoints.Add(new Point2f((float)landmark.X, (float)landmark.Y));
        }

        using var imageInput = InputArray.Create(imagePoints.ToArray());
        using var courtInput = InputArray.Create(courtPoints.ToArray());
        using var homographyMat = Cv2.FindHomography(imageInput, courtInput, HomographyMethods.Ransac, RansacReprojectionThresholdMeters);
        if (homographyMat.Empty() || homographyMat.Rows != 3 || homographyMat.Cols != 3)
        {
            return CalibrationResult.Fail("Could not compute a homography from the given points - check they are not collinear or duplicated.");
        }

        var values = new double[9];
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                values[(row * 3) + col] = homographyMat.At<double>(row, col);
            }
        }

        return CalibrationResult.Ok(new CalibrationData(sport, values, DateTimeOffset.UtcNow));
    }
}
