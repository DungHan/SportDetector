namespace NBA.Vision;

/// <summary>A rectangle in 0..1 frame-relative coordinates, independent of the frame's actual pixel resolution.</summary>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Default scoreboard search region used when a source has no explicit <see cref="SourceProfile.ScoreboardRegion"/>
    /// override - broadcast scoreboards are most commonly overlaid along the bottom edge of the frame.
    /// </summary>
    public static NormalizedRect DefaultScoreboardRegion { get; } = new(X: 0, Y: 0.85, Width: 1.0, Height: 0.15);
}
