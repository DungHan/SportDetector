using NBA.Vision;

namespace NBA.Tracking;

/// <summary>
/// Sole <see cref="IBallTracker"/> implementation - a single-object counterpart to
/// <see cref="ByteTrackPlayerTracker"/>, minus multi-track association (there is at most one ball, so no IoU
/// matching, confidence tiers, or identity to protect). Reuses <see cref="BoxMotionPredictor"/> directly: each
/// <see cref="Update"/> call with a detection reports the Kalman-smoothed box exactly like
/// <see cref="ByteTrackPlayerTracker.ApplyMatch"/> does for a player track. While unmatched, coasts on the pure
/// motion-predicted box for up to <paramref name="maxCoastFrames"/> consecutive misses (see
/// tracking/ball-tracking spec's "coast through brief missed detections") before reporting null. Once the coast
/// bound is exceeded, the next matched detection discards the old (now stale/high-covariance) predictor and
/// starts a fresh one - so the filter's first <c>Correct</c> call initializes directly to the new measurement
/// per <see cref="Axis1DKalmanFilter"/>'s own documented first-call behavior, rather than blending against a
/// long-stale prior (see tracking/ball-tracking spec's "re-acquire immediately, no confirmation delay").
/// </summary>
public sealed class BallTracker(int maxCoastFrames = 5) : IBallTracker
{
    private BoxMotionPredictor _predictor = new();
    private bool _isAcquired;
    private int _missedFrames;
    private float _lastConfidence;

    public BallPosition? Update(IReadOnlyList<OnCourtObjectDetection> detections)
    {
        var predictedBox = _predictor.Predict();

        OnCourtObjectDetection? best = null;
        foreach (var detection in detections)
        {
            if (detection.ClassName != "Ball")
            {
                continue;
            }

            if (best is null || detection.Confidence > best.Value.Confidence)
            {
                best = detection;
            }
        }

        if (best is { } detected)
        {
            if (!_isAcquired && _missedFrames > maxCoastFrames)
            {
                // Coast bound was already exceeded before this match - start a fresh filter so the first
                // Correct() call below initializes directly to this measurement instead of blending against a
                // long-stale, high-covariance prior.
                _predictor = new BoxMotionPredictor();
            }

            var box = (detected.Left, detected.Top, detected.Right, detected.Bottom);
            var corrected = _predictor.Correct(box);
            _isAcquired = true;
            _missedFrames = 0;
            _lastConfidence = detected.Confidence;
            return ToPosition(corrected);
        }

        return ReportMissedFrame(predictedBox);
    }

    public BallPosition? PredictOnly()
    {
        var predictedBox = _predictor.Predict();
        return ReportMissedFrame(predictedBox);
    }

    private BallPosition? ReportMissedFrame((double Left, double Top, double Right, double Bottom) predictedBox)
    {
        if (!_isAcquired)
        {
            return null;
        }

        _missedFrames++;
        if (_missedFrames > maxCoastFrames)
        {
            _isAcquired = false;
            return null;
        }

        return ToPosition(predictedBox);
    }

    private BallPosition ToPosition((double Left, double Top, double Right, double Bottom) box) =>
        new(box.Left, box.Top, box.Right, box.Bottom, _lastConfidence);

    public void Reset()
    {
        _predictor = new BoxMotionPredictor();
        _isAcquired = false;
        _missedFrames = 0;
        _lastConfidence = 0;
    }
}
