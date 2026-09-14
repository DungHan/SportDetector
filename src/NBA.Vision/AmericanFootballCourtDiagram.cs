namespace NBA.Vision;

/// <summary>
/// American football (NFL) field diagram: 100 yards of field of play plus a 10-yard end zone at each end,
/// a yard line every 5 yards (every 10 yards drawn thicker, matching the goal lines), and the end zones
/// picked out in a darker green block.
/// </summary>
internal static class AmericanFootballCourtDiagram
{
    private const double EndZoneDepthMeters = 9.144; // 10 yd
    private const double FieldOfPlayLengthMeters = 91.44; // 100 yd
    private const double TotalLengthMeters = EndZoneDepthMeters + FieldOfPlayLengthMeters + EndZoneDepthMeters;
    private const double WidthMeters = 48.768; // 160 ft / 53.33 yd
    private const double YardLineSpacingMeters = 4.572; // 5 yd
    private const int YardLineCount = 21; // every 5 yd across 100 yd of field of play, inclusive of both goal lines
    private const double MinorLineThicknessMeters = 0.08;
    private const double MajorLineThicknessMeters = 0.15; // every 10 yd, and both goal lines
    private const double NumberInsetFromSidelineMeters = 6.0; // roughly matches the real ~9 ft inset
    private const double NumberHeightMeters = 2.0;

    internal static readonly CourtDiagramSpec Definition = Build();

    private static CourtDiagramSpec Build()
    {
        List<CourtMarking> markings =
        [
            new RectMarking(0, 0, EndZoneDepthMeters, WidthMeters, Filled: true, FillHex: "#1B5E20"),
            new RectMarking(EndZoneDepthMeters + FieldOfPlayLengthMeters, 0, EndZoneDepthMeters, WidthMeters, Filled: true, FillHex: "#1B5E20"),
        ];

        for (var i = 0; i < YardLineCount; i++)
        {
            var x = EndZoneDepthMeters + (i * YardLineSpacingMeters);
            var isMajor = i == 0 || i == YardLineCount - 1 || i % 2 == 0; // goal lines and every 10 yd
            markings.Add(new LineMarking(x, 0, x, WidthMeters, isMajor ? MajorLineThicknessMeters : MinorLineThicknessMeters));

            // Yard numbers sit at every 10-yard line except the two goal lines (i=0 and i=last), counting up to
            // midfield and back down (10,20,...,50,...,20,10) - printed near both sidelines, as on a real field.
            if (isMajor && i != 0 && i != YardLineCount - 1)
            {
                var tensFromNearGoal = i / 2 * 10;
                var yardNumber = tensFromNearGoal <= 50 ? tensFromNearGoal : 100 - tensFromNearGoal;
                markings.Add(new TextMarking(x, NumberInsetFromSidelineMeters, yardNumber.ToString(), NumberHeightMeters));
                markings.Add(new TextMarking(x, WidthMeters - NumberInsetFromSidelineMeters, yardNumber.ToString(), NumberHeightMeters));
            }
        }

        return new CourtDiagramSpec(
            SportType.AmericanFootball,
            "American football (NFL field)",
            SurfaceWidthMeters: WidthMeters,
            SurfaceLengthMeters: TotalLengthMeters,
            Colors: new CourtColorScheme(SurfaceHex: "#2E7D32", LineHex: "#FFFFFF"),
            Markings: markings);
    }
}
