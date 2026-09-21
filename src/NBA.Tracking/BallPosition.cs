namespace NBA.Tracking;

/// <summary>
/// The ball's current smoothed/coasted image-space axis-aligned bounding box (pixels) and confidence, as
/// reported by <see cref="IBallTracker"/>. Deliberately not <c>OnCourtObjectDetection</c> (which carries a
/// <c>ClassName</c> the ball tracker's own consumer already knows is always "Ball") or <c>TrackedPlayer</c>
/// (which carries a <c>TrackId</c>/<c>Color</c> the ball has neither of - see tracking/ball-tracking spec).
/// </summary>
public readonly record struct BallPosition(double Left, double Top, double Right, double Bottom, float Confidence);
