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
/// reused. There is no missing-model degraded path here (unlike <c>IPlayerDetector</c>/<c>ICourtKeypointDetector</c>) -
/// this is a pure algorithm over already-in-memory boxes, so this is the only <see cref="IPlayerTracker"/> implementation.
/// </summary>
public sealed class ByteTrackPlayerTracker(
    float highConfidenceThreshold = 0.6f,
    double highConfidenceIouThreshold = 0.3,
    double lowConfidenceIouThreshold = 0.3,
    int maxLostFrames = 30) : IPlayerTracker
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

        var matchedTrackIndices = new HashSet<int>();

        // Round 1: every track's predicted box vs high-confidence detections.
        var predictedBoxes = _tracks.Select(t => t.PredictedBox).ToList();
        var highBoxes = highDetectionIndices.Select(i => ToBox(detections[i])).ToList();
        var round1 = GreedyIouMatcher.Match(predictedBoxes, highBoxes, highConfidenceIouThreshold);

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
        var round2 = GreedyIouMatcher.Match(unmatchedTrackBoxes, lowBoxes, lowConfidenceIouThreshold);

        foreach (var (predictedIndex, detectionPosition) in round2.Matches)
        {
            var trackIndex = unmatchedTrackIndices[predictedIndex];
            var detection = detections[lowDetectionIndices[detectionPosition]];
            ApplyMatch(_tracks[trackIndex], detection);
            matchedTrackIndices.Add(trackIndex);
        }

        // High-confidence detections still unmatched after round 1 spawn new tracks (low-confidence detections
        // never do - see the type-level doc comment).
        foreach (var detectionPosition in round1.UnmatchedDetections)
        {
            var detection = detections[highDetectionIndices[detectionPosition]];
            var box = ToBox(detection);
            var track = new Track { Id = _nextTrackId++, PredictedBox = box, LastBox = box, LastConfidence = detection.Confidence };
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

        return _tracks
            .Select(t => new TrackedPlayer(t.Id, t.LastBox.Left, t.LastBox.Top, t.LastBox.Right, t.LastBox.Bottom, t.LastConfidence))
            .ToList();
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

        return _tracks
            .Select(t => new TrackedPlayer(t.Id, t.LastBox.Left, t.LastBox.Top, t.LastBox.Right, t.LastBox.Bottom, t.LastConfidence))
            .ToList();
    }

    private void AdvancePredictions()
    {
        foreach (var track in _tracks)
        {
            track.PredictedBox = track.Predictor.Predict();
            track.LastBox = track.PredictedBox;
        }
    }

    public void Reset() => _tracks.Clear();

    private static void ApplyMatch(Track track, PlayerDetection detection)
    {
        var box = ToBox(detection);
        track.Predictor.Correct(box);
        track.LastBox = box;
        track.LastConfidence = detection.Confidence;
        track.LostFrames = 0;
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
    }
}
