namespace NBA.Vision;

/// <summary>
/// Computes a dominant B/G/R color over a detection box's upper-body ("torso") sub-rectangle - a cheap
/// appearance signal for <c>tracking/player-tracking</c>'s team-color veto (add-team-color-track-gating
/// design.md's "plain byte-averaging over the BGRA span already in hand" decision, since evolved from a plain
/// mean - see <see cref="DominantColor"/> - to one that resists dilution by non-jersey pixels within the same
/// rectangle), not a general color-extraction feature. Operates directly on the same BGRA8 pixel span
/// <see cref="OnnxPlayerDetector.Detect"/> already has, needing no new imaging dependency or model.
/// </summary>
public static class UpperBodyColorSampling
{
    /// <summary>Fraction of the box's height, measured from its top, excluded as the head/hair region, for an upright (standing/walking) pose.</summary>
    public const double StandingTopMarginFraction = 0.18;

    /// <summary>Fraction of the box's height, measured from its top, sampled down to for an upright pose (down to the waist, stopping before shorts).</summary>
    public const double StandingHeightFraction = 0.52;

    /// <summary>Fraction of the box's width trimmed from each side for an upright pose.</summary>
    public const double StandingSideMarginFraction = 0.25;

    /// <summary>Same as <see cref="StandingTopMarginFraction"/>, but for a lunging/running pose - tighter, since a wide stride also raises the torso's top edge relative to the now-taller apparent box.</summary>
    public const double StridingTopMarginFraction = 0.15;

    /// <summary>Same as <see cref="StandingHeightFraction"/>, but for a lunging/running pose.</summary>
    public const double StridingHeightFraction = 0.42;

    /// <summary>Same as <see cref="StandingSideMarginFraction"/>, but for a lunging/running pose - wider, since an extended stride widens the box more than a torso-only crop would, and swinging arms need trimming too.</summary>
    public const double StridingSideMarginFraction = 0.35;

    /// <summary>Width/height ratio below which a box is treated as an upright standing/walking pose (the narrower, taller shape a torso keeps when the player isn't striding).</summary>
    public const double StandingAspectRatioThreshold = 0.45;

    /// <summary>
    /// Width/height ratio at or above which a box is treated as a dive/fall - wide, motion-blurred, and with no
    /// reliable torso location to crop to. <see cref="UpperBodyRectangle"/> returns <c>null</c> for these
    /// instead of guessing at a rectangle, so the caller skips color sampling for the frame entirely rather than
    /// averaging in whatever the geometry happens to land on.
    /// </summary>
    public const double DiveAspectRatioThreshold = 0.75;

    /// <summary>
    /// Narrows a detection box to its upper-body sub-rectangle, choosing the crop fractions from the box's own
    /// width/height ratio (solution 2's "dynamic aspect-ratio adaptive geometry") rather than one fixed ratio for
    /// every pose: a wide stride pushes the torso's relative position and the box's own proportions around, and a
    /// dive/fall is too blurred to crop reliably at all. A degenerate box (zero or negative height) has no
    /// meaningful ratio and is treated the same as a dive - <c>null</c>.
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom)? UpperBodyRectangle(
        double left, double top, double right, double bottom)
    {
        var width = right - left;
        var height = bottom - top;
        var aspectRatio = height > 0 ? width / height : double.PositiveInfinity;

        if (aspectRatio >= DiveAspectRatioThreshold)
        {
            return null;
        }

        var (topFraction, heightFraction, sideMarginFraction) = aspectRatio < StandingAspectRatioThreshold
            ? (StandingTopMarginFraction, StandingHeightFraction, StandingSideMarginFraction)
            : (StridingTopMarginFraction, StridingHeightFraction, StridingSideMarginFraction);

        return (
            left + (width * sideMarginFraction),
            top + (height * topFraction),
            right - (width * sideMarginFraction),
            top + (height * heightFraction));
    }

    /// <summary>Levels per channel the histogram in <see cref="DominantColor"/> quantizes into - coarse enough that a jersey's own shading/fold variance still falls into one bucket, fine enough to separate it from a distinctly different contaminant color (skin, court, shadow).</summary>
    private const int BucketLevels = 16;

    private const int BucketShift = 4; // log2(256 / BucketLevels), i.e. how many low bits each channel drops.

    private const int BucketCount = BucketLevels * BucketLevels * BucketLevels;

    /// <summary>
    /// Samples the dominant B/G/R color of the pixels within the given rectangle, clamped here to
    /// <c>[0, width) x [0, height)</c> (the caller's rectangle - typically a detection box's upper-body
    /// sub-rectangle above - is not assumed pre-clamped, since a box near a frame edge can extend slightly
    /// outside it). "Dominant" means the mean color of whichever quantized color bucket the most pixels fall
    /// into, not the mean of every pixel in the rectangle - a jersey normally fills most of this rectangle's
    /// area, so its bucket outnumbers any minority contaminant (skin, shadow, background bleeding in at an
    /// edge) regardless of what that contaminant's own color is, where a plain mean would get pulled toward
    /// gray by averaging all of them together. Returns <c>null</c> - the "not meaningful" sentinel - if the
    /// clamped region collapses to zero width or height, rather than a meaningless guess.
    /// </summary>
    public static (byte R, byte G, byte B)? DominantColor(
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

        var counts = new int[BucketCount];
        var sumR = new long[BucketCount];
        var sumG = new long[BucketCount];
        var sumB = new long[BucketCount];

        for (var y = y0; y < y1; y++)
        {
            var rowOffset = y * stride;
            for (var x = x0; x < x1; x++)
            {
                var pixelOffset = rowOffset + (x * 4);
                var b = bgra8Pixels[pixelOffset];
                var g = bgra8Pixels[pixelOffset + 1];
                var r = bgra8Pixels[pixelOffset + 2];

                var bucket = ((r >> BucketShift) * BucketLevels * BucketLevels)
                    + ((g >> BucketShift) * BucketLevels)
                    + (b >> BucketShift);
                counts[bucket]++;
                sumR[bucket] += r;
                sumG[bucket] += g;
                sumB[bucket] += b;
            }
        }

        var bestBucket = 0;
        for (var i = 1; i < BucketCount; i++)
        {
            if (counts[i] > counts[bestBucket])
            {
                bestBucket = i;
            }
        }

        return (
            (byte)(sumR[bestBucket] / counts[bestBucket]),
            (byte)(sumG[bestBucket] / counts[bestBucket]),
            (byte)(sumB[bestBucket] / counts[bestBucket]));
    }
}
