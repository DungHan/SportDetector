namespace NBA.State;

/// <summary>
/// Best-effort structured read of a broadcast scoreboard at one point in time. Every field is nullable and set
/// to null when it wasn't confidently recognized this pass, rather than guessing - see
/// <see cref="GameStateTracker"/> for how a null field is handled against the previous reading.
/// </summary>
public sealed record ScoreboardReading(
    string? HomeTeam,
    string? AwayTeam,
    int? HomeScore,
    int? AwayScore,
    string? GameClock,
    int? ShotClock,
    DateTimeOffset RecognizedAt);
