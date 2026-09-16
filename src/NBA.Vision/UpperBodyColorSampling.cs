namespace NBA.Vision;

/// <summary>
/// Computes a plain mean B/G/R color over a detection box's upper-body ("torso") sub-rectangle - a cheap
/// appearance signal for <c>tracking/player-tracking</c>'s team-color veto (add-team-color-track-gating
/// design.md's "plain byte-averaging over the BGRA span already in hand" decision), not a general color-
/// extraction feature. Operates directly on the same BGRA8 pixel span <see cref="OnnxPlayerDetector.Detect"/>
/// already has, needing no new imaging dependency.
/// </summary>
public static class UpperBodyColorSampling
{
    /// <summary>Fraction of the box's height, measured from its top, sampled as the upper-body region.</summary>
    public const double HeightFraction = 0.4;

    /// <summary>
    /// Narrows a detection box to its upper-body sub-rectangle (top <see cref="HeightFraction"/> of height,
    /// full width) - a pure function with no pixel access, independently testable from any buffer.
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom) UpperBodyRectangle(
        double left, double top, double right, double bottom)
    {
        var height = bottom - top;
        return (left, top, right, top + (height * HeightFraction));
    }

    /// <summary>
    /// Samples the mean B/G/R color of the pixels within the given rectangle, clamped here to
    /// <c>[0, width) x [0, height)</c> (the caller's rectangle - typically a detection box or its upper-body
    /// sub-rectangle above - is not assumed pre-clamped, since a box near a frame edge can extend slightly
    /// outside it). Returns <c>null</c> - the "not meaningful" sentinel - if the clamped region collapses to
    /// zero width or height, rather than a meaningless guess.
    /// </summary>
    public static (byte R, byte G, byte B)? MeanColor(
        ReadOnlySpan<byte> bgra8Pixels,
        int width,
        int height,
        int stride,
        double left,
        double top,
        double right,
        double bottom)
    {
        var x0 = Math.Max(0, (int)Math.Floor(left));
        var y0 = Math.Max(0, (int)Math.Floor(top));
        var x1 = Math.Min(width, (int)Math.Ceiling(right));
        var y1 = Math.Min(height, (int)Math.Ceiling(bottom));

        if (x1 <= x0 || y1 <= y0)
        {
            return null;
        }

        long sumB = 0;
        long sumG = 0;
        long sumR = 0;
        var count = 0;

        for (var y = y0; y < y1; y++)
        {
            var rowOffset = y * stride;
            for (var x = x0; x < x1; x++)
            {
                var pixelOffset = rowOffset + (x * 4);
                sumB += bgra8Pixels[pixelOffset];
                sumG += bgra8Pixels[pixelOffset + 1];
                sumR += bgra8Pixels[pixelOffset + 2];
                count++;
            }
        }

        return ((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
    }
}
