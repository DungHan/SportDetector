namespace NBA.Vision;

/// <summary>
/// Degraded-path classifier used when no trained sport-classifier model file is available yet (see design.md's
/// "No labeled training data or trained weights exist yet for the sport classifier" risk). Always reports
/// <see cref="SportType.Unknown"/> at zero confidence, which <see cref="SportClassificationCoordinator"/>
/// then surfaces as "unknown" rather than a wrong guess - the manual override path remains available.
/// </summary>
public sealed class NullSportClassifier : ISportClassifier
{
    public SportClassifierOutput Classify(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) =>
        new(SportType.Unknown, 0f);
}
