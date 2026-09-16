namespace NBA.JerseyOcr.Tests;

public class CropClampingTests
{
    [Fact]
    public void Clamp_BoxFullyInsideBounds_IsUnchanged()
    {
        var result = CropClamping.Clamp(left: 10, top: 20, right: 30, bottom: 40, width: 100, height: 100);

        Assert.Equal(new ClampedCrop(10, 20, 30, 40, IsDegenerate: false), result);
    }

    [Fact]
    public void Clamp_BoxPartiallyOutsideBounds_IsClamped()
    {
        var result = CropClamping.Clamp(left: -10, top: -5, right: 50, bottom: 120, width: 40, height: 100);

        Assert.Equal(new ClampedCrop(0, 0, 40, 100, IsDegenerate: false), result);
    }

    [Fact]
    public void Clamp_BoxFullyOutsideBounds_IsReportedAsDegenerate()
    {
        var result = CropClamping.Clamp(left: 150, top: 150, right: 200, bottom: 200, width: 100, height: 100);

        Assert.True(result.IsDegenerate);
    }

    [Fact]
    public void Clamp_BoxCollapsingToZeroWidthAfterClamping_IsReportedAsDegenerate()
    {
        // Right (50) clamps to width (40), left (30) stays - collapses to zero width (30..40 is fine,
        // but here left is set past the clamped right to force a collapse).
        var result = CropClamping.Clamp(left: 40, top: 10, right: 50, bottom: 20, width: 40, height: 100);

        Assert.True(result.IsDegenerate);
    }
}
