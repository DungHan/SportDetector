using NBA.Vision;

namespace NBA.Tracking;

/// <summary>
/// Smooths per-frame <c>Ball</c>-class detections into a single filtered position, coasting through brief
/// missed-detection gaps - see tracking/ball-tracking spec. Exactly one of <see cref="Update"/> or
/// <see cref="PredictOnly"/> is called once per processed frame, depending on whether detection ran for that
/// frame, mirroring <see cref="IPlayerTracker"/>'s contract. Unlike <see cref="IPlayerTracker"/>, there is at
/// most one ball, so there is no association/identity concept - only a position, or none.
/// </summary>
public interface IBallTracker
{
    /// <summary>Updates the filtered ball position from <paramref name="detections"/> (only entries with <c>ClassName == "Ball"</c> are considered; if more than one, only the highest-confidence one is used) and returns the resulting smoothed/coasted position, or null if no position can currently be reported (never acquired, or coasted past the configured miss bound). Call only for a frame on which detection actually ran.</summary>
    BallPosition? Update(IReadOnlyList<OnCourtObjectDetection> detections);

    /// <summary>Advances the motion model by one frame-step without taking any new detections, returning the same coasted/null result <see cref="Update"/> would with zero <c>Ball</c> detections this attempt. Call for a frame on which detection did not run.</summary>
    BallPosition? PredictOnly();

    /// <summary>Forgets all filter state - used when switching to an unrelated capture source.</summary>
    void Reset();
}
