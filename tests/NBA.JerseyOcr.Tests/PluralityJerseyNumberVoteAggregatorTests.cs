namespace NBA.JerseyOcr.Tests;

public class PluralityJerseyNumberVoteAggregatorTests
{
    private static (int TrackId, JerseyNumberRecognitionResult Result) Vote(int trackId, int? number) =>
        (trackId, new JerseyNumberRecognitionResult(number, Confidence: 0.9f));

    [Fact]
    public void Update_FewerVotesThanThreshold_ReportsNoResolvedNumber()
    {
        var aggregator = new PluralityJerseyNumberVoteAggregator(minVotes: 5);

        IReadOnlyDictionary<int, int> result = new Dictionary<int, int>();
        for (var i = 0; i < 4; i++)
        {
            result = aggregator.Update([Vote(1, 7)]);
        }

        Assert.Empty(result);
    }

    [Fact]
    public void Update_VotesSplitAcrossCandidates_ReportsNoResolvedNumberEvenAfterManyFrames()
    {
        var aggregator = new PluralityJerseyNumberVoteAggregator(minVotes: 5);

        IReadOnlyDictionary<int, int> result = new Dictionary<int, int>();
        for (var i = 0; i < 8; i++)
        {
            result = aggregator.Update([Vote(1, i % 2 == 0 ? 7 : 9)]);
        }

        // 4 votes for 7, 4 votes for 9 - neither reaches minVotes (5).
        Assert.Empty(result);
    }

    [Fact]
    public void Update_OneCandidateReachesThreshold_ResolvesToThatCandidate()
    {
        var aggregator = new PluralityJerseyNumberVoteAggregator(minVotes: 5);

        IReadOnlyDictionary<int, int> result = new Dictionary<int, int>();
        for (var i = 0; i < 5; i++)
        {
            result = aggregator.Update([Vote(1, 7)]);
        }

        Assert.Equal(7, result[1]);
    }

    [Fact]
    public void Update_ResolvedNumber_IsNotDisplacedByASingleDisagreeingFrame()
    {
        var aggregator = new PluralityJerseyNumberVoteAggregator(minVotes: 5);
        for (var i = 0; i < 5; i++)
        {
            aggregator.Update([Vote(1, 7)]);
        }

        var result = aggregator.Update([Vote(1, 9)]); // one disagreeing frame: 7 has 5 votes, 9 has 1

        Assert.Equal(7, result[1]);
    }

    [Fact]
    public void Update_DisagreeingCandidateOvertakesResolvedNumber_ResolvesToNewCandidate()
    {
        var aggregator = new PluralityJerseyNumberVoteAggregator(minVotes: 5);
        for (var i = 0; i < 5; i++)
        {
            aggregator.Update([Vote(1, 7)]); // 7: 5 votes
        }

        IReadOnlyDictionary<int, int> result = new Dictionary<int, int>();
        for (var i = 0; i < 6; i++)
        {
            result = aggregator.Update([Vote(1, 9)]); // 9: 6 votes, overtakes 7's 5
        }

        Assert.Equal(9, result[1]);
    }

    [Fact]
    public void Update_TrackAbsentFromLaterCall_DiscardsItsAccumulatedVotes()
    {
        var aggregator = new PluralityJerseyNumberVoteAggregator(minVotes: 5);
        for (var i = 0; i < 5; i++)
        {
            aggregator.Update([Vote(1, 7)]);
        }

        aggregator.Update([]); // track 1 absent - terminated, its votes are discarded

        // A later call reusing the same track ID (an unrelated new track, not real ID reuse) must start from
        // zero rather than resuming the discarded count of 5.
        IReadOnlyDictionary<int, int> result = new Dictionary<int, int>();
        for (var i = 0; i < 4; i++)
        {
            result = aggregator.Update([Vote(1, 7)]);
        }

        Assert.Empty(result);
    }
}
