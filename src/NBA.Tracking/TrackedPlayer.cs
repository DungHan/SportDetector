namespace NBA.Tracking;

/// <summary>One tracked player's image-space axis-aligned bounding box (pixels), stable track ID, and confidence score.</summary>
public readonly record struct TrackedPlayer(int TrackId, double Left, double Top, double Right, double Bottom, float Confidence);
