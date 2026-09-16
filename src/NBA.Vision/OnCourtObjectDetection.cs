namespace NBA.Vision;

/// <summary>
/// One detected non-<c>Player</c> on-court/broadcast-overlay object's image-space axis-aligned bounding box
/// (pixels), confidence score, and class name (e.g. <c>"Ball"</c>, <c>"Hoop"</c>, <c>"Shot Clock"</c>) - see
/// <c>vision/on-court-object-detection</c>. Unlike <see cref="PlayerDetection"/>, carries no color-sampling
/// signal - only <c>tracking/player-tracking</c>'s team-color gating needs that.
/// </summary>
public readonly record struct OnCourtObjectDetection(
    double Left,
    double Top,
    double Right,
    double Bottom,
    float Confidence,
    string ClassName);
