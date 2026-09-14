namespace NBA.Vision;

/// <summary>
/// Sport-keyed registry of <see cref="CourtDiagramSpec"/>s - purely for rendering a court/field diagram once a
/// sport is classified. Deliberately separate from <see cref="CourtGeometryRegistry"/>: that registry's
/// membership means "this sport supports homography calibration", which is a different (and currently
/// narrower) guarantee than "this sport has a diagram to draw". Every sport the CLIP classifier can name
/// (see <c>tools/clip-text-encoder/generate_prompt_embeddings.py</c>) is registered here.
/// </summary>
public static class CourtDiagramRegistry
{
    public static IReadOnlyDictionary<SportType, CourtDiagramSpec> All { get; } =
        new Dictionary<SportType, CourtDiagramSpec>
        {
            [SportType.Basketball] = BasketballCourtDiagram.Definition,
            [SportType.Soccer] = SoccerCourtDiagram.Definition,
            [SportType.Tennis] = TennisCourtDiagram.Definition,
            [SportType.AmericanFootball] = AmericanFootballCourtDiagram.Definition,
        };

    public static bool IsSupported(SportType sport) => All.ContainsKey(sport);

    public static bool TryGet(SportType sport, out CourtDiagramSpec spec) =>
        All.TryGetValue(sport, out spec!);
}
