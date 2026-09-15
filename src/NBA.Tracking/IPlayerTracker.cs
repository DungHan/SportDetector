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
    /// <summary>Associates <paramref name="detections"/> with existing tracks (or creates/terminates tracks as needed) and returns every currently-live track's latest box. Call only for a frame on which detection actually ran.</summary>
    IReadOnlyList<TrackedPlayer> Update(IReadOnlyList<PlayerDetection> detections);

    /// <summary>Advances every live track's motion model by one frame-step and returns the resulting boxes, without association, without aging any track's occlusion-buffer count, and without spawning or terminating tracks. Call for a frame on which detection did not run (per the configured detection cadence), so tracked boxes still move between real detections instead of freezing.</summary>
    IReadOnlyList<TrackedPlayer> PredictOnly();

    /// <summary>Terminates every live track and forgets all state - used when switching to an unrelated capture source, so track IDs don't carry over to a scene the tracker never saw.</summary>
    void Reset();
}
