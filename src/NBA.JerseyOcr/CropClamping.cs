namespace NBA.JerseyOcr;

/// <summary>
/// Clamps a crop rectangle to a frame's bounds - a track's motion-predicted box (used during brief occlusion,
/// tracking/player-tracking) can extend outside the frame, and a source-space crop rectangle must be within
/// <c>[0, width) x [0, height)</c> before it can be sampled (see design.md's degenerate-crop handling).
/// </summary>
public static class CropClamping
{
    public static ClampedCrop Clamp(double left, double top, double right, double bottom, int width, int height)
    {
        var clampedLeft = (int)Math.Clamp(Math.Floor(left), 0, width);
        var clampedTop = (int)Math.Clamp(Math.Floor(top), 0, height);
        var clampedRight = (int)Math.Clamp(Math.Ceiling(right), 0, width);
        var clampedBottom = (int)Math.Clamp(Math.Ceiling(bottom), 0, height);

        var isDegenerate = clampedRight <= clampedLeft || clampedBottom <= clampedTop;
        return new ClampedCrop(clampedLeft, clampedTop, clampedRight, clampedBottom, isDegenerate);
    }
}
