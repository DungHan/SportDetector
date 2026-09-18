namespace NBA.Vision;

/// <summary>
/// The only fully populated <see cref="CourtGeometryDefinition"/> in this change (see proposal.md's Non-Goals -
/// other sports get a registry extension point, not real geometry). Dimensions are a regulation NBA court
/// (94ft x 50ft), origin at the top-left baseline corner, X increasing along the length, Y along the width.
///
/// <see cref="CourtLandmark.KeypointIndex"/> is set on the landmarks that the trained court-keypoint model
/// (see models/README.md) actually predicts - a YOLOv8-pose model trained on Roboflow's public
/// "basketball-court-detection-2" dataset (33 keypoints/court, CC BY 4.0). The dataset's keypoint order has no
/// authoritative name list (Roboflow's own export just numbers them with gaps, no schema published) so it was
/// reverse engineered from three independent sources: (1) `data.yaml`'s `flip_idx` mirror structure - indices
/// 0-14 and 18-32 are two mirrored 15-point halves (one per basket end), each self-mapping to the other end's
/// equivalent landmark; indices 15/16/17 sit on the mirror axis itself (the half-court line) and self-map;
/// (2) pixel-level cross-referencing of real annotated broadcast frames against arenas with solid-color paint
/// (the lane's exact boundary was matched to keypoint pixel coordinates within ~15px); (3) direct confirmation
/// from someone who knows the real NBA court markings being labeled. Earlier revisions of this file guessed
/// indices 0/1/27/28 were the paint's baseline corners - that guess was wrong (visual inspection kept finding
/// them clustered near the rim) and the true explanation is camera perspective: the far-end baseline corner and
/// its adjacent corner-three point compress toward the basket's screen position under a sideline broadcast
/// camera, which looks like "near the rim" until you check where the *near* corner (same landmark type, less
/// foreshortened) actually lands.
///
/// One basket end's 15-point half, in index order, all confirmed against real annotated frames:
/// 0/5 true baseline corners; 1/4 three-point line x baseline ("corner three"); 2/3 lane line x baseline;
/// 6 restricted-area (charge circle) center; 7/8 three-point line's straight-to-arc transition; 9/11 lane line x
/// free-throw line; 10 free-throw line center (the free-throw line's midpoint - same real-world point already
/// used for <c>FreeThrowLineCenter_*</c>); 12/14 the 28-foot sideline hash marks (NBA's last-2-minutes
/// advance-the-ball spot / coaching-box boundary); 13 the three-point arc's apex ("top of the key" straightaway
/// three). Each pair's lower index sits at the Y=0 sideline (suffixed <c>_Near</c> below), the higher index at
/// the Y=SurfaceWidthMeters sideline (<c>_Far</c>) - this is just a naming convention, not a claim about camera
/// distance. <c>CenterCourt</c> (index 16) and the two half-court-line/sideline intersections (15/17) sit on
/// the mirror axis and are shared by both ends.
///
/// <b>Unverified risk</b>: which physical corner is the lower vs. higher index in each mirrored pair was
/// confirmed for the pairs above (via the solid-paint pixel matching and direct court-marking knowledge), but
/// was not independently re-checked against this exact dataset export for every single pair - if any pair is
/// swapped, the *entire* computed homography mirrors left/right (a consistent, detectable error, not a
/// scrambled one). Needs verification against a real capture once available.
/// </summary>
internal static class BasketballGeometry
{
    private const double LengthMeters = 28.6512; // 94 ft
    private const double WidthMeters = 15.24; // 50 ft
    private const double FreeThrowLineDistance = 5.7912; // baseline -> free-throw line, both ends
    private const double LaneHalfWidthMeters = 2.4384; // NBA lane ("the paint") is 16 ft wide
    private const double BasketCenterDistance = 1.6002; // baseline -> basket/rim center (and restricted-area center), 5.25 ft
    private const double CornerThreeSidelineOffset = 0.9144; // corner three-point line -> sideline, 3 ft
    private const double ThreePointArcRadius = 7.239; // 23.75 ft, measured from the basket center
    private const double SidelineHashMark28Ft = 8.5344; // 28 ft: NBA's advance-the-ball spot / coaching-box limit
    private const double ThreePointArcApexDistance = BasketCenterDistance + ThreePointArcRadius; // "top of the key"

    // Where the corner three's straight segment meets the arc: the arc, centered on the basket, evaluated at
    // the corner three's Y offset from center court.
    private static readonly double ThreePointTransitionX = BasketCenterDistance + Math.Sqrt(
        (ThreePointArcRadius * ThreePointArcRadius) -
        ((WidthMeters / 2 - CornerThreeSidelineOffset) * (WidthMeters / 2 - CornerThreeSidelineOffset)));

    internal static readonly CourtGeometryDefinition Definition = new(
        Sport: SportType.Basketball,
        DisplayName: "Basketball (NBA court)",
        SurfaceWidthMeters: WidthMeters,
        SurfaceLengthMeters: LengthMeters,
        Landmarks:
        [
            // Left end (baseline at X=0). Index order matches the model's 0-14 half.
            new CourtLandmark("BaselineCorner_Left_Near", 0, 0, KeypointIndex: 0),
            new CourtLandmark("ThreePointCornerBaseline_Left_Near", 0, CornerThreeSidelineOffset, KeypointIndex: 1),
            new CourtLandmark("PaintCorner_Left_Near", 0, (WidthMeters / 2) - LaneHalfWidthMeters, KeypointIndex: 2),
            new CourtLandmark("PaintCorner_Left_Far", 0, (WidthMeters / 2) + LaneHalfWidthMeters, KeypointIndex: 3),
            new CourtLandmark("ThreePointCornerBaseline_Left_Far", 0, WidthMeters - CornerThreeSidelineOffset, KeypointIndex: 4),
            new CourtLandmark("BaselineCorner_Left_Far", 0, WidthMeters, KeypointIndex: 5),
            new CourtLandmark("RestrictedAreaCenter_Left", BasketCenterDistance, WidthMeters / 2, KeypointIndex: 6),
            new CourtLandmark("ThreePointTransition_Left_Near", ThreePointTransitionX, CornerThreeSidelineOffset, KeypointIndex: 7),
            new CourtLandmark("ThreePointTransition_Left_Far", ThreePointTransitionX, WidthMeters - CornerThreeSidelineOffset, KeypointIndex: 8),
            new CourtLandmark("FreeThrowLanePost_Left_Near", FreeThrowLineDistance, (WidthMeters / 2) - LaneHalfWidthMeters, KeypointIndex: 9),
            new CourtLandmark("FreeThrowLineCenter_Left", FreeThrowLineDistance, WidthMeters / 2, KeypointIndex: 10),
            new CourtLandmark("FreeThrowLanePost_Left_Far", FreeThrowLineDistance, (WidthMeters / 2) + LaneHalfWidthMeters, KeypointIndex: 11),
            new CourtLandmark("SidelineHashMark28Ft_Left_Near", SidelineHashMark28Ft, 0, KeypointIndex: 12),
            new CourtLandmark("ThreePointArcTop_Left", ThreePointArcApexDistance, WidthMeters / 2, KeypointIndex: 13),
            new CourtLandmark("SidelineHashMark28Ft_Left_Far", SidelineHashMark28Ft, WidthMeters, KeypointIndex: 14),

            // Shared mirror-axis landmarks (self-mapping under flip_idx).
            new CourtLandmark("MidCourtLine_SidelineA", LengthMeters / 2, 0, KeypointIndex: 15),
            new CourtLandmark("CenterCourt", LengthMeters / 2, WidthMeters / 2, KeypointIndex: 16),
            new CourtLandmark("MidCourtLine_SidelineB", LengthMeters / 2, WidthMeters, KeypointIndex: 17),

            // Right end (baseline at X=LengthMeters), mirroring the left end 1:1 via flip_idx - the index for
            // each landmark type below is NOT a fixed offset from its left-end counterpart (flip_idx's mapping
            // isn't linear); see the class doc above for the full 0-14 <-> 18-32 pairing.
            new CourtLandmark("SidelineHashMark28Ft_Right_Near", LengthMeters - SidelineHashMark28Ft, 0, KeypointIndex: 18),
            new CourtLandmark("ThreePointArcTop_Right", LengthMeters - ThreePointArcApexDistance, WidthMeters / 2, KeypointIndex: 19),
            new CourtLandmark("SidelineHashMark28Ft_Right_Far", LengthMeters - SidelineHashMark28Ft, WidthMeters, KeypointIndex: 20),
            new CourtLandmark("FreeThrowLanePost_Right_Near", LengthMeters - FreeThrowLineDistance, (WidthMeters / 2) - LaneHalfWidthMeters, KeypointIndex: 21),
            new CourtLandmark("FreeThrowLineCenter_Right", LengthMeters - FreeThrowLineDistance, WidthMeters / 2, KeypointIndex: 22),
            new CourtLandmark("FreeThrowLanePost_Right_Far", LengthMeters - FreeThrowLineDistance, (WidthMeters / 2) + LaneHalfWidthMeters, KeypointIndex: 23),
            new CourtLandmark("ThreePointTransition_Right_Near", LengthMeters - ThreePointTransitionX, CornerThreeSidelineOffset, KeypointIndex: 24),
            new CourtLandmark("ThreePointTransition_Right_Far", LengthMeters - ThreePointTransitionX, WidthMeters - CornerThreeSidelineOffset, KeypointIndex: 25),
            new CourtLandmark("RestrictedAreaCenter_Right", LengthMeters - BasketCenterDistance, WidthMeters / 2, KeypointIndex: 26),
            new CourtLandmark("BaselineCorner_Right_Near", LengthMeters, 0, KeypointIndex: 27),
            new CourtLandmark("ThreePointCornerBaseline_Right_Near", LengthMeters, CornerThreeSidelineOffset, KeypointIndex: 28),
            new CourtLandmark("PaintCorner_Right_Near", LengthMeters, (WidthMeters / 2) - LaneHalfWidthMeters, KeypointIndex: 29),
            new CourtLandmark("PaintCorner_Right_Far", LengthMeters, (WidthMeters / 2) + LaneHalfWidthMeters, KeypointIndex: 30),
            new CourtLandmark("ThreePointCornerBaseline_Right_Far", LengthMeters, WidthMeters - CornerThreeSidelineOffset, KeypointIndex: 31),
            new CourtLandmark("BaselineCorner_Right_Far", LengthMeters, WidthMeters, KeypointIndex: 32),
        ]);
}
