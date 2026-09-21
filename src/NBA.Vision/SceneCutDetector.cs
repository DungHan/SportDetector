namespace NBA.Vision;

/// <summary>
/// Detects a hard scene/camera cut between two frames of the same capture source, via a cheap grid-sampled
/// mean-color signature comparison. <see cref="CourtCalibrationCoordinator"/> persists a computed calibration
/// per source and reuses it indefinitely (dual-view-shell's "one continuous source" assumption) - correct for
/// watching a single live broadcast where the camera setup is stable, but wrong for a highlight reel that cuts
/// between different games/arenas/camera angles within the same capture session: the stale homography from an
/// earlier clip gets applied to a completely different court framing, throwing player positions to nonsensical
/// spots on the minimap. A large enough frame-to-frame signature change is the signal used to invalidate that
/// stale calibration (see the call site in <c>MainWindowViewModel</c>) so the next frame with enough confident
/// keypoints recalibrates instead of silently reusing a homography that no longer corresponds to anything on
/// screen.
/// </summary>
public static class SceneCutDetector
{
    /// <summary>Frame is split into a GridSize x GridSize grid of cells, each reduced to a mean B/G/R color.</summary>
    public const int GridSize = 8;

    /// <summary>Samples per cell per axis (9 samples/cell total) - enough to catch a cut without averaging every pixel.</summary>
    private const int SamplesPerCellSide = 3;

    /// <summary>
    /// Mean per-channel absolute difference (each channel in [0, 1]) above which two signatures are considered
    /// different scenes rather than the same shot panning/zooming - a placeholder, not yet empirically tuned
    /// against real broadcast footage (same caveat as this project's other hand-picked thresholds).
    /// </summary>
    public const double DefaultChangeThreshold = 0.2;

    /// <summary>
    /// Reduces a BGRA8 frame to a <see cref="GridSize"/> x <see cref="GridSize"/> grid of mean B/G/R colors
    /// (each channel in [0, 1]), sparsely sampled rather than averaging every pixel - cheap enough to run every
    /// time <see cref="IsCut"/> is checked, regardless of the source frame's actual resolution.
    /// </summary>
    public static double[] ComputeGridSignature(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        var signature = new double[GridSize * GridSize * 3];

        for (var cellY = 0; cellY < GridSize; cellY++)
        {
            for (var cellX = 0; cellX < GridSize; cellX++)
            {
                long sumB = 0;
                long sumG = 0;
                long sumR = 0;

                for (var sy = 0; sy < SamplesPerCellSide; sy++)
                {
                    var y = Math.Clamp((int)(((cellY + ((sy + 0.5) / SamplesPerCellSide)) * height) / GridSize), 0, height - 1);
                    for (var sx = 0; sx < SamplesPerCellSide; sx++)
                    {
                        var x = Math.Clamp((int)(((cellX + ((sx + 0.5) / SamplesPerCellSide)) * width) / GridSize), 0, width - 1);
                        var offset = (y * stride) + (x * 4);
                        sumB += bgra8Pixels[offset];
                        sumG += bgra8Pixels[offset + 1];
                        sumR += bgra8Pixels[offset + 2];
                    }
                }

                const int sampleCount = SamplesPerCellSide * SamplesPerCellSide;
                var cellIndex = ((cellY * GridSize) + cellX) * 3;
                signature[cellIndex] = sumB / (double)sampleCount / 255.0;
                signature[cellIndex + 1] = sumG / (double)sampleCount / 255.0;
                signature[cellIndex + 2] = sumR / (double)sampleCount / 255.0;
            }
        }

        return signature;
    }

    /// <summary>
    /// Whether <paramref name="currentSignature"/> represents a hard cut away from <paramref name="previousSignature"/>
    /// - both must come from <see cref="ComputeGridSignature"/> (same grid size, so same length).
    /// </summary>
    public static bool IsCut(double[] previousSignature, double[] currentSignature, double changeThreshold = DefaultChangeThreshold)
    {
        if (previousSignature.Length != currentSignature.Length)
        {
            throw new ArgumentException("Signatures must come from the same grid size to be comparable.");
        }

        var sumAbsoluteDifference = 0.0;
        for (var i = 0; i < previousSignature.Length; i++)
        {
            sumAbsoluteDifference += Math.Abs(previousSignature[i] - currentSignature[i]);
        }

        return (sumAbsoluteDifference / previousSignature.Length) > changeThreshold;
    }

    /// <summary>
    /// Marks which grid cells fall inside any of <paramref name="boxesInPixels"/> (e.g. currently-tracked player
    /// boxes, in the same pixel space this call's <paramref name="width"/>/<paramref name="height"/> describe -
    /// crop-local when a playback crop is active) - a cell counts as covered if its center point lands inside
    /// any box. Feeds <see cref="ComputeBackgroundChangeScore"/> so a player moving through the frame doesn't
    /// get mistaken for the camera itself moving (see MainWindowViewModel's keypoint-refresh gate).
    /// </summary>
    public static bool[] ComputeCellMask(IReadOnlyList<(double Left, double Top, double Right, double Bottom)> boxesInPixels, int width, int height)
    {
        var mask = new bool[GridSize * GridSize];
        if (boxesInPixels.Count == 0)
        {
            return mask;
        }

        for (var cellY = 0; cellY < GridSize; cellY++)
        {
            var cy = ((cellY + 0.5) * height) / GridSize;
            for (var cellX = 0; cellX < GridSize; cellX++)
            {
                var cx = ((cellX + 0.5) * width) / GridSize;
                for (var i = 0; i < boxesInPixels.Count; i++)
                {
                    var box = boxesInPixels[i];
                    if (cx >= box.Left && cx < box.Right && cy >= box.Top && cy < box.Bottom)
                    {
                        mask[(cellY * GridSize) + cellX] = true;
                        break;
                    }
                }
            }
        }

        return mask;
    }

    /// <summary>
    /// Same mean per-channel absolute difference as <see cref="IsCut"/>'s internal comparison, but restricted to
    /// cells <paramref name="mask"/> marks as background (false) - so known player-occupied cells (see
    /// <see cref="ComputeCellMask"/>) never contribute to the score. Returns null rather than a misleadingly
    /// confident number when fewer than <paramref name="minUnmaskedCellFraction"/> of the grid's cells are
    /// unmasked (e.g. a fast break spreading players across most of the court) - too few background samples left
    /// to trust either way; the caller should fall back to its own safety-net logic rather than treat null as
    /// "no change".
    /// </summary>
    public static double? ComputeBackgroundChangeScore(
        double[] previousSignature, double[] currentSignature, bool[] mask, double minUnmaskedCellFraction = 0.3)
    {
        if (previousSignature.Length != currentSignature.Length)
        {
            throw new ArgumentException("Signatures must come from the same grid size to be comparable.");
        }

        var unmaskedCells = 0;
        var sumAbsoluteDifference = 0.0;
        for (var cell = 0; cell < mask.Length; cell++)
        {
            if (mask[cell])
            {
                continue;
            }

            unmaskedCells++;
            var baseIndex = cell * 3;
            sumAbsoluteDifference += Math.Abs(previousSignature[baseIndex] - currentSignature[baseIndex])
                + Math.Abs(previousSignature[baseIndex + 1] - currentSignature[baseIndex + 1])
                + Math.Abs(previousSignature[baseIndex + 2] - currentSignature[baseIndex + 2]);
        }

        if (unmaskedCells < mask.Length * minUnmaskedCellFraction)
        {
            return null;
        }

        return sumAbsoluteDifference / (unmaskedCells * 3);
    }
}
