namespace NBA.Tracking;

/// <summary>
/// One tracked player's image-space axis-aligned bounding box (pixels), stable track ID, and confidence score.
/// </summary>
/// <param name="FramesSinceMatch">
/// Consecutive <see cref="IPlayerTracker.Update"/> calls since this track last matched a real detection - 0
/// means this frame's box came from an actual detection; a positive value means it's coasting on motion
/// prediction alone (still within <see cref="ByteTrackPlayerTracker"/>'s occlusion buffer). Lets a consumer
/// that needs to cap how many players it shows at once (e.g. the minimap - basketball has at most 10 players
/// on court, so more than that live at once means some are stale/duplicate tracks, not real extra people)
/// prefer real-this-frame matches over ones merely coasting.
/// </param>
/// <param name="Color">
/// This track's running EMA-smoothed jersey/upper-body color estimate (rounded to whole bytes for display),
/// or null if it has never matched a detection with a usable sampled color. Exposed for display (e.g. the
/// minimap's per-player marker fill) - <see cref="ByteTrackPlayerTracker"/> already used this same estimate
/// internally for its team-color match veto before this field existed.
/// </param>
public readonly record struct TrackedPlayer(
    int TrackId, double Left, double Top, double Right, double Bottom, float Confidence,
    int FramesSinceMatch = 0, (byte R, byte G, byte B)? Color = null);
