namespace NBA.State.Tests;

public class GameStateTrackerTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToStatusDictionary_BeforeAnyUpdate_IsEmpty()
    {
        var tracker = new GameStateTracker();

        Assert.Empty(tracker.ToStatusDictionary());
    }

    [Fact]
    public void Update_WithNullReading_IsNoOp()
    {
        var tracker = new GameStateTracker();
        tracker.Update(new ScoreboardReading("LAL", "BOS", 65, 70, "5:32", 14, At));

        tracker.Update(null);

        Assert.Equal(70, tracker.Current!.AwayScore);
    }

    [Fact]
    public void Update_WithPartialReading_RetainsPreviousValuesForMissingFields()
    {
        var tracker = new GameStateTracker();
        tracker.Update(new ScoreboardReading("LAL", "BOS", 65, 70, "5:32", 14, At));

        // Next pass only recognized the clock - score/teams/shot clock weren't read this time, so they should
        // stick rather than flicker back to unknown.
        tracker.Update(new ScoreboardReading(null, null, null, null, "5:31", null, At.AddSeconds(1)));

        var current = tracker.Current!;
        Assert.Equal("LAL", current.HomeTeam);
        Assert.Equal("BOS", current.AwayTeam);
        Assert.Equal(65, current.HomeScore);
        Assert.Equal(70, current.AwayScore);
        Assert.Equal(14, current.ShotClock);
        Assert.Equal("5:31", current.GameClock); // the one field that *was* re-recognized does update
    }

    [Fact]
    public void ToStatusDictionary_WithFullReading_FormatsTeamsScoreClockAndShotClock()
    {
        var tracker = new GameStateTracker();
        tracker.Update(new ScoreboardReading("LAL", "BOS", 65, 70, "5:32", 14, At));

        var status = tracker.ToStatusDictionary();

        Assert.Equal("Boston Celtics @ Los Angeles Lakers", status["Teams"]);
        Assert.Equal("70 - 65", status["Score"]);
        Assert.Equal("5:32", status["Clock"]);
        Assert.Equal("14", status["Shot Clock"]);
    }

    [Fact]
    public void ToStatusDictionary_OmitsFieldsThatWereNeverRecognized()
    {
        var tracker = new GameStateTracker();
        tracker.Update(new ScoreboardReading(null, null, null, null, "5:32", null, At));

        var status = tracker.ToStatusDictionary();

        Assert.False(status.ContainsKey("Teams"));
        Assert.False(status.ContainsKey("Score"));
        Assert.False(status.ContainsKey("Shot Clock"));
        Assert.Equal("5:32", status["Clock"]);
    }
}
