namespace NBA.Vision;

/// <summary>
/// Both outputs of one <see cref="IMultiClassObjectDetector.Detect"/> call: <see cref="Players"/> (the
/// `Player`-class detections, identical in shape/behavior to <see cref="IPlayerDetector"/>'s output) and
/// <see cref="Others"/> (every other class detected in that same inference pass). Bundling both in one result
/// is what lets one model inference serve both consumers - see design.md's "A new IMultiClassObjectDetector
/// interface... supersedes IPlayerDetector at the MainWindowViewModel wiring layer" decision.
/// </summary>
public sealed record MultiClassDetectionResult(
    IReadOnlyList<PlayerDetection> Players,
    IReadOnlyList<OnCourtObjectDetection> Others);
