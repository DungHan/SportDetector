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
}
