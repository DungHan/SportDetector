namespace NBA.Vision;

/// <summary>Applies a computed homography to project image-space points into court-space. See the court-calibration spec's "Project image-space points to court-space" requirement.</summary>
public static class PointProjector
{
    public static CourtPoint Project(CalibrationData calibration, ImagePoint point)
    {
        var x = point.X;
        var y = point.Y;

        var denominator = (calibration[2, 0] * x) + (calibration[2, 1] * y) + calibration[2, 2];
        var courtX = ((calibration[0, 0] * x) + (calibration[0, 1] * y) + calibration[0, 2]) / denominator;
        var courtY = ((calibration[1, 0] * x) + (calibration[1, 1] * y) + calibration[1, 2]) / denominator;

        return new CourtPoint(courtX, courtY);
    }
}
