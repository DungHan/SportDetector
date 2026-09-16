namespace NBA.JerseyOcr;

/// <summary>
/// Turns a stream of per-frame, per-track recognition results into a stable resolved number per track - see
/// ocr/jersey-number-recognition's "Multi-frame voting resolves one stable number per track" requirement.
/// Mirrors <c>IPlayerTracker.Update</c>'s per-frame shape: called exactly once per processed frame.
/// </summary>
public interface IJerseyNumberVoteAggregator
{
    /// <summary>
    /// Accumulates <paramref name="frameResults"/> into each track's vote counts and returns every track whose
    /// current leading candidate has resolved (reached the implementation's vote threshold), keyed by track ID.
    /// A track ID absent from <paramref name="frameResults"/> has its accumulated state discarded - this is how
    /// track termination is detected, with no separate termination event needed.
    /// </summary>
    IReadOnlyDictionary<int, int> Update(IReadOnlyList<(int TrackId, JerseyNumberRecognitionResult Result)> frameResults);
}
