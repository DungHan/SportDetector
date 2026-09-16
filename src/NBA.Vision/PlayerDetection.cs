namespace NBA.Vision;

/// <summary>
/// One detected player's image-space axis-aligned bounding box (pixels), confidence score, and approximate
/// upper-body mean color (<see cref="Color"/>, <c>null</c> when the box was too small/degenerate to sample -
/// see <see cref="UpperBodyColorSampling"/>). <see cref="Color"/> is an internal appearance signal for
/// <c>tracking/player-tracking</c>'s team-color veto, not a rendered/displayed value.
/// </summary>
public readonly record struct PlayerDetection(
    double Left,
    double Top,
    double Right,
    double Bottom,
    float Confidence,
    (byte R, byte G, byte B)? Color = null);
