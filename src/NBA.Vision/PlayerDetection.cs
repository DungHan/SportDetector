namespace NBA.Vision;

/// <summary>One detected player's image-space axis-aligned bounding box (pixels) and confidence score.</summary>
public readonly record struct PlayerDetection(double Left, double Top, double Right, double Bottom, float Confidence);
