namespace NBA.State;

/// <summary>
/// In-memory current game state, updated ~1Hz from <see cref="ScoreboardTextParser"/> output. Deliberately not
/// persisted via ISourceProfileStore (unlike SourceProfile's Sport/Calibration in NBA.Vision) - score/clock
/// change constantly within a single viewing session and have no reason to survive a source re-selection or
/// app restart, unlike those per-source stable facts.
/// </summary>
public sealed class GameStateTracker
{
    public ScoreboardReading? Current { get; private set; }

    /// <summary>
    /// Merges a new (possibly partial) reading over the last known-good state - a field that wasn't recognized
    /// this pass keeps its previous value rather than blanking out, so a single missed OCR pass doesn't flicker
    /// the display back to placeholder text. A null <paramref name="reading"/> (nothing recognized at all) is a
    /// no-op.
    /// </summary>
    public void Update(ScoreboardReading? reading)
    {
        if (reading is null)
        {
            return;
        }

        Current = new ScoreboardReading(
            reading.HomeTeam ?? Current?.HomeTeam,
            reading.AwayTeam ?? Current?.AwayTeam,
            reading.HomeScore ?? Current?.HomeScore,
            reading.AwayScore ?? Current?.AwayScore,
            reading.GameClock ?? Current?.GameClock,
            reading.ShotClock ?? Current?.ShotClock,
            reading.RecognizedAt);
    }

    /// <summary>Projects the current state for <c>MinimapViewModel.SetStatusInfo</c> - empty until any field has ever been recognized.</summary>
    public IReadOnlyDictionary<string, string> ToStatusDictionary()
    {
        var result = new Dictionary<string, string>();
        if (Current is not { } current)
        {
            return result;
        }

        if (current.AwayTeam is not null || current.HomeTeam is not null)
        {
            result["Teams"] = $"{DisplayName(current.AwayTeam)} @ {DisplayName(current.HomeTeam)}";
        }

        if (current.AwayScore is not null || current.HomeScore is not null)
        {
            result["Score"] = $"{current.AwayScore?.ToString() ?? "-"} - {current.HomeScore?.ToString() ?? "-"}";
        }

        if (current.GameClock is not null)
        {
            result["Clock"] = current.GameClock;
        }

        if (current.ShotClock is not null)
        {
            result["Shot Clock"] = current.ShotClock.Value.ToString();
        }

        return result;
    }

    private static string DisplayName(string? teamCode) =>
        teamCode is not null && TeamAbbreviations.Codes.TryGetValue(teamCode, out var name) ? name : teamCode ?? "?";
}
