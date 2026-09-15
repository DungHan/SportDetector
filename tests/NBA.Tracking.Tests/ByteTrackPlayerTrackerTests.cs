using NBA.Vision;

namespace NBA.Tracking.Tests;

public class ByteTrackPlayerTrackerTests
{
    private static PlayerDetection Box(double left, double top, double right, double bottom, float confidence) =>
        new(left, top, right, bottom, confidence);

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
}
