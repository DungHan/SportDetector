namespace NBA.Tracking.Tests;

public class GreedyIouMatcherTests
{
    [Fact]
    public void Match_OverlappingPairAboveThreshold_Matches()
    {
        var predicted = new[] { (0.0, 0.0, 10.0, 10.0) };
        var detections = new[] { (1.0, 1.0, 11.0, 11.0) }; // heavily overlapping

        var result = GreedyIouMatcher.Match(predicted, detections, iouThreshold: 0.3);

        var match = Assert.Single(result.Matches);
        Assert.Equal(0, match.PredictedIndex);
        Assert.Equal(0, match.DetectionIndex);
        Assert.Empty(result.UnmatchedPredicted);
        Assert.Empty(result.UnmatchedDetections);
    }

    [Fact]
    public void Match_PairBelowThreshold_LeftUnmatched()
    {
        var predicted = new[] { (0.0, 0.0, 10.0, 10.0) };
        var detections = new[] { (50.0, 50.0, 60.0, 60.0) }; // no overlap at all

        var result = GreedyIouMatcher.Match(predicted, detections, iouThreshold: 0.3);

        Assert.Empty(result.Matches);
        Assert.Equal([0], result.UnmatchedPredicted);
        Assert.Equal([0], result.UnmatchedDetections);
    }

    [Fact]
    public void Match_OnePredictedTwoCandidateDetections_KeepsOnlyHigherIouPair()
    {
        var predicted = new[] { (0.0, 0.0, 10.0, 10.0) };
        var detections = new[]
        {
            (2.0, 2.0, 12.0, 12.0), // lower IoU with predicted[0]
            (0.5, 0.5, 10.5, 10.5), // higher IoU with predicted[0]
        };

        var result = GreedyIouMatcher.Match(predicted, detections, iouThreshold: 0.1);

        var match = Assert.Single(result.Matches);
        Assert.Equal(0, match.PredictedIndex);
        Assert.Equal(1, match.DetectionIndex); // the higher-IoU detection wins
        Assert.Empty(result.UnmatchedPredicted);
        Assert.Equal([0], result.UnmatchedDetections); // the lower-IoU detection stays unmatched
    }
}
