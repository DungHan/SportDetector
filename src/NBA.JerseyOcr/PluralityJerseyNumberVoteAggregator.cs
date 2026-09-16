namespace NBA.JerseyOcr;

/// <summary>
/// Plurality-with-threshold vote aggregator (design.md's "recomputed fresh from accumulated counts each call,
/// not a one-shot lock-in" decision): each track's displayed number is always the current argmax of its
/// accumulated votes, once that candidate's count reaches <paramref name="minVotes"/> - so a later candidate
/// naturally displaces the resolved one only once it actually out-votes it, with no extra state needed.
/// </summary>
public sealed class PluralityJerseyNumberVoteAggregator(int minVotes = 5) : IJerseyNumberVoteAggregator
{
    private readonly Dictionary<int, Dictionary<int, int>> _voteCounts = [];

    public IReadOnlyDictionary<int, int> Update(IReadOnlyList<(int TrackId, JerseyNumberRecognitionResult Result)> frameResults)
    {
        var liveTrackIds = new HashSet<int>();
        foreach (var (trackId, result) in frameResults)
        {
            liveTrackIds.Add(trackId);
            if (result.Number is not { } number)
            {
                continue;
            }

            if (!_voteCounts.TryGetValue(trackId, out var counts))
            {
                counts = [];
                _voteCounts[trackId] = counts;
            }

            counts[number] = counts.GetValueOrDefault(number) + 1;
        }

        // A track ID absent from this frame's results is the only termination signal (no explicit
        // IPlayerTracker termination event exists) - discard its accumulated votes entirely.
        foreach (var terminatedTrackId in _voteCounts.Keys.Where(trackId => !liveTrackIds.Contains(trackId)).ToList())
        {
            _voteCounts.Remove(terminatedTrackId);
        }

        var resolved = new Dictionary<int, int>();
        foreach (var (trackId, counts) in _voteCounts)
        {
            var leader = counts.MaxBy(candidate => candidate.Value);
            if (leader.Value >= minVotes)
            {
                resolved[trackId] = leader.Key;
            }
        }

        return resolved;
    }
}
