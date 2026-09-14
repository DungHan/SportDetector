namespace NBA.Vision;

/// <summary>
/// Sport-keyed registry of <see cref="CourtGeometryDefinition"/>s. Calibration (automatic and manual) and
/// homography computation are written against "the current source's selected geometry", never a hardcoded
/// sport - adding a sport later means adding an entry here, not touching calibration code. See design.md's
/// "Sport-keyed court geometry registry" decision. Also doubles as the sport-classification "is this sport
/// supported" check (spec: "Sport registry is extensible") - one registry, one source of truth, instead of
/// two lists that could drift out of sync.
/// </summary>
public static class CourtGeometryRegistry
{
    public static IReadOnlyDictionary<SportType, CourtGeometryDefinition> All { get; } =
        new Dictionary<SportType, CourtGeometryDefinition>
        {
            [SportType.Basketball] = BasketballGeometry.Definition,
        };

    public static bool IsSupported(SportType sport) => All.ContainsKey(sport);

    public static bool TryGet(SportType sport, out CourtGeometryDefinition definition) =>
        All.TryGetValue(sport, out definition!);
}
