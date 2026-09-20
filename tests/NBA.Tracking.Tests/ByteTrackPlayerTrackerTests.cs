using NBA.Vision;

namespace NBA.Tracking.Tests;

public class ByteTrackPlayerTrackerTests
{
    private static PlayerDetection Box(double left, double top, double right, double bottom, float confidence) =>
        new(left, top, right, bottom, confidence);

    private static PlayerDetection Box(double left, double top, double right, double bottom, float confidence, (byte R, byte G, byte B) color) =>
        new(left, top, right, bottom, confidence, color);

    [Fact]
    public void Update_SmoothlyMovingHighConfidenceDetection_KeepsSameTrackId()
    {
        var tracker = new ByteTrackPlayerTracker();

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var firstId = Assert.Single(first).TrackId;

        for (var step = 1; step <= 5; step++)
        {
            var result = tracker.Update([Box(step * 2.0, 0, (step * 2.0) + 10, 10, 0.9f)]);
            var track = Assert.Single(result);
            Assert.Equal(firstId, track.TrackId);
        }
    }

    [Fact]
    public void Update_DetectionDropsToLowConfidenceForOneFrame_StillMatchedAndKeepsId()
    {
        // highConfidenceThreshold defaults to 0.6f - 0.2f is "low" but still above whatever IPlayerDetector's
        // own threshold would have been (this tracker doesn't re-apply that threshold, per design.md).
        var tracker = new ByteTrackPlayerTracker();

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var trackId = Assert.Single(first).TrackId;

        var occluded = tracker.Update([Box(1, 1, 11, 11, 0.2f)]);
        var occludedTrack = Assert.Single(occluded);
        Assert.Equal(trackId, occludedTrack.TrackId);

        var recovered = tracker.Update([Box(2, 2, 12, 12, 0.9f)]);
        var recoveredTrack = Assert.Single(recovered);
        Assert.Equal(trackId, recoveredTrack.TrackId);
    }

    [Fact]
    public void Update_LowConfidenceDetectionWithNoNearbyTrack_DiscardedWithoutCreatingTrack()
    {
        var tracker = new ByteTrackPlayerTracker();

        var result = tracker.Update([Box(0, 0, 10, 10, 0.2f)]);

        Assert.Empty(result);
    }

    [Fact]
    public void Update_TrackWithNoMatch_SurvivesUntilOcclusionBufferThenTerminates()
    {
        const int maxLostFrames = 3;
        var tracker = new ByteTrackPlayerTracker(maxLostFrames: maxLostFrames);

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var trackId = Assert.Single(first).TrackId;

        for (var missedFrame = 1; missedFrame < maxLostFrames; missedFrame++)
        {
            var result = tracker.Update([]);
            var track = Assert.Single(result);
            Assert.Equal(trackId, track.TrackId);
        }

        var afterBuffer = tracker.Update([]);
        Assert.Empty(afterBuffer);
    }

    [Fact]
    public void Update_UnmatchedPastVisibilityLimit_WithheldFromResultButNotTerminated()
    {
        const int maxVisibleLostFrames = 2;
        var tracker = new ByteTrackPlayerTracker(maxLostFrames: 10, maxVisibleLostFrames: maxVisibleLostFrames);

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var trackId = Assert.Single(first).TrackId;

        for (var missedFrame = 1; missedFrame <= maxVisibleLostFrames; missedFrame++)
        {
            var result = tracker.Update([]);
            var track = Assert.Single(result);
            Assert.Equal(trackId, track.TrackId);
        }

        // One more miss than maxVisibleLostFrames tolerates - the track is still alive (well under
        // maxLostFrames: 10) but should no longer be reported.
        var withheld = tracker.Update([]);
        Assert.Empty(withheld);

        // Re-matching brings it back with the same ID, proving it was withheld rather than terminated.
        var recovered = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var recoveredTrack = Assert.Single(recovered);
        Assert.Equal(trackId, recoveredTrack.TrackId);
    }

    [Fact]
    public void Update_FramesSinceMatch_ZeroWhenMatchedThisCall_PositiveWhileCoasting()
    {
        // A consumer that needs to distinguish "this frame's real detection" from "still shown but coasting on
        // motion prediction" (e.g. the minimap capping how many players it shows at once) reads this field -
        // it must track the same occlusion-buffer state Update_UnmatchedPastVisibilityLimit_... exercises above.
        var tracker = new ByteTrackPlayerTracker(maxLostFrames: 10, maxVisibleLostFrames: 2);

        var matched = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        Assert.Equal(0, Assert.Single(matched).FramesSinceMatch);

        var missedOnce = tracker.Update([]);
        Assert.Equal(1, Assert.Single(missedOnce).FramesSinceMatch);

        var missedTwice = tracker.Update([]);
        Assert.Equal(2, Assert.Single(missedTwice).FramesSinceMatch);

        var rematched = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        Assert.Equal(0, Assert.Single(rematched).FramesSinceMatch);
    }

    [Fact]
    public void PredictOnly_TrackWithheldAfterMissedUpdates_StaysWithheldDuringPrediction()
    {
        var tracker = new ByteTrackPlayerTracker(maxLostFrames: 10, maxVisibleLostFrames: 1);

        tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        tracker.Update([]); // LostFrames: 1 - still within maxVisibleLostFrames
        tracker.Update([]); // LostFrames: 2 - now past maxVisibleLostFrames

        var predicted = tracker.PredictOnly();

        Assert.Empty(predicted);
    }

    [Fact]
    public void Update_UnmatchedHighConfidenceDetection_CreatesNewTrackWithFreshId()
    {
        var tracker = new ByteTrackPlayerTracker();

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var firstId = Assert.Single(first).TrackId;

        // A second, far-away high-confidence detection alongside the first (still tracked) one.
        var result = tracker.Update([Box(0, 0, 10, 10, 0.9f), Box(500, 500, 510, 510, 0.9f)]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.TrackId == firstId);
        Assert.Contains(result, t => t.TrackId != firstId);
    }

    [Fact]
    public void Update_TerminatedTrackId_NeverReassignedToLaterDetectionAtSamePosition()
    {
        var tracker = new ByteTrackPlayerTracker(maxLostFrames: 1);

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var terminatedId = Assert.Single(first).TrackId;

        var afterTermination = tracker.Update([]); // 1 missed frame - terminates immediately (maxLostFrames: 1)
        Assert.Empty(afterTermination);

        // A new detection appears at the exact same position the terminated track occupied.
        var later = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var laterTrack = Assert.Single(later);

        Assert.NotEqual(terminatedId, laterTrack.TrackId);
    }

    [Fact]
    public void PredictOnly_LiveTrack_MovesTowardPredictedPositionKeepingSameId()
    {
        var tracker = new ByteTrackPlayerTracker();

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var trackId = Assert.Single(first).TrackId;

        // Establish a rightward velocity so the motion model has something to extrapolate.
        tracker.Update([Box(2, 0, 12, 10, 0.9f)]);
        tracker.Update([Box(4, 0, 14, 10, 0.9f)]);

        var predicted = tracker.PredictOnly();
        var predictedTrack = Assert.Single(predicted);

        Assert.Equal(trackId, predictedTrack.TrackId);
        Assert.True(predictedTrack.Left > 4, "expected the predicted box to keep moving right, not freeze in place");
    }

    [Fact]
    public void PredictOnly_RepeatedlyBeyondOcclusionBuffer_DoesNotTerminateTrack()
    {
        const int maxLostFrames = 3;
        var tracker = new ByteTrackPlayerTracker(maxLostFrames: maxLostFrames);

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var trackId = Assert.Single(first).TrackId;

        for (var step = 0; step < maxLostFrames * 5; step++)
        {
            var result = tracker.PredictOnly();
            var track = Assert.Single(result);
            Assert.Equal(trackId, track.TrackId);
        }
    }

    [Fact]
    public void PredictOnly_NoLiveTracks_ReturnsEmptyWithoutThrowing()
    {
        var tracker = new ByteTrackPlayerTracker();

        var result = tracker.PredictOnly();

        Assert.Empty(result);
    }

    [Fact]
    public void Update_UnmatchedCallsStillTerminateAtOcclusionBuffer_EvenWithInterleavedPredictOnlyCalls()
    {
        const int maxLostFrames = 3;
        var tracker = new ByteTrackPlayerTracker(maxLostFrames: maxLostFrames);

        var first = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        var trackId = Assert.Single(first).TrackId;

        for (var missedUpdate = 1; missedUpdate < maxLostFrames; missedUpdate++)
        {
            // Several PredictOnly calls between each real Update call must not affect the occlusion countdown.
            tracker.PredictOnly();
            tracker.PredictOnly();

            var result = tracker.Update([]);
            var track = Assert.Single(result);
            Assert.Equal(trackId, track.TrackId);
        }

        tracker.PredictOnly();
        var afterBuffer = tracker.Update([]);
        Assert.Empty(afterBuffer);
    }

    private static readonly (byte R, byte G, byte B) Red = (250, 10, 10);
    private static readonly (byte R, byte G, byte B) Blue = (10, 10, 250);

    [Fact]
    public void Update_MatchedDetectionWithColor_SurfacesItOnTrackedPlayer()
    {
        // TrackedPlayer.Color is the display-facing exposure of the tracker's internal color-veto estimate
        // (see MinimapView.axaml.cs's per-player marker fill) - this pins that it actually reaches the public
        // result, not just the private Track used for veto matching.
        var tracker = new ByteTrackPlayerTracker();

        var spawned = tracker.Update([Box(0, 0, 10, 10, 0.9f, Red)]);
        Assert.Equal(Red, Assert.Single(spawned).Color);
    }

    [Fact]
    public void Update_DetectionWithNoSampledColor_LeavesTrackedPlayerColorNull()
    {
        var tracker = new ByteTrackPlayerTracker();

        var spawned = tracker.Update([Box(0, 0, 10, 10, 0.9f)]);
        Assert.Null(Assert.Single(spawned).Color);
    }

    [Fact]
    public void Update_ColorMismatchedPair_NotAssociatedDespiteIouOverlap()
    {
        var tracker = new ByteTrackPlayerTracker();

        // Two well-separated tracks, seeded with distinct colors.
        var seeded = tracker.Update([Box(0, 0, 10, 10, 0.9f, Red), Box(500, 500, 510, 510, 0.9f, Blue)]);
        Assert.Equal(2, seeded.Count);
        var trackAId = seeded.Single(t => t.Left < 100).TrackId;
        var trackBId = seeded.Single(t => t.Left > 100).TrackId;

        // Next frame: a detection lands right on top of each track's predicted position, but with the *other*
        // track's color - a color-swap. IoU alone would match each track to the nearby box; the color veto
        // must block both, since this frame's two colors are well-separated (a real two-way split).
        var result = tracker.Update([Box(1, 1, 11, 11, 0.9f, Blue), Box(501, 501, 511, 511, 0.9f, Red)]);

        // Neither original track absorbed the mismatched-color detection at its own position - each stays
        // alive (unmatched, still visible) and each mismatched detection spawns its own new track instead of
        // being merged into the nearby track. Proven by track count (4, not 2) and by both original IDs
        // surviving as distinct, still-live tracks.
        Assert.Equal(4, result.Count);
        Assert.Contains(result, t => t.TrackId == trackAId);
        Assert.Contains(result, t => t.TrackId == trackBId);
    }

    [Fact]
    public void Update_ColorMatchedPair_AssociatesNormally()
    {
        var tracker = new ByteTrackPlayerTracker();

        var seeded = tracker.Update([Box(0, 0, 10, 10, 0.9f, Red), Box(500, 500, 510, 510, 0.9f, Blue)]);
        var trackAId = seeded.Single(t => t.Left < 100).TrackId;
        var trackBId = seeded.Single(t => t.Left > 100).TrackId;

        // Same shape as the mismatch test above, but colors line up with each track's own seeded color.
        var result = tracker.Update([Box(1, 1, 11, 11, 0.9f, Red), Box(501, 501, 511, 511, 0.9f, Blue)]);

        // Normal IoU association proceeds: same two track IDs, no new tracks spawned, boxes updated to the
        // exact matched detection boxes (proving a real match happened, not just unmatched coasting).
        Assert.Equal(2, result.Count);
        var trackA = Assert.Single(result, t => t.TrackId == trackAId);
        var trackB = Assert.Single(result, t => t.TrackId == trackBId);
        Assert.Equal(1, trackA.Left);
        Assert.Equal(501, trackB.Left);
    }

    [Fact]
    public void Update_FewerThanTwoColoredDetectionsThisFrame_VetoSkipped_IouOnlyAssociationApplies()
    {
        var tracker = new ByteTrackPlayerTracker();

        var seeded = tracker.Update([Box(0, 0, 10, 10, 0.9f, Red)]);
        var trackAId = Assert.Single(seeded).TrackId;

        // Only one detection this frame (mismatched color) - fewer than two colored detections means no real
        // two-way split can be computed, so the veto self-disables and IoU alone decides the match.
        var result = tracker.Update([Box(1, 1, 11, 11, 0.9f, Blue)]);

        var track = Assert.Single(result);
        Assert.Equal(trackAId, track.TrackId);
        Assert.Equal(1, track.Left); // matched to the new box despite the color mismatch
    }

    [Fact]
    public void Update_CentroidsTooCloseTogether_VetoSkipped_IouOnlyAssociationApplies()
    {
        var tracker = new ByteTrackPlayerTracker();

        var seeded = tracker.Update([Box(0, 0, 10, 10, 0.9f, Red)]);
        var trackAId = Assert.Single(seeded).TrackId;

        // This frame's two detections carry near-identical colors (blue-ish, close together) - no real
        // two-color split exists, so the minimum-centroid-separation safeguard disables the veto even though
        // both colors clearly disagree with the track's own (red) seeded color.
        var nearBlue1 = ((byte)10, (byte)10, (byte)250);
        var nearBlue2 = ((byte)12, (byte)9, (byte)248);
        var result = tracker.Update([Box(1, 1, 11, 11, 0.9f, nearBlue1), Box(900, 900, 910, 910, 0.9f, nearBlue2)]);

        var trackA = Assert.Single(result, t => t.TrackId == trackAId);
        Assert.Equal(1, trackA.Left); // matched despite the color mismatch, since the veto was inactive
    }

    [Fact]
    public void Update_NewlySpawnedTrackColor_IsSeededFromSpawningDetection_AndLaterInfluencesTheVeto()
    {
        var tracker = new ByteTrackPlayerTracker();

        // An unrelated track, spatially far from everything below for the rest of this test - its own
        // eventual fate (matched, unmatched, terminated) doesn't matter, since it never has IoU overlap with
        // any later box here.
        tracker.Update([Box(0, 0, 10, 10, 0.9f, Red)]);

        // A brand-new detection, nowhere near any existing track - an ordinary spawn (spawning never consults
        // the color veto at all, per the type-level doc comment - nothing here could block it even in
        // principle). Seeded, if spawning correctly seeds a track's color, with this detection's own (blue)
        // color.
        var afterSpawn = tracker.Update([Box(200, 200, 210, 210, 0.9f, Blue)]);
        var spawnedTrackId = Assert.Single(afterSpawn, t => t.Left > 100).TrackId;

        // A third frame: a detection lands right on the spawned track's position with a *conflicting* (red)
        // color, alongside another far-away, differently-colored detection for a valid two-way split this
        // frame. If the spawned track's color was really seeded to blue, this conflicting detection must be
        // vetoed (the spawned track stays unmatched at its own position, and a new track spawns for the
        // conflicting detection instead); if seeding had failed (color left null), the veto would never
        // trigger (a colorless track is always eligible) and the spawned track would incorrectly jump to this
        // conflicting detection's box.
        var result = tracker.Update([Box(202, 202, 212, 212, 0.9f, Red), Box(600, 600, 610, 610, 0.9f, Blue)]);

        var spawnedTrack = Assert.Single(result, t => t.TrackId == spawnedTrackId);
        Assert.Equal(200, spawnedTrack.Left); // still at its own predicted position, not the conflicting detection's box
    }

    [Fact]
    public void Update_RepeatedlyMatchedAgainstADifferentColor_EmaConvergesAwayFromTheSeededColor()
    {
        // Convergence is observed indirectly through its effect on the veto rather than by reading
        // TrackedPlayer.Color directly - exact EMA rounding at each step would make a direct assertion brittle,
        // whereas "does the veto now treat this track as Blue" is the behavior that actually matters.
        var tracker = new ByteTrackPlayerTracker();

        var seeded = tracker.Update([Box(0, 0, 10, 10, 0.9f, Red)]);
        var trackId = Assert.Single(seeded).TrackId;

        // Fifteen frames matched against the *same* position with a Blue detection - only one detection per
        // frame, so the veto self-disables each time (see the "fewer than two colored detections" test above)
        // and the match always proceeds by IoU alone, letting the color EMA drift each frame regardless of
        // agreement. At colorEmaAlpha's default (0.3), the seeded Red's remaining weight after 15 steps is
        // 0.7^15 (~0.5%) - the track's color estimate should be almost entirely Blue by the end.
        for (var i = 0; i < 15; i++)
        {
            var step = tracker.Update([Box(0, 0, 10, 10, 0.9f, Blue)]);
            Assert.Equal(trackId, Assert.Single(step).TrackId);
        }

        // Now present the *original* seeded color (Red) back at the track's position, alongside a far-away
        // Blue detection for a valid two-way split. If the EMA had truly converged to Blue, this Red detection
        // - the track's own long-abandoned original color - must now be vetoed as a mismatch; if convergence
        // never actually happened (e.g. a no-op EMA update), the track would still think of itself as Red and
        // would match this detection normally.
        var result = tracker.Update([Box(0, 0, 10, 10, 0.9f, Red), Box(500, 500, 510, 510, 0.9f, Blue)]);

        var track = Assert.Single(result, t => t.TrackId == trackId);
        Assert.Equal(0, track.Left); // stayed at its own predicted position - the Red detection was vetoed, not matched
    }
}
