namespace NBA.Vision;

/// <summary>
/// Auto-detects which normalized sub-rectangle of a captured frame is actually "the game" versus surrounding
/// chrome (YouTube page, browser UI, ...), by accumulating per-cell brightness-difference energy over many
/// frames and returning the largest contiguous region that keeps moving. This is deliberately not a two-frame
/// diff: a single-frame flicker, or an isolated UI element that moves on its own (an autoplay thumbnail, a
/// blinking icon), doesn't survive averaging over <see cref="_minimumFrames"/> samples or the
/// connected-component step, whereas the game region does because it moves on nearly every frame.
/// </summary>
public sealed class PlaybackRegionDetector(int gridWidth = 32, int gridHeight = 18, int minimumFrames = 45)
{
    /// <summary>Cells whose accumulated energy is below this fraction of the busiest cell's energy are treated as background noise.</summary>
    public const float ActivityThresholdRatio = 0.15f;

    /// <summary>
    /// Grid-cell radius used only to decide connectivity for the largest-component search, not the final
    /// rectangle's bounds - bridges an on-court cell that happens to be quiet this sampling window (e.g. a
    /// player who barely moved) so it doesn't fracture the game region into disconnected pieces that then lose
    /// to a larger unrelated component. Small enough that a genuinely separate motion source elsewhere in the
    /// frame (an autoplay thumbnail, a blinking icon) still isn't bridged in.
    /// </summary>
    private const int ConnectivityBridgeRadius = 2;

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
    /// Returns the bounding rectangle of the largest contiguous region of consistently-moving cells, once at
    /// least <paramref name="minimumFrames"/> frame-to-frame samples have been folded in via <see cref="Accumulate"/>.
    /// False before that, or if nothing in the frame moved enough to stand out from background noise.
    /// </summary>
    public bool TryGetRegion(out NormalizedRect region)
    {
        region = default;
        if (FramesAccumulated < minimumFrames)
        {
            return false;
        }

        var max = 0f;
        foreach (var energy in _cellEnergy)
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
        var active = new bool[gridWidth * gridHeight];
        for (var i = 0; i < _cellEnergy.Length; i++)
        {
            active[i] = _cellEnergy[i] >= threshold;
        }

        if (!TryFindLargestComponent(active, out var minX, out var minY, out var maxX, out var maxY))
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

    private bool TryFindLargestComponent(bool[] active, out int minX, out int minY, out int maxX, out int maxY)
    {
        var connectivity = Dilate(active);
        var visited = new bool[connectivity.Length];
        var queue = new Queue<int>();
        var bestSize = 0;
        minX = minY = maxX = maxY = 0;
        var bestFound = false;

        for (var start = 0; start < connectivity.Length; start++)
        {
            if (!connectivity[start] || visited[start])
            {
                continue;
            }

            queue.Enqueue(start);
            visited[start] = true;
            var size = 0;
            var componentMinX = gridWidth;
            var componentMinY = gridHeight;
            var componentMaxX = -1;
            var componentMaxY = -1;

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var cellX = index % gridWidth;
                var cellY = index / gridWidth;

                // The rectangle's bounds are measured from the original, undilated activity only - the
                // dilated mask above exists solely to decide connectivity, so a bridged-but-quiet cell widens
                // the component without itself stretching the reported bounds.
                if (active[index])
                {
                    size++;
                    componentMinX = Math.Min(componentMinX, cellX);
                    componentMaxX = Math.Max(componentMaxX, cellX);
                    componentMinY = Math.Min(componentMinY, cellY);
                    componentMaxY = Math.Max(componentMaxY, cellY);
                }

                TryEnqueueNeighbor(cellX - 1, cellY, connectivity, visited, queue);
                TryEnqueueNeighbor(cellX + 1, cellY, connectivity, visited, queue);
                TryEnqueueNeighbor(cellX, cellY - 1, connectivity, visited, queue);
                TryEnqueueNeighbor(cellX, cellY + 1, connectivity, visited, queue);
            }

            if (size > bestSize)
            {
                bestSize = size;
                minX = componentMinX;
                minY = componentMinY;
                maxX = componentMaxX;
                maxY = componentMaxY;
                bestFound = true;
            }
        }

        return bestFound;
    }

    private bool[] Dilate(bool[] active)
    {
        var dilated = new bool[active.Length];
        for (var y = 0; y < gridHeight; y++)
        {
            for (var x = 0; x < gridWidth; x++)
            {
                var index = (y * gridWidth) + x;
                if (active[index])
                {
                    dilated[index] = true;
                    continue;
                }

                for (var dy = -ConnectivityBridgeRadius; dy <= ConnectivityBridgeRadius && !dilated[index]; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= gridHeight)
                    {
                        continue;
                    }

                    for (var dx = -ConnectivityBridgeRadius; dx <= ConnectivityBridgeRadius; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= gridWidth)
                        {
                            continue;
                        }

                        if (active[(ny * gridWidth) + nx])
                        {
                            dilated[index] = true;
                            break;
                        }
                    }
                }
            }
        }

        return dilated;
    }

    private void TryEnqueueNeighbor(int x, int y, bool[] connectivity, bool[] visited, Queue<int> queue)
    {
        if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
        {
            return;
        }

        var index = (y * gridWidth) + x;
        if (!connectivity[index] || visited[index])
        {
            return;
        }

        visited[index] = true;
        queue.Enqueue(index);
    }
}
