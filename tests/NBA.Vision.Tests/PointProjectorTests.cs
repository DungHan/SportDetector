using NBA.Vision;

namespace NBA.Vision.Tests;

public class PointProjectorTests
{
    [Fact]
    public void Project_WithIdentityHomography_ReturnsSamePoint()
    {
        var identity = new CalibrationData(SportType.Basketball, [1, 0, 0, 0, 1, 0, 0, 0, 1], DateTimeOffset.UtcNow);

        var result = PointProjector.Project(identity, new ImagePoint(12.5, 7.25));

        Assert.Equal(12.5, result.X, precision: 6);
        Assert.Equal(7.25, result.Y, precision: 6);
    }

    [Fact]
    public void Project_WithScaleAndTranslateHomography_MapsCorrectly()
    {
        // court = image * 0.1 - (10, 5), i.e. image = court*10 + (100,50) inverted.
        // Row-major 3x3: [0.1 0 -10; 0 0.1 -5; 0 0 1]
        var calibration = new CalibrationData(SportType.Basketball, [0.1, 0, -10, 0, 0.1, -5, 0, 0, 1], DateTimeOffset.UtcNow);

        var result = PointProjector.Project(calibration, new ImagePoint(200, 150));

        Assert.Equal(10.0, result.X, precision: 6); // 200*0.1 - 10
        Assert.Equal(10.0, result.Y, precision: 6); // 150*0.1 - 5
    }

    [Fact]
    public void Project_WithPerspectiveHomography_DividesByW()
    {
        // A homography with a non-trivial bottom row (perspective term) - verifies the division-by-w step
        // is actually applied, not just the affine sub-case.
        var calibration = new CalibrationData(SportType.Basketball, [2, 0, 0, 0, 2, 0, 0.01, 0, 1], DateTimeOffset.UtcNow);

        var result = PointProjector.Project(calibration, new ImagePoint(10, 10));

        var expectedW = (0.01 * 10) + 1; // 1.1
        Assert.Equal(20 / expectedW, result.X, precision: 6);
        Assert.Equal(20 / expectedW, result.Y, precision: 6);
    }
}
