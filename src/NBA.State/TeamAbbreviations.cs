namespace NBA.State;

/// <summary>
/// Static three-letter broadcast scoreboard code -&gt; full team name lookup for all 30 NBA teams - the only
/// place in NBA.State with basketball-specific domain knowledge. Used to both validate that a recognized token
/// really is a team code (not junk OCR output) and to render a friendly name in the status panel.
/// </summary>
public static class TeamAbbreviations
{
    public static IReadOnlyDictionary<string, string> Codes { get; } = new Dictionary<string, string>
    {
        ["ATL"] = "Atlanta Hawks",
        ["BOS"] = "Boston Celtics",
        ["BKN"] = "Brooklyn Nets",
        ["CHA"] = "Charlotte Hornets",
        ["CHI"] = "Chicago Bulls",
        ["CLE"] = "Cleveland Cavaliers",
        ["DAL"] = "Dallas Mavericks",
        ["DEN"] = "Denver Nuggets",
        ["DET"] = "Detroit Pistons",
        ["GSW"] = "Golden State Warriors",
        ["HOU"] = "Houston Rockets",
        ["IND"] = "Indiana Pacers",
        ["LAC"] = "LA Clippers",
        ["LAL"] = "Los Angeles Lakers",
        ["MEM"] = "Memphis Grizzlies",
        ["MIA"] = "Miami Heat",
        ["MIL"] = "Milwaukee Bucks",
        ["MIN"] = "Minnesota Timberwolves",
        ["NOP"] = "New Orleans Pelicans",
        ["NYK"] = "New York Knicks",
        ["OKC"] = "Oklahoma City Thunder",
        ["ORL"] = "Orlando Magic",
        ["PHI"] = "Philadelphia 76ers",
        ["PHX"] = "Phoenix Suns",
        ["POR"] = "Portland Trail Blazers",
        ["SAC"] = "Sacramento Kings",
        ["SAS"] = "San Antonio Spurs",
        ["TOR"] = "Toronto Raptors",
        ["UTA"] = "Utah Jazz",
        ["WAS"] = "Washington Wizards",
    };
}
