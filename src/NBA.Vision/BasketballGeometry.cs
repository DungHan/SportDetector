namespace NBA.Vision;

/// <summary>
/// The only fully populated <see cref="CourtGeometryDefinition"/> in this change (see proposal.md's Non-Goals -
/// other sports get a registry extension point, not real geometry). Dimensions are a regulation NBA court
/// (94ft x 50ft), origin at the top-left baseline corner, X increasing along the length, Y along the width.
/// Three-point/free-throw landmark positions are reasonable approximations of the true NBA rulebook geometry
/// (which has a non-uniform arc radius) - precise enough to compute a usable homography, but worth replacing
/// with exact rulebook coordinates once real calibration accuracy testing starts.
///
/// <see cref="CourtLandmark.KeypointIndex"/> is set on the landmarks that the trained court-keypoint model
/// (see models/README.md) actually predicts - a YOLOv8-pose model trained on Roboflow's public
/// "basketball-court-detection-2" dataset (33 keypoints/court, CC BY 4.0), whose keypoint order was reverse
/// engineered from `flip_idx` (mirror pairs) and real broadcast frames rather than an authoritative schema
/// (none was found). <c>CenterCourt</c> (index 16, the jump-circle center) and the <c>FreeThrowLineCenter_*</c>
/// pair (indices 6/26, the free-throw circle centers - the same real-world point as the free-throw line's
/// midpoint) were confidently identified and reuse the existing manually-clicked landmarks. The paint-baseline
/// corners and mid-court/sideline points below are new, model-only landmarks. The 4 true court corners
/// (<c>BaselineCorner_*</c>) and the 3-point arc apexes are NOT in the model's 33-point set at all (it has
/// side-of-arc points instead, whose exact real-world position wasn't confidently pinned down) - they remain
/// manual-calibration-only (<c>KeypointIndex</c> null).
///
/// <b>Unverified risk</b>: for each mirrored pair below (e.g. the two paint corners on one baseline), which
/// physical corner is index N vs. index N+1 could not be confirmed from single broadcast frames alone - if
/// swapped, the *entire* computed homography mirrors left/right (a consistent, detectable error, not a
/// scrambled one), not a per-point error. Needs verification against a real capture once available.
/// </summary>
internal static class BasketballGeometry
{
    private const double LengthMeters = 28.6512; // 94 ft
    private const double WidthMeters = 15.24; // 50 ft
    private const double FreeThrowLineDistance = 5.7912; // baseline -> free-throw line, both ends
    private const double LaneHalfWidthMeters = 2.4384; // NBA lane ("the paint") is 16 ft wide

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
            new CourtLandmark("CenterCourt", LengthMeters / 2, WidthMeters / 2, KeypointIndex: 16),
            new CourtLandmark("FreeThrowLineCenter_Left", FreeThrowLineDistance, WidthMeters / 2, KeypointIndex: 6),
            new CourtLandmark("FreeThrowLineCenter_Right", LengthMeters - FreeThrowLineDistance, WidthMeters / 2, KeypointIndex: 26),
            new CourtLandmark("ThreePointArcTop_Left", 8.814, WidthMeters / 2),
            new CourtLandmark("ThreePointArcTop_Right", LengthMeters - 8.814, WidthMeters / 2),
            new CourtLandmark("MidCourtLine_SidelineA", LengthMeters / 2, 0, KeypointIndex: 15),
            new CourtLandmark("MidCourtLine_SidelineB", LengthMeters / 2, WidthMeters, KeypointIndex: 17),
            new CourtLandmark("PaintCorner_Left_A", 0, (WidthMeters / 2) - LaneHalfWidthMeters, KeypointIndex: 0),
            new CourtLandmark("PaintCorner_Left_B", 0, (WidthMeters / 2) + LaneHalfWidthMeters, KeypointIndex: 1),
            new CourtLandmark("PaintCorner_Right_A", LengthMeters, (WidthMeters / 2) - LaneHalfWidthMeters, KeypointIndex: 27),
            new CourtLandmark("PaintCorner_Right_B", LengthMeters, (WidthMeters / 2) + LaneHalfWidthMeters, KeypointIndex: 28),
        ]);
}
