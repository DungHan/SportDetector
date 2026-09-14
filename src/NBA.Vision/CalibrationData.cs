namespace NBA.Vision;

/// <summary>
/// A computed homography (row-major 3x3) mapping this source's image-space pixels to the given sport's
/// court-space coordinates. Scoped to a sport per the court-calibration spec's "Calibration is scoped per
/// capture source and sport" - a <see cref="SourceProfile"/> should discard this when the sport changes.
/// </summary>
public sealed record CalibrationData(SportType Sport, double[] HomographyRowMajor, DateTimeOffset ComputedAt)
{
    public double this[int row, int col] => HomographyRowMajor[(row * 3) + col];
}
