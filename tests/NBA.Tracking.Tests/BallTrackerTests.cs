using NBA.Vision;

namespace NBA.Tracking.Tests;

public class BallTrackerTests
{
    private static OnCourtObjectDetection Ball(double left, double top, double right, double bottom, float confidence) =>
        new(left, top, right, bottom, confidence, "Ball");

    [Fact]
    public void Update_SingleDetection_ReturnsSmoothedPosition()
    {
        var tracker = new BallTracker();

        var first = tracker.Update([Ball(100, 100, 110, 110, 0.9f)]);
        Assert.NotNull(first);

        // First-ever correction initializes the filter directly to the measurement (no prior to blend
        // against, per Axis1DKalmanFilter's own documented first-call behavior).
        Assert.Equal(100, first!.Value.Left);

        // A second, jittery detection should now be smoothed - not equal to the raw new detection.
        var second = tracker.Update([Ball(120, 100, 130, 110, 0.9f)]);
        Assert.NotNull(second);
        Assert.True(second!.Value.Left > 100 && second.Value.Left < 120,
            $"expected smoothed Left between 100 and 120, got {second.Value.Left}");
    }

    [Fact]
    public void Update_MultipleBallDetectionsInOneAttempt_UsesOnlyHighestConfidence()
    {
        var tracker = new BallTracker();

        var result = tracker.Update([
            Ball(500, 500, 510, 510, 0.4f), // false positive, lower confidence
            Ball(100, 100, 110, 110, 0.95f), // the real ball
        ]);

        Assert.NotNull(result);
        Assert.Equal(100, result!.Value.Left);
    }

    [Fact]
    public void Update_IgnoresNonBallDetections()
    {
        var tracker = new BallTracker();

        var result = tracker.Update([new OnCourtObjectDetection(100, 100, 110, 110, 0.95f, "Hoop")]);

        Assert.Null(result);
    }

    [Fact]
    public void Update_MissesWithinCoastBound_KeepsReportingPredictedPosition()
    {
        var tracker = new BallTracker(maxCoastFrames: 3);

        var acquired = tracker.Update([Ball(100, 100, 110, 110, 0.9f)]);
        Assert.NotNull(acquired);

        for (var i = 0; i < 3; i++)
        {
            var coasted = tracker.Update([]);
            Assert.NotNull(coasted);
        }
    }

    [Fact]
    public void Update_MissesBeyondCoastBound_ReportsNull()
    {
        var tracker = new BallTracker(maxCoastFrames: 3);

        tracker.Update([Ball(100, 100, 110, 110, 0.9f)]);

        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(tracker.Update([]));
        }

        Assert.Null(tracker.Update([]));
    }

    [Fact]
    public void Update_DetectionAfterExceedingCoastBound_ImmediatelyResumesReporting()
    {
        var tracker = new BallTracker(maxCoastFrames: 2);

        tracker.Update([Ball(100, 100, 110, 110, 0.9f)]);

        // Exceed the coast bound - no ball reported.
        for (var i = 0; i < 3; i++)
        {
            tracker.Update([]);
        }

        // A single new detection should immediately resume reporting - no multi-frame confirmation delay,
        // unlike player tracking's confirmation gate (the ball has no identity to protect, only a position).
        var reacquired = tracker.Update([Ball(400, 400, 410, 410, 0.9f)]);

        Assert.NotNull(reacquired);
        Assert.Equal(400, reacquired!.Value.Left);
    }

    [Fact]
    public void PredictOnly_BeforeAnyDetection_ReturnsNull()
    {
        var tracker = new BallTracker();

        Assert.Null(tracker.PredictOnly());
    }

    [Fact]
    public void Reset_ClearsAcquiredState()
    {
        var tracker = new BallTracker(maxCoastFrames: 5);

        tracker.Update([Ball(100, 100, 110, 110, 0.9f)]);
        tracker.Reset();

        Assert.Null(tracker.PredictOnly());
    }
}
