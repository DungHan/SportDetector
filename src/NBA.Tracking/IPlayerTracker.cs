using NBA.Vision;

namespace NBA.Tracking;

/// <summary>
/// Associates a frame's player detections with persistent tracks, one per physical player, so identity survives
/// across frames (including brief occlusion) - see tracking/player-tracking spec. Called exactly once per
/// processed frame; each call advances every track's motion model by one frame-step (not wall-clock time).
/// </summary>
public interface IPlayerTracker
{
    /// <summary>Associates <paramref name="detections"/> with existing tracks (or creates/terminates tracks as needed) and returns every currently-live track's latest box.</summary>
    IReadOnlyList<TrackedPlayer> Update(IReadOnlyList<PlayerDetection> detections);

    /// <summary>Terminates every live track and forgets all state - used when switching to an unrelated capture source, so track IDs don't carry over to a scene the tracker never saw.</summary>
    void Reset();
}
