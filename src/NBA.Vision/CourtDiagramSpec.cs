namespace NBA.Vision;

/// <summary>
/// A sport's "impression" color scheme for rendering its court/field diagram - surface, line, and (for sports
/// where the broadcast look has one) surround/apron color. Hex strings are "#RRGGBB", chosen to match how the
/// sport is typically presented on TV rather than any specific venue's actual colors.
/// </summary>
public sealed record CourtColorScheme(string SurfaceHex, string LineHex, string? SurroundHex = null);

/// <summary>One drawable court marking, in court-space meters from the diagram's origin (top-left).</summary>
public abstract record CourtMarking;

public sealed record LineMarking(double X1, double Y1, double X2, double Y2, double ThicknessMeters = 0.05) : CourtMarking;

public sealed record CircleMarking(double CenterX, double CenterY, double RadiusMeters) : CourtMarking;

/// <summary>An arc/partial ellipse. Angles are degrees, measured like <c>Cv2.Ellipse</c> (0 = +X axis, clockwise).</summary>
public sealed record ArcMarking(double CenterX, double CenterY, double RadiusMeters, double StartAngleDeg, double EndAngleDeg) : CourtMarking;

/// <summary>
/// A rectangle marking. When <paramref name="Filled"/> is set, it's filled with <paramref name="FillHex"/>
/// (falling back to the diagram's line color if unset) instead of just outlined - used for zones that need
/// their own block color, like an American football end zone.
/// </summary>
public sealed record RectMarking(double X, double Y, double Width, double Height, bool Filled = false, string? FillHex = null) : CourtMarking;

/// <summary>A label (e.g. a yard number) centered at a court-space point, sized to a target cap-height in meters.</summary>
public sealed record TextMarking(double CenterX, double CenterY, string Text, double HeightMeters) : CourtMarking;

/// <summary>
/// Purely visual court/field description for <see cref="CourtDiagramRenderer"/> - independent of
/// <see cref="CourtGeometryDefinition"/>, which is calibration-landmark data and only exists for sports that
/// support homography calibration. This lets every classifiable sport get a diagram even if it can't (yet) be
/// calibrated. See <see cref="CourtDiagramRegistry"/> for the sport-keyed set of these.
/// </summary>
public sealed record CourtDiagramSpec(
    SportType Sport,
    string DisplayName,
    double SurfaceWidthMeters,
    double SurfaceLengthMeters,
    CourtColorScheme Colors,
    IReadOnlyList<CourtMarking> Markings);
