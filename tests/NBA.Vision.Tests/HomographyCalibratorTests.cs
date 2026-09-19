using NBA.Vision;

namespace NBA.Vision.Tests;

public class HomographyCalibratorTests
{
    // A simple, known, invertible image<->court transform (pure scale + translate - a homography is a
    // superset of this) used to verify Cv2.FindHomography actually recovers the right mapping, not just that
    // it returns *some* 3x3 matrix. imageX = courtX*10 + 100, imageY = courtY*10 + 50.
    private static ImagePoint ToImage(CourtPoint court) => new((court.X * 10) + 100, (court.Y * 10) + 50);

    private static IReadOnlyList<LandmarkCorrespondence> KnownCorrespondences() =>
    [
        new(ToImage(new CourtPoint(0, 0)), "BaselineCorner_Left_Near"),
        new(ToImage(new CourtPoint(28.6512, 0)), "BaselineCorner_Right_Near"),
        new(ToImage(new CourtPoint(0, 15.24)), "BaselineCorner_Left_Far"),
        new(ToImage(new CourtPoint(28.6512, 15.24)), "BaselineCorner_Right_Far"),
        new(ToImage(new CourtPoint(14.3256, 7.62)), "CenterCourt"),
    ];

    [Fact]
    public void Compute_WithKnownSyntheticTransform_RecoversCorrectMapping()
    {
        var result = HomographyCalibrator.Compute(SportType.Basketball, KnownCorrespondences());

        Assert.True(result.Success, result.FailureReason);
        Assert.NotNull(result.Calibration);

        // Round-trip every fitted point back through the recovered homography.
        var projected = PointProjector.Project(result.Calibration!, ToImage(new CourtPoint(14.3256, 7.62)));
        Assert.Equal(14.3256, projected.X, precision: 2);
        Assert.Equal(7.62, projected.Y, precision: 2);
    }

    [Fact]
    public void Compute_FewerThanMinimumPoints_Fails()
    {
        var threePoints = KnownCorrespondences().Take(3).ToList();

        var result = HomographyCalibrator.Compute(SportType.Basketball, threePoints);

        Assert.False(result.Success);
        Assert.Contains("4", result.FailureReason);
    }

    [Fact]
    public void Compute_UnsupportedSport_Fails()
    {
        var result = HomographyCalibrator.Compute(new SportType("soccer"), KnownCorrespondences());

        Assert.False(result.Success);
        Assert.Contains("soccer", result.FailureReason);
    }

    [Fact]
    public void Compute_WithOneOutlierAmongGoodPoints_StillRecoversCorrectMapping()
    {
        // 6 good points on the known scale+translate transform, plus a 7th whose image position is wildly
        // wrong for its claimed landmark - e.g. the keypoint model misfiring on one landmark, or a mislabeled
        // mirrored left/right pair (see BasketballGeometry's "Unverified risk" note). A plain least-squares fit
        // (no RANSAC) lets this one bad correspondence drag the whole homography off; RANSAC should instead
        // treat it as an outlier and fit from the 6 good ones.
        var goodPoints = new List<LandmarkCorrespondence>(KnownCorrespondences())
        {
            new(ToImage(new CourtPoint(14.3256, 0)), "MidCourtLine_SidelineA"),
            new(ToImage(new CourtPoint(14.3256, 15.24)), "MidCourtLine_SidelineB"),
        };

        var withOutlier = new List<LandmarkCorrespondence>(goodPoints)
        {
            [0] = new(new ImagePoint(9999, 9999), "BaselineCorner_Left_Near"),
        };

        var result = HomographyCalibrator.Compute(SportType.Basketball, withOutlier);

        Assert.True(result.Success, result.FailureReason);
        Assert.NotNull(result.Calibration);

        var projected = PointProjector.Project(result.Calibration!, ToImage(new CourtPoint(14.3256, 7.62)));
        Assert.Equal(14.3256, projected.X, precision: 1);
        Assert.Equal(7.62, projected.Y, precision: 1);
    }

    [Fact]
    public void Compute_UnknownLandmarkName_Fails()
    {
        var points = new List<LandmarkCorrespondence>(KnownCorrespondences())
        {
            [4] = new(ToImage(new CourtPoint(1, 1)), "NotARealLandmark"),
        };

        var result = HomographyCalibrator.Compute(SportType.Basketball, points);

        Assert.False(result.Success);
        Assert.Contains("NotARealLandmark", result.FailureReason);
    }
}
