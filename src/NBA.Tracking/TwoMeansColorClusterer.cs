namespace NBA.Tracking;

/// <summary>
/// Splits a set of colors into two groups by simple 2-means (Lloyd's algorithm, fixed iteration count, plain
/// Euclidean distance over R/G/B) - a per-frame stand-in for "the two teams' jersey colors"
/// (add-team-color-track-gating design.md's "per-frame team-color grouping via a simple 2-means, not a
/// persistent/global color model" decision). Not a general k-means implementation - fixed at two clusters,
/// since that is the only shape <see cref="ByteTrackPlayerTracker"/> needs. Deterministic (no RNG dependency,
/// matching every other algorithm in this codebase): seeded from the input's first color and whichever color
/// is farthest from it, not randomly.
/// </summary>
public static class TwoMeansColorClusterer
{
    private const int MaxIterations = 10;

    /// <summary>
    /// Runs 2-means over <paramref name="colors"/>, returning the two resulting centroids. Requires at least
    /// two colors - callers are responsible for skipping this call entirely (design.md's degenerate-frame
    /// safeguard) when fewer than two colors are available this frame.
    /// </summary>
    public static ((double R, double G, double B) CentroidA, (double R, double G, double B) CentroidB) Cluster(
        IReadOnlyList<(byte R, byte G, byte B)> colors)
    {
        if (colors.Count < 2)
        {
            throw new ArgumentException("2-means clustering needs at least two colors.", nameof(colors));
        }

        var points = colors.Select(c => ((double)c.R, (double)c.G, (double)c.B)).ToList();

        var seedA = points[0];
        var seedB = points.OrderByDescending(p => SquaredDistance(p, seedA)).First();

        var centroidA = seedA;
        var centroidB = seedB;

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var groupA = new List<(double R, double G, double B)>();
            var groupB = new List<(double R, double G, double B)>();

            foreach (var point in points)
            {
                if (SquaredDistance(point, centroidA) <= SquaredDistance(point, centroidB))
                {
                    groupA.Add(point);
                }
                else
                {
                    groupB.Add(point);
                }
            }

            var nextA = groupA.Count > 0 ? Mean(groupA) : centroidA;
            var nextB = groupB.Count > 0 ? Mean(groupB) : centroidB;

            if (nextA == centroidA && nextB == centroidB)
            {
                break;
            }

            centroidA = nextA;
            centroidB = nextB;
        }

        return (centroidA, centroidB);
    }

    /// <summary>Plain squared Euclidean distance over R/G/B - exposed so callers can compare a single color/centroid against these centroids using the same metric this clustering used, without recomputing a square root neither side needs.</summary>
    public static double SquaredDistance((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return (dr * dr) + (dg * dg) + (db * db);
    }

    private static (double R, double G, double B) Mean(List<(double R, double G, double B)> points)
    {
        double sumR = 0;
        double sumG = 0;
        double sumB = 0;

        foreach (var point in points)
        {
            sumR += point.R;
            sumG += point.G;
            sumB += point.B;
        }

        var count = points.Count;
        return (sumR / count, sumG / count, sumB / count);
    }
}
