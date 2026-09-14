namespace NBA.Vision;

/// <summary>
/// Tennis court diagram, drawn portrait (baselines top/bottom, sidelines left/right, matching how broadcast
/// diagrams usually orient it) at doubles-court size (10.97m sideline-to-sideline x 23.77m baseline-to-baseline):
/// singles sidelines, net, service lines, and center service lines. Tan/orange surround with a dark green
/// play surface, per the reference look requested for this sport specifically (other sports use their own
/// broadcast-typical colors).
/// </summary>
internal static class TennisCourtDiagram
{
    private const double DoublesWidthMeters = 10.97; // sideline to sideline - the horizontal axis here
    private const double LengthMeters = 23.77; // baseline to baseline - the vertical axis here
    private const double SinglesWidthMeters = 8.23;
    private const double ServiceLineDistanceFromNetMeters = 6.4;

    internal static readonly CourtDiagramSpec Definition = Build();

    private static CourtDiagramSpec Build()
    {
        var singlesInset = (DoublesWidthMeters - SinglesWidthMeters) / 2;
        var nearSinglesX = singlesInset;
        var farSinglesX = DoublesWidthMeters - singlesInset;
        var netY = LengthMeters / 2;
        var nearServiceY = netY - ServiceLineDistanceFromNetMeters;
        var farServiceY = netY + ServiceLineDistanceFromNetMeters;
        var centerX = DoublesWidthMeters / 2;

        List<CourtMarking> markings =
        [
            new LineMarking(nearSinglesX, 0, nearSinglesX, LengthMeters),
            new LineMarking(farSinglesX, 0, farSinglesX, LengthMeters),
            new LineMarking(0, netY, DoublesWidthMeters, netY, ThicknessMeters: 0.03),
            new LineMarking(nearSinglesX, nearServiceY, farSinglesX, nearServiceY),
            new LineMarking(nearSinglesX, farServiceY, farSinglesX, farServiceY),
            new LineMarking(centerX, nearServiceY, centerX, netY),
            new LineMarking(centerX, netY, centerX, farServiceY),
        ];

        return new CourtDiagramSpec(
            SportType.Tennis,
            "Tennis (hard court)",
            SurfaceWidthMeters: LengthMeters,
            SurfaceLengthMeters: DoublesWidthMeters,
            Colors: new CourtColorScheme(SurfaceHex: "#3F6B52", LineHex: "#FFFFFF", SurroundHex: "#D9A05C"),
            Markings: markings);
    }
}
