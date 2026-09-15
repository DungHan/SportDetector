using System.Text.RegularExpressions;
using NBA.OCR;

namespace NBA.State;

/// <summary>
/// Turns raw OCR text (NBA.OCR's <see cref="ScoreboardOcrLine"/> output) into a structured
/// <see cref="ScoreboardReading"/>. Pure/deterministic - easy to unit test with fixed text fixtures, no OS or
/// model dependency. Broadcast scoreboards vary a lot between networks, so this is a best-effort heuristic, not
/// a guaranteed-correct parse: it looks for two known team codes, an adjacent pair of small integers for the
/// score, an "M:SS" token for the game clock, and a standalone 0-24 integer (not already used by the score) for
/// the shot clock.
/// </summary>
public static partial class ScoreboardTextParser
{
    [GeneratedRegex(@"^\d{1,2}:\d{2}$")]
    private static partial Regex GameClockPattern();

    public static ScoreboardReading? Parse(IReadOnlyList<ScoreboardOcrLine> lines, DateTimeOffset recognizedAt)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        var tokens = lines
            .SelectMany(line => line.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        // Broadcast scoreboards conventionally list the visiting team first (left-to-right) - not verified
        // across networks/eras, so treat this as a heuristic rather than a guaranteed home/away mapping.
        var teamCodes = tokens
            .Select(t => t.ToUpperInvariant())
            .Where(t => TeamAbbreviations.Codes.ContainsKey(t))
            .Distinct()
            .Take(2)
            .ToArray();
        var awayTeam = teamCodes.Length > 0 ? teamCodes[0] : null;
        var homeTeam = teamCodes.Length > 1 ? teamCodes[1] : null;

        var gameClock = tokens.FirstOrDefault(t => GameClockPattern().IsMatch(t));

        var numericTokens = tokens
            .Select(t => int.TryParse(t.Trim(',', '.'), out var n) ? n : (int?)null)
            .Where(n => n is not null)
            .Select(n => n!.Value)
            .ToArray();

        int? awayScore = null;
        int? homeScore = null;
        var scorePairStartIndex = -1;
        for (var i = 0; i < numericTokens.Length - 1; i++)
        {
            if (numericTokens[i] is >= 0 and <= 200 && numericTokens[i + 1] is >= 0 and <= 200)
            {
                awayScore = numericTokens[i];
                homeScore = numericTokens[i + 1];
                scorePairStartIndex = i;
                break;
            }
        }

        int? shotClock = null;
        for (var i = 0; i < numericTokens.Length; i++)
        {
            if (i == scorePairStartIndex || i == scorePairStartIndex + 1)
            {
                continue;
            }

            if (numericTokens[i] is >= 0 and <= 24)
            {
                shotClock = numericTokens[i];
                break;
            }
        }

        if (awayTeam is null && homeTeam is null && gameClock is null && awayScore is null)
        {
            return null;
        }

        return new ScoreboardReading(homeTeam, awayTeam, homeScore, awayScore, gameClock, shotClock, recognizedAt);
    }
}
