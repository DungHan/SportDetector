namespace NBA.Vision;

/// <summary>
/// One named, real-world court landmark, in court-space meters from the geometry's fixed origin.
/// <paramref name="KeypointIndex"/>, when set, is the position of this landmark in the court-keypoint ONNX
/// model's fixed-order output (see <see cref="OnnxCourtKeypointDetector"/>) - null means the landmark exists
/// only for manual calibration (a human can click it, but no model predicts it).
/// </summary>
public sealed record CourtLandmark(string Name, double X, double Y, int? KeypointIndex = null);

/// <summary>
/// A sport's playing-surface geometry: its named landmarks (for calibration) and real-world dimensions (for
/// rendering the minimap diagram). See the court-calibration spec's "Playing-surface geometry is selected by
/// sport" requirement - <see cref="CourtGeometryRegistry"/> is the sport-keyed registry of these.
/// </summary>
public sealed record CourtGeometryDefinition(
    SportType Sport,
    string DisplayName,
    IReadOnlyList<CourtLandmark> Landmarks,
    double SurfaceWidthMeters,
    double SurfaceLengthMeters)
{
    public CourtLandmark? FindLandmark(string name) => Landmarks.FirstOrDefault(l => l.Name == name);
}
