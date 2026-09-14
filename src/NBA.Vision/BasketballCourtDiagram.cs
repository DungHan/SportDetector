namespace NBA.Vision;

/// <summary>
/// Basketball (NBA court) diagram: hardwood-floor coloring plus the center line/circle, both free-throw lanes
/// and circles, and both three-point arcs. The arc is a reasonable approximation - real NBA arcs run straight
/// near each corner (clipped by the sideline) before completing the curve, same simplification noted by
/// <see cref="BasketballGeometry"/> for its calibration landmarks.
/// </summary>
internal static class BasketballCourtDiagram
{
    private const double LengthMeters = 28.6512; // 94 ft
    private const double WidthMeters = 15.24; // 50 ft
    private const double KeyWidthMeters = 4.8768; // 16 ft, the free-throw lane width
    private const double FreeThrowDistanceMeters = 5.7912; // 19 ft from baseline
    private const double FreeThrowCircleRadiusMeters = 1.8288; // 6 ft
    private const double CenterCircleRadiusMeters = 1.8288; // 6 ft
    private const double ThreePointRadiusMeters = 7.239; // 23.75 ft
    private const double BasketDistanceFromBaselineMeters = 1.6002; // 5.25 ft
    private const double CornerSidelineDistanceMeters = 0.9144; // 3 ft - how far the corner straight run sits from the sideline

    internal static readonly CourtDiagramSpec Definition = Build();

    private static CourtDiagramSpec Build()
    {
        var halfWidth = WidthMeters / 2;
        var markings = new List<CourtMarking>
        {
            new LineMarking(LengthMeters / 2, 0, LengthMeters / 2, WidthMeters),
            new CircleMarking(LengthMeters / 2, halfWidth, CenterCircleRadiusMeters),
        };

        AddKeyAndArc(markings, baselineX: 0, direction: 1);
        AddKeyAndArc(markings, baselineX: LengthMeters, direction: -1);

        return new CourtDiagramSpec(
            SportType.Basketball,
            "Basketball (NBA court)",
            SurfaceWidthMeters: WidthMeters,
            SurfaceLengthMeters: LengthMeters,
            Colors: new CourtColorScheme(SurfaceHex: "#C68642", LineHex: "#FFFFFF", SurroundHex: "#3E2723"),
            Markings: markings);
    }

    /// <summary>
    /// Draws one end's free-throw lane, free-throw circle, and three-point arc. <paramref name="direction"/> is
    /// +1 for the end at X=0 (arc opens toward +X, i.e. toward center court) or -1 for the end at
    /// X=<see cref="LengthMeters"/> (arc opens toward -X).
    /// </summary>
    private static void AddKeyAndArc(List<CourtMarking> markings, double baselineX, int direction)
    {
        var halfWidth = WidthMeters / 2;
        var freeThrowX = baselineX + (direction * FreeThrowDistanceMeters);
        var basketX = baselineX + (direction * BasketDistanceFromBaselineMeters);

        markings.Add(new RectMarking(
            X: Math.Min(baselineX, freeThrowX),
            Y: halfWidth - (KeyWidthMeters / 2),
            Width: FreeThrowDistanceMeters,
            Height: KeyWidthMeters));

        markings.Add(new CircleMarking(freeThrowX, halfWidth, FreeThrowCircleRadiusMeters));

        // The three-point circle would cross the sideline before closing, so it runs as a straight line
        // parallel to the sideline from the baseline out to where a true radius-R arc from the basket would land.
        var sidelineOffset = halfWidth - CornerSidelineDistanceMeters;
        var straightRunLength = Math.Sqrt((ThreePointRadiusMeters * ThreePointRadiusMeters) - (sidelineOffset * sidelineOffset));
        var clipX = basketX + (direction * straightRunLength);

        markings.Add(new LineMarking(baselineX, CornerSidelineDistanceMeters, clipX, CornerSidelineDistanceMeters));
        markings.Add(new LineMarking(baselineX, WidthMeters - CornerSidelineDistanceMeters, clipX, WidthMeters - CornerSidelineDistanceMeters));

        var sweepAngleDeg = Math.Atan2(sidelineOffset, straightRunLength) * 180 / Math.PI;
        var centerAngleDeg = direction > 0 ? 0 : 180;
        markings.Add(new ArcMarking(basketX, halfWidth, ThreePointRadiusMeters, centerAngleDeg - sweepAngleDeg, centerAngleDeg + sweepAngleDeg));
    }
}
