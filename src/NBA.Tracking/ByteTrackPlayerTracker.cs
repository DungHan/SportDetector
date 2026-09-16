using NBA.Vision;

namespace NBA.Tracking;

/// <summary>
/// Associates per-frame <see cref="PlayerDetection"/>s into persistent tracks using ByteTrack's core two-round
/// idea (tracking/player-tracking spec): high-confidence detections are matched against every track's
/// motion-predicted box first, then remaining low-confidence detections are matched only against tracks still
/// unmatched after that first round - recovering tracks through brief occlusion/motion blur instead of dropping
/// them, unlike SORT-family trackers that discard low-confidence boxes outright. Low-confidence detections never
/// spawn new tracks. Unmatched tracks are kept alive (at their motion-predicted position) for up to
/// <paramref name="maxLostFrames"/> consecutive <see cref="Update"/> calls (i.e. detection attempts, not raw
/// captured frames - see <see cref="PredictOnly"/>) before being terminated; terminated track IDs are never
/// reused. A track coasting on pure motion prediction for more than <paramref name="maxVisibleLostFrames"/>
/// consecutive unmatched <see cref="Update"/> calls is withheld from both methods' return value (though it stays
/// alive internally, and can still be re-matched and become visible again, until <paramref name="maxLostFrames"/>) -
/// past a couple of misses the constant-velocity extrapolation is more likely to be drifting away from the real
/// player than tracking them, so it's better to show nothing than a box flying off on stale velocity. There is
/// no missing-model degraded path here (unlike <c>IPlayerDetector</c>/<c>ICourtKeypointDetector</c>) -
/// this is a pure algorithm over already-in-memory boxes, so this is the only <see cref="IPlayerTracker"/> implementation.
/// Association also gains a team-color consistency veto on top of IoU (add-team-color-track-gating design.md):
/// each real detection frame, this frame's detection colors are split into two groups by
/// <see cref="TwoMeansColorClusterer"/> (a per-frame stand-in for "the two teams' jersey colors"), and a
/// candidate (track, detection) pair is only allowed to match if both are nearest the same one of this frame's
/// two centroids - a color disagreement vetoes an otherwise IoU-eligible pair. The veto self-disables (falls
/// back to IoU-only, unchanged from before this decision) whenever the color signal isn't informative this
/// frame: fewer than two detections carry a usable color, or the two centroids are too close together
/// (<paramref name="minCentroidSeparation"/>) to represent a real two-color split. Each track keeps its own
/// running color estimate (<paramref name="colorEmaAlpha"/>-smoothed), seeded when the track is created and
/// updated on every successful match - it tracks "what does this specific player tend to look like," not which
/// team the player is on; the frame-level 2-means step exists only to produce this frame's two comparison
/// points, not to permanently label any track.
/// </summary>
public sealed class ByteTrackPlayerTracker(
    float highConfidenceThreshold = 0.6f,
    double highConfidenceIouThreshold = 0.3,
    double lowConfidenceIouThreshold = 0.3,
    int maxLostFrames = 20,
    int maxVisibleLostFrames = 5,
    double minCentroidSeparation = 30.0,
    double colorEmaAlpha = 0.3) : IPlayerTracker
{
    private readonly List<Track> _tracks = [];
    private int _nextTrackId = 1;

    public IReadOnlyList<TrackedPlayer> Update(IReadOnlyList<PlayerDetection> detections)
    {
        var originalTrackCount = _tracks.Count;

        // Advance every existing track's motion model by one frame-step before any association. Default to
        // reporting the predicted box; ApplyMatch overwrites LastBox for whichever tracks get matched below.
        AdvancePredictions();

        var highDetectionIndices = new List<int>();
        var lowDetectionIndices = new List<int>();
        for (var i = 0; i < detections.Count; i++)
        {
            (detections[i].Confidence >= highConfidenceThreshold ? highDetectionIndices : lowDetectionIndices).Add(i);
        }

        // This frame's team-color veto signal (design.md's "per-frame team-color grouping" decision): computed
        // once per Update call - not per round - over every detection carrying a usable color, regardless of
        // confidence tier, and reused by both rounds' isEligible closures below. Self-disables (colorCentroids
        // stays null) whenever the signal isn't informative this frame: fewer than two colored detections, or
        // the two centroids land too close together to represent a real two-color split.
        var coloredDetections = detections.Where(d => d.Color.HasValue).Select(d => d.Color!.Value).ToList();
        ((double R, double G, double B) A, (double R, double G, double B) B)? colorCentroids = null;
        if (coloredDetections.Count >= 2)
        {
            var candidate = TwoMeansColorClusterer.Cluster(coloredDetections);
            if (TwoMeansColorClusterer.SquaredDistance(candidate.CentroidA, candidate.CentroidB) >= minCentroidSeparation * minCentroidSeparation)
            {
                colorCentroids = (candidate.CentroidA, candidate.CentroidB);
            }
        }

        var matchedTrackIndices = new HashSet<int>();

        // Round 1: every track's predicted box vs high-confidence detections.
        var predictedBoxes = _tracks.Select(t => t.PredictedBox).ToList();
        var highBoxes = highDetectionIndices.Select(i => ToBox(detections[i])).ToList();
        var round1 = GreedyIouMatcher.Match(
            predictedBoxes,
            highBoxes,
            highConfidenceIouThreshold,
            isEligible: (predictedIndex, detectionPosition) =>
                ColorsAgree(_tracks[predictedIndex].Color, detections[highDetectionIndices[detectionPosition]].Color, colorCentroids));

        foreach (var (predictedIndex, detectionPosition) in round1.Matches)
        {
            var detection = detections[highDetectionIndices[detectionPosition]];
            ApplyMatch(_tracks[predictedIndex], detection);
            matchedTrackIndices.Add(predictedIndex);
        }

        // Round 2: tracks still unmatched after round 1 vs low-confidence detections only.
        var unmatchedTrackIndices = round1.UnmatchedPredicted;
        var unmatchedTrackBoxes = unmatchedTrackIndices.Select(i => _tracks[i].PredictedBox).ToList();
        var lowBoxes = lowDetectionIndices.Select(i => ToBox(detections[i])).ToList();
        var round2 = GreedyIouMatcher.Match(
            unmatchedTrackBoxes,
            lowBoxes,
            lowConfidenceIouThreshold,
            isEligible: (predictedIndex, detectionPosition) =>
                ColorsAgree(_tracks[unmatchedTrackIndices[predictedIndex]].Color, detections[lowDetectionIndices[detectionPosition]].Color, colorCentroids));

        foreach (var (predictedIndex, detectionPosition) in round2.Matches)
        {
            var trackIndex = unmatchedTrackIndices[predictedIndex];
            var detection = detections[lowDetectionIndices[detectionPosition]];
            ApplyMatch(_tracks[trackIndex], detection);
            matchedTrackIndices.Add(trackIndex);
        }

        // High-confidence detections still unmatched after round 1 spawn new tracks (low-confidence detections
        // never do - see the type-level doc comment). Seeded with the spawning detection's color, if any - the
        // veto above never applies to a spawn itself, only to matching against already-live tracks.
        foreach (var detectionPosition in round1.UnmatchedDetections)
        {
            var detection = detections[highDetectionIndices[detectionPosition]];
            var box = ToBox(detection);
            var track = new Track
            {
                Id = _nextTrackId++,
                PredictedBox = box,
                LastBox = box,
                LastConfidence = detection.Confidence,
                Color = detection.Color.HasValue
                    ? ((double)detection.Color.Value.R, (double)detection.Color.Value.G, (double)detection.Color.Value.B)
                    : null,
            };
            track.Predictor.Correct(box);
            _tracks.Add(track);
        }

        // Age out tracks unmatched in both rounds; terminate any that exceed the occlusion buffer. Only the
        // tracks that existed before this frame's new-track spawns are eligible to be aged (a track just
        // created above starts at LostFrames = 0).
        for (var i = 0; i < originalTrackCount; i++)
        {
            if (!matchedTrackIndices.Contains(i))
            {
                _tracks[i].LostFrames++;
            }
        }

        _tracks.RemoveAll(t => t.LostFrames >= maxLostFrames);

        return ToVisiblePlayers();
    }

    /// <summary>
    /// Advances every live track's motion model by one frame-step and reports the resulting boxes, without
    /// running detection association, without aging <c>LostFrames</c>, and without spawning or terminating
    /// tracks - the counterpart to <see cref="Update"/> for a frame on which detection was not attempted (see
    /// tracking/player-tracking spec's "Advance motion prediction without detection").
    /// </summary>
    public IReadOnlyList<TrackedPlayer> PredictOnly()
    {
        AdvancePredictions();

        return ToVisiblePlayers();
    }

    private void AdvancePredictions()
    {
        foreach (var track in _tracks)
        {
            track.PredictedBox = track.Predictor.Predict();
            track.LastBox = track.PredictedBox;
        }
    }

    /// <summary>Every live track, excluding those coasting on pure motion prediction past <paramref name="maxVisibleLostFrames"/> - see the type-level doc comment.</summary>
    private IReadOnlyList<TrackedPlayer> ToVisiblePlayers() => _tracks
        .Where(t => t.LostFrames <= maxVisibleLostFrames)
        .Select(t => new TrackedPlayer(t.Id, t.LastBox.Left, t.LastBox.Top, t.LastBox.Right, t.LastBox.Bottom, t.LastConfidence))
        .ToList();

    public void Reset() => _tracks.Clear();

    private void ApplyMatch(Track track, PlayerDetection detection)
    {
        var box = ToBox(detection);
        track.Predictor.Correct(box);
        track.LastBox = box;
        track.LastConfidence = detection.Confidence;
        track.LostFrames = 0;

        if (detection.Color is (byte R, byte G, byte B) color)
        {
            var sample = ((double)color.R, (double)color.G, (double)color.B);
            var existing = track.Color;
            track.Color = existing.HasValue
                ? (
                    (colorEmaAlpha * sample.Item1) + ((1 - colorEmaAlpha) * existing.Value.R),
                    (colorEmaAlpha * sample.Item2) + ((1 - colorEmaAlpha) * existing.Value.G),
                    (colorEmaAlpha * sample.Item3) + ((1 - colorEmaAlpha) * existing.Value.B))
                : sample;
        }
    }

    /// <summary>
    /// "Nearest-centroid identity," never a raw cluster index (design.md's decision - 2-means' group
    /// numbering is arbitrary and can flip between frames): eligible when either side has no color opinion yet
    /// (a track that's never matched a colored detection, or a detection whose crop was degenerate this frame -
    /// nothing to disagree about), when the veto is inactive this frame (<paramref name="centroids"/> is
    /// <c>null</c> - not enough signal, per the type-level doc comment), or when both colors are nearest the
    /// same one of this frame's two centroids. Ineligible only when both sides have an opinion and they land
    /// nearest different centroids.
    /// </summary>
    private static bool ColorsAgree(
        (double R, double G, double B)? trackColor,
        (byte R, byte G, byte B)? detectionColor,
        ((double R, double G, double B) A, (double R, double G, double B) B)? centroids)
    {
        if (!centroids.HasValue || !trackColor.HasValue || !detectionColor.HasValue)
        {
            return true;
        }

        var (centroidA, centroidB) = centroids.Value;
        var track = trackColor.Value;
        var detectionByte = detectionColor.Value;
        var detection = ((double)detectionByte.R, (double)detectionByte.G, (double)detectionByte.B);

        var trackNearestA = TwoMeansColorClusterer.SquaredDistance(track, centroidA) <= TwoMeansColorClusterer.SquaredDistance(track, centroidB);
        var detectionNearestA = TwoMeansColorClusterer.SquaredDistance(detection, centroidA) <= TwoMeansColorClusterer.SquaredDistance(detection, centroidB);

        return trackNearestA == detectionNearestA;
    }

    private static (double Left, double Top, double Right, double Bottom) ToBox(PlayerDetection detection) =>
        (detection.Left, detection.Top, detection.Right, detection.Bottom);

    private sealed class Track
    {
        public required int Id { get; init; }

        public BoxMotionPredictor Predictor { get; } = new();

        /// <summary>This frame-step's motion-predicted box (computed once per <see cref="Update"/> call, before association).</summary>
        public required (double Left, double Top, double Right, double Bottom) PredictedBox { get; set; }

        /// <summary>The box reported for this track this frame: the matched detection's box, or - while unmatched but still within the occlusion buffer - the motion-predicted box.</summary>
        public required (double Left, double Top, double Right, double Bottom) LastBox { get; set; }

        public required float LastConfidence { get; set; }

        public int LostFrames { get; set; }

        /// <summary>Running EMA estimate of this track's own appearance color - seeded at spawn, updated on every successful match. Not which team the track is on (see the type-level doc comment).</summary>
        public (double R, double G, double B)? Color { get; set; }
    }
}
