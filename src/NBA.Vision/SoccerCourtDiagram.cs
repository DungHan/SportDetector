namespace NBA.Vision;

/// <summary>
/// Soccer/football pitch diagram at FIFA's recommended international size (105m x 68m): center line/circle,
/// both penalty areas, goal areas, penalty spots and arcs, and the four corner arcs.
/// </summary>
internal static class SoccerCourtDiagram
{
    private const double LengthMeters = 105.0;
    private const double WidthMeters = 68.0;
    private const double CenterCircleRadiusMeters = 9.15;
    private const double PenaltyAreaDepthMeters = 16.5;
    private const double PenaltyAreaWidthMeters = 40.32;
    private const double GoalAreaDepthMeters = 5.5;
    private const double GoalAreaWidthMeters = 18.32;
    private const double PenaltySpotDistanceMeters = 11.0;
    private const double PenaltyArcRadiusMeters = 9.15;
    private const double CornerArcRadiusMeters = 1.0;

    internal static readonly CourtDiagramSpec Definition = Build();

    private static CourtDiagramSpec Build()
    {
        var halfWidth = WidthMeters / 2;
        var markings = new List<CourtMarking>
        {
            new LineMarking(LengthMeters / 2, 0, LengthMeters / 2, WidthMeters),
            new CircleMarking(LengthMeters / 2, halfWidth, CenterCircleRadiusMeters),
        };

        AddGoalEnd(markings, goalLineX: 0, direction: 1);
        AddGoalEnd(markings, goalLineX: LengthMeters, direction: -1);
        AddCornerArcs(markings);

        return new CourtDiagramSpec(
            SportType.Soccer,
            "Soccer (FIFA pitch)",
            SurfaceWidthMeters: WidthMeters,
            SurfaceLengthMeters: LengthMeters,
            Colors: new CourtColorScheme(SurfaceHex: "#2E7D32", LineHex: "#FFFFFF"),
            Markings: markings);
    }

    /// <summary>
    /// Draws one end's penalty area, goal area, penalty spot, and penalty arc (the "D" - only the portion of
    /// the circle around the spot that sits outside the penalty area is visible in a real pitch).
    /// <paramref name="direction"/> is +1 for the goal line at X=0 (arc opens toward +X) or -1 for the goal
    /// line at X=<see cref="LengthMeters"/> (arc opens toward -X).
    /// </summary>
    private static void AddGoalEnd(List<CourtMarking> markings, double goalLineX, int direction)
    {
        var halfWidth = WidthMeters / 2;
        var penaltyAreaEdgeX = goalLineX + (direction * PenaltyAreaDepthMeters);
        var penaltySpotX = goalLineX + (direction * PenaltySpotDistanceMeters);

        markings.Add(new RectMarking(
            X: Math.Min(goalLineX, penaltyAreaEdgeX),
            Y: halfWidth - (PenaltyAreaWidthMeters / 2),
            Width: PenaltyAreaDepthMeters,
            Height: PenaltyAreaWidthMeters));

        markings.Add(new RectMarking(
            X: Math.Min(goalLineX, goalLineX + (direction * GoalAreaDepthMeters)),
            Y: halfWidth - (GoalAreaWidthMeters / 2),
            Width: GoalAreaDepthMeters,
            Height: GoalAreaWidthMeters));

        markings.Add(new CircleMarking(penaltySpotX, halfWidth, 0.15));

        // The penalty arc is the part of the radius-R circle around the spot that lies outside the penalty area.
        var edgeOffset = Math.Abs(penaltyAreaEdgeX - penaltySpotX);
        var halfChord = Math.Sqrt((PenaltyArcRadiusMeters * PenaltyArcRadiusMeters) - (edgeOffset * edgeOffset));
        var sweepAngleDeg = Math.Atan2(halfChord, edgeOffset) * 180 / Math.PI;
        var centerAngleDeg = direction > 0 ? 0 : 180;
        markings.Add(new ArcMarking(penaltySpotX, halfWidth, PenaltyArcRadiusMeters, centerAngleDeg - sweepAngleDeg, centerAngleDeg + sweepAngleDeg));
    }

    /// <summary>Quarter-circle flags at all four corners, each sweeping the 90 degrees that fall inside the pitch.</summary>
    private static void AddCornerArcs(List<CourtMarking> markings)
    {
        (double X, double Y)[] corners = [(0, 0), (LengthMeters, 0), (LengthMeters, WidthMeters), (0, WidthMeters)];
        for (var i = 0; i < corners.Length; i++)
        {
            markings.Add(new ArcMarking(corners[i].X, corners[i].Y, CornerArcRadiusMeters, i * 90.0, (i + 1) * 90.0));
        }
    }
}
