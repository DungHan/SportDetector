using NBA.Vision;

namespace NBA.Tracking;

/// <summary>
/// Associates a frame's player detections with persistent tracks, one per physical player, so identity survives
/// across frames (including brief occlusion) - see tracking/player-tracking spec. Exactly one of
/// <see cref="Update"/> or <see cref="PredictOnly"/> is called once per processed frame, depending on whether
/// detection ran for that frame (see vision/player-detection's detection-cadence requirement); each call
/// advances every track's motion model by one frame-step (not wall-clock time).
/// </summary>
public interface IPlayerTracker
{
    /// <summary>Associates <paramref name="detections"/> with existing tracks (or creates/terminates tracks as needed) and returns every currently-live track's latest box, except a track that has gone unmatched for too many consecutive calls - it stays alive internally (so it can still be re-matched) but is withheld from the result until it is. Call only for a frame on which detection actually ran.</summary>
    IReadOnlyList<TrackedPlayer> Update(IReadOnlyList<PlayerDetection> detections);

    /// <summary>Advances every live track's motion model by one frame-step and returns the resulting boxes (subject to the same withholding as <see cref="Update"/> for tracks unmatched too long), without association, without aging any track's occlusion-buffer count, and without spawning or terminating tracks. Call for a frame on which detection did not run (per the configured detection cadence), so tracked boxes still move between real detections instead of freezing.</summary>
    IReadOnlyList<TrackedPlayer> PredictOnly();

    /// <summary>Terminates every live track and forgets all state - used when switching to an unrelated capture source, so track IDs don't carry over to a scene the tracker never saw.</summary>
    void Reset();

    /// <summary>
    /// Every confirmed track still alive internally, including ones coasting on motion prediction past the point
    /// where <see cref="Update"/>/<see cref="PredictOnly"/> would withhold them (they only report a track through
    /// a short occlusion buffer, to protect the raw overlay from a box drifting away on stale velocity - see
    /// <c>ByteTrackPlayerTracker</c>'s type-level doc comment). A consumer that would rather keep showing a
    /// track through a longer miss - fading it out itself via <see cref="TrackedPlayer.FramesSinceMatch"/>
    /// instead of having it disappear outright (e.g. the minimap) - should read from here instead.
    /// </summary>
    IReadOnlyList<TrackedPlayer> AllConfirmedTracks { get; }
}
