namespace NBA.Vision;

/// <summary>
/// Auto-detects which normalized sub-rectangle of a captured frame is actually "the game" versus surrounding
/// chrome (YouTube page, browser UI, ...), by accumulating per-cell brightness-difference energy over many
/// frames, then projecting it onto each axis (summing every column's energy down to one value per column, and
/// every row's down to one value per row) and taking the longest contiguous run of columns/rows whose summed
/// energy stands out from background noise. This is deliberately not a two-frame diff: a single-frame flicker,
/// or an isolated UI element that moves on its own (an autoplay thumbnail, a blinking icon), doesn't survive
/// averaging over <see cref="_minimumFrames"/> samples. And it's deliberately a per-axis projection rather than
/// 2D blob connectivity: a cell that's briefly quiet (e.g. a player who barely moved this sampling window)
/// still leaves its row and column with energy from everything else moving through them, so it doesn't fracture
/// the game region the way a strict 2D connected-component search would.
/// </summary>
public sealed class PlaybackRegionDetector(int gridWidth = 32, int gridHeight = 18, int minimumFrames = 45)
{
    /// <summary>Rows/columns whose summed energy is below this fraction of the busiest row's/column's energy are treated as background noise.</summary>
    public const float ActivityThresholdRatio = 0.15f;

    private readonly float[] _previousCellLuma = new float[gridWidth * gridHeight];
    private readonly float[] _cellEnergy = new float[gridWidth * gridHeight];
    private bool _hasPrevious;

    public int FramesAccumulated { get; private set; }

    /// <summary>Clears all accumulated state - call when switching capture source, since motion energy from a previous source is meaningless for the new one.</summary>
    public void Reset()
    {
        Array.Clear(_previousCellLuma);
        Array.Clear(_cellEnergy);
        _hasPrevious = false;
        FramesAccumulated = 0;
    }

    /// <summary>Folds one more frame into the accumulated motion energy. Call this once per captured frame.</summary>
    public void Accumulate(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)
    {
        var cellLuma = new float[gridWidth * gridHeight];
        var cellCounts = new int[gridWidth * gridHeight];

        for (var y = 0; y < height; y++)
        {
            var cellY = y * gridHeight / height;
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var cellX = x * gridWidth / width;
                var pixelOffset = rowOffset + (x * 4);
                var luma = (bgra8Pixels[pixelOffset] + bgra8Pixels[pixelOffset + 1] + bgra8Pixels[pixelOffset + 2]) / 3f;

                var cellIndex = (cellY * gridWidth) + cellX;
                cellLuma[cellIndex] += luma;
                cellCounts[cellIndex]++;
            }
        }

        for (var i = 0; i < cellLuma.Length; i++)
        {
            if (cellCounts[i] > 0)
            {
                cellLuma[i] /= cellCounts[i];
            }
        }

        if (_hasPrevious)
        {
            for (var i = 0; i < cellLuma.Length; i++)
            {
                _cellEnergy[i] += MathF.Abs(cellLuma[i] - _previousCellLuma[i]);
            }

            FramesAccumulated++;
        }

        cellLuma.CopyTo(_previousCellLuma, 0);
        _hasPrevious = true;
    }

    /// <summary>
    /// Returns the bounding rectangle of the longest contiguous run of consistently-moving columns crossed with
    /// the longest contiguous run of consistently-moving rows, once at least <paramref name="minimumFrames"/>
    /// frame-to-frame samples have been folded in via <see cref="Accumulate"/>. False before that, or if
    /// nothing in the frame moved enough to stand out from background noise.
    /// </summary>
    public bool TryGetRegion(out NormalizedRect region)
    {
        region = default;
        if (FramesAccumulated < minimumFrames)
        {
            return false;
        }

        var columnEnergy = new float[gridWidth];
        var rowEnergy = new float[gridHeight];
        for (var y = 0; y < gridHeight; y++)
        {
            for (var x = 0; x < gridWidth; x++)
            {
                var energy = _cellEnergy[(y * gridWidth) + x];
                columnEnergy[x] += energy;
                rowEnergy[y] += energy;
            }
        }

        if (!TryLongestActiveRun(columnEnergy, out var minX, out var maxX) ||
            !TryLongestActiveRun(rowEnergy, out var minY, out var maxY))
        {
            return false;
        }

        region = new NormalizedRect(
            X: (double)minX / gridWidth,
            Y: (double)minY / gridHeight,
            Width: (double)(maxX - minX + 1) / gridWidth,
            Height: (double)(maxY - minY + 1) / gridHeight);
        return true;
    }

    /// <summary>
    /// Thresholds <paramref name="axisEnergy"/> against its own busiest entry, then returns the start/end
    /// indices of the longest contiguous run of entries at or above that threshold - so one small, unrelated
    /// active stretch elsewhere on the axis (a distant animated page element) loses to the game region's own
    /// run rather than expanding the reported bounds to cover both.
    /// </summary>
    private static bool TryLongestActiveRun(float[] axisEnergy, out int start, out int end)
    {
        start = end = -1;

        var max = 0f;
        foreach (var energy in axisEnergy)
        {
            if (energy > max)
            {
                max = energy;
            }
        }

        if (max <= 0f)
        {
            return false;
        }

        var threshold = max * ActivityThresholdRatio;
        var bestLength = 0;
        var runStart = -1;
        for (var i = 0; i < axisEnergy.Length; i++)
        {
            if (axisEnergy[i] >= threshold)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }

                var length = i - runStart + 1;
                if (length > bestLength)
                {
                    bestLength = length;
                    start = runStart;
                    end = i;
                }
            }
            else
            {
                runStart = -1;
            }
        }

        return bestLength > 0;
    }
}
