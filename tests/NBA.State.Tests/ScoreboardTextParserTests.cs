using NBA.OCR;

namespace NBA.State.Tests;

public class ScoreboardTextParserTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ScoreboardOcrLine[] Lines(params string[] texts) =>
        texts.Select(t => new ScoreboardOcrLine(t, Confidence: 1f)).ToArray();

    [Fact]
    public void Parse_WithNoLines_ReturnsNull()
    {
        Assert.Null(ScoreboardTextParser.Parse([], At));
    }

    [Fact]
    public void Parse_WithNoRecognizableFields_ReturnsNull()
    {
        // Garbage OCR output with no team codes, no score-shaped numbers, no clock - nothing worth reporting.
        var reading = ScoreboardTextParser.Parse(Lines("xyz abc"), At);

        Assert.Null(reading);
    }

    [Fact]
    public void Parse_SingleLine_WithTeamsScoreAndClock_ExtractsAllFields()
    {
        // Real output captured from the macOS Vision helper against a rendered "BOS 70 - 65 LAL  5:32" image.
        var reading = ScoreboardTextParser.Parse(Lines("BOS 70 - 65 LAL 5:32"), At);

        Assert.NotNull(reading);
        Assert.Equal("BOS", reading!.AwayTeam);
        Assert.Equal("LAL", reading.HomeTeam);
        Assert.Equal(70, reading.AwayScore);
        Assert.Equal(65, reading.HomeScore);
        Assert.Equal("5:32", reading.GameClock);
        Assert.Null(reading.ShotClock);
        Assert.Equal(At, reading.RecognizedAt);
    }

    [Fact]
    public void Parse_MultipleLines_AreJoinedBeforeTokenizing()
    {
        var reading = ScoreboardTextParser.Parse(Lines("BOS 70", "LAL 65", "Q3 5:32"), At);

        Assert.NotNull(reading);
        Assert.Equal("BOS", reading!.AwayTeam);
        Assert.Equal("LAL", reading.HomeTeam);
        Assert.Equal(70, reading.AwayScore);
        Assert.Equal(65, reading.HomeScore);
        Assert.Equal("5:32", reading.GameClock);
    }

    [Fact]
    public void Parse_WithShotClockToken_DistinctFromScorePair_ExtractsShotClock()
    {
        var reading = ScoreboardTextParser.Parse(Lines("BOS 70 - 65 LAL 5:32 14"), At);

        Assert.NotNull(reading);
        Assert.Equal(14, reading!.ShotClock);
        Assert.Equal(70, reading.AwayScore);
        Assert.Equal(65, reading.HomeScore);
    }

    [Fact]
    public void Parse_TeamCodeIsCaseInsensitive()
    {
        var reading = ScoreboardTextParser.Parse(Lines("bos 70 - 65 lal"), At);

        Assert.NotNull(reading);
        Assert.Equal("BOS", reading!.AwayTeam);
        Assert.Equal("LAL", reading.HomeTeam);
    }

    [Fact]
    public void Parse_UnknownThreeLetterToken_IsNotTreatedAsATeam()
    {
        var reading = ScoreboardTextParser.Parse(Lines("XXX 70 - 65 YYY"), At);

        Assert.NotNull(reading); // still recognizes the score
        Assert.Null(reading!.AwayTeam);
        Assert.Null(reading.HomeTeam);
        Assert.Equal(70, reading.AwayScore);
        Assert.Equal(65, reading.HomeScore);
    }

    [Fact]
    public void Parse_OnlyClockRecognized_StillReturnsPartialReading()
    {
        var reading = ScoreboardTextParser.Parse(Lines("some garbage 5:32 more garbage"), At);

        Assert.NotNull(reading);
        Assert.Equal("5:32", reading!.GameClock);
        Assert.Null(reading.AwayTeam);
        Assert.Null(reading.AwayScore);
    }
}
