namespace NBA.Vision;

public enum SportClassificationStatus
{
    /// <summary>Confidently classified as a sport with a registered <see cref="CourtGeometryDefinition"/>.</summary>
    Confident,

    /// <summary>Confidence was below the acceptance threshold - no sport committed to.</summary>
    Unknown,

    /// <summary>Confidently classified, but as a sport with no registered geometry (e.g. soccer, tennis in this change).</summary>
    RecognizedButUnsupported,
}
