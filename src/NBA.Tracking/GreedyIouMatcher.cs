namespace NBA.Tracking;

/// <summary>Result of matching predicted track boxes against detection boxes.</summary>
public sealed record GreedyMatchResult(
    IReadOnlyList<(int PredictedIndex, int DetectionIndex)> Matches,
    IReadOnlyList<int> UnmatchedPredicted,
    IReadOnlyList<int> UnmatchedDetections);

/// <summary>
/// Greedily associates predicted boxes with detection boxes by IoU - sorts every above-threshold pair by IoU
/// descending and claims them one at a time, skipping any pair where either side is already claimed. Not an
/// optimal (Hungarian/linear-assignment) solver - see design.md's "greedy, not Hungarian" decision. Same greedy
/// pattern <c>OnnxPlayerDetector.SuppressOverlapping</c> already uses for NMS.
/// </summary>
public static class GreedyIouMatcher
{
    /// <summary>
    /// <paramref name="isEligible"/>, when supplied, is checked (predicted-index, detection-index - positions
    /// within <paramref name="predictedBoxes"/>/<paramref name="detectionBoxes"/>) alongside the IoU threshold
    /// before a pair becomes a match candidate at all - an additional caller-supplied veto on top of spatial
    /// overlap (e.g. <c>ByteTrackPlayerTracker</c>'s team-color consistency check), checked once per candidate
    /// pair, not re-checked as claims happen. Defaults to <c>null</c> (every above-threshold pair eligible),
    /// so existing callers are unaffected and this matcher itself stays appearance-agnostic - see design.md's
    /// "keep GreedyIouMatcher generically reusable" decision.
    /// </summary>
    public static GreedyMatchResult Match(
        IReadOnlyList<(double Left, double Top, double Right, double Bottom)> predictedBoxes,
        IReadOnlyList<(double Left, double Top, double Right, double Bottom)> detectionBoxes,
        double iouThreshold,
        Func<int, int, bool>? isEligible = null)
    {
        var candidates = new List<(int PredictedIndex, int DetectionIndex, double Iou)>();
        for (var p = 0; p < predictedBoxes.Count; p++)
        {
            for (var d = 0; d < detectionBoxes.Count; d++)
            {
                var iou = Iou(predictedBoxes[p], detectionBoxes[d]);
                if (iou > iouThreshold && (isEligible is null || isEligible(p, d)))
                {
                    candidates.Add((p, d, iou));
                }
            }
        }

        var matchedPredicted = new HashSet<int>();
        var matchedDetections = new HashSet<int>();
        var matches = new List<(int PredictedIndex, int DetectionIndex)>();

        foreach (var candidate in candidates.OrderByDescending(c => c.Iou))
        {
            if (matchedPredicted.Contains(candidate.PredictedIndex) || matchedDetections.Contains(candidate.DetectionIndex))
            {
                continue;
            }

            matchedPredicted.Add(candidate.PredictedIndex);
            matchedDetections.Add(candidate.DetectionIndex);
            matches.Add((candidate.PredictedIndex, candidate.DetectionIndex));
        }

        var unmatchedPredicted = Enumerable.Range(0, predictedBoxes.Count).Where(i => !matchedPredicted.Contains(i)).ToList();
        var unmatchedDetections = Enumerable.Range(0, detectionBoxes.Count).Where(i => !matchedDetections.Contains(i)).ToList();

        return new GreedyMatchResult(matches, unmatchedPredicted, unmatchedDetections);
    }

    private static double Iou(
        (double Left, double Top, double Right, double Bottom) a,
        (double Left, double Top, double Right, double Bottom) b)
    {
        var intersectWidth = Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left));
        var intersectHeight = Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));
        var intersectArea = intersectWidth * intersectHeight;

        var areaA = Math.Max(0, a.Right - a.Left) * Math.Max(0, a.Bottom - a.Top);
        var areaB = Math.Max(0, b.Right - b.Left) * Math.Max(0, b.Bottom - b.Top);
        var unionArea = areaA + areaB - intersectArea;

        return unionArea <= 0 ? 0 : intersectArea / unionArea;
    }
}
