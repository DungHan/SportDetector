namespace NBA.Vision;

/// <summary>A source's evaluated sport - either from the automatic classifier or a manual override, cached in its <see cref="SourceProfile"/>.</summary>
public sealed record SportClassification(
    SportType Sport,
    float Confidence,
    SportClassificationStatus Status,
    bool IsManualOverride);
