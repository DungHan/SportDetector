namespace NBA.Vision;

/// <summary>
/// Per-source "things we figured out and don't want to redo every time we see this source again" - keyed by
/// an opaque, caller-supplied stable identity string (NBA.App derives it from a capture source's process
/// name/title/resolution; NBA.Vision doesn't need to know how). Deliberately generic, not calibration-specific
/// - see design.md's "SourceProfile concept is deliberately generic". Holds the sport classification and the
/// calibration homography today; a future change (score/state ROI) is expected to add another named entry
/// here rather than inventing a second persistence mechanism.
/// </summary>
public sealed class SourceProfile
{
    public required string SourceKey { get; init; }

    public SportClassification? Sport { get; set; }

    public CalibrationData? Calibration { get; set; }
}
