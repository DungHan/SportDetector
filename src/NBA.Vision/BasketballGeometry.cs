namespace NBA.Vision;

/// <summary>
/// The only fully populated <see cref="CourtGeometryDefinition"/> in this change (see proposal.md's Non-Goals -
/// other sports get a registry extension point, not real geometry). Dimensions are a regulation NBA court
/// (94ft x 50ft), origin at the top-left baseline corner, X increasing along the length, Y along the width.
/// Three-point/free-throw landmark positions are reasonable approximations of the true NBA rulebook geometry
/// (which has a non-uniform arc radius) - precise enough to compute a usable homography, but worth replacing
/// with exact rulebook coordinates once real calibration accuracy testing starts.
/// </summary>
internal static class BasketballGeometry
{
    private const double LengthMeters = 28.6512; // 94 ft
    private const double WidthMeters = 15.24; // 50 ft

    internal static readonly CourtGeometryDefinition Definition = new(
        Sport: SportType.Basketball,
        DisplayName: "Basketball (NBA court)",
        SurfaceWidthMeters: WidthMeters,
        SurfaceLengthMeters: LengthMeters,
        Landmarks:
        [
            new CourtLandmark("BaselineCorner_Left_Near", 0, 0),
            new CourtLandmark("BaselineCorner_Left_Far", 0, WidthMeters),
            new CourtLandmark("BaselineCorner_Right_Near", LengthMeters, 0),
            new CourtLandmark("BaselineCorner_Right_Far", LengthMeters, WidthMeters),
            new CourtLandmark("CenterCourt", LengthMeters / 2, WidthMeters / 2),
            new CourtLandmark("FreeThrowLineCenter_Left", 5.7912, WidthMeters / 2),
            new CourtLandmark("FreeThrowLineCenter_Right", LengthMeters - 5.7912, WidthMeters / 2),
            new CourtLandmark("ThreePointArcTop_Left", 8.814, WidthMeters / 2),
            new CourtLandmark("ThreePointArcTop_Right", LengthMeters - 8.814, WidthMeters / 2),
        ]);
}
