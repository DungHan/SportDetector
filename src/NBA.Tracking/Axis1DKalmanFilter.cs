namespace NBA.Tracking;

/// <summary>
/// A minimal constant-velocity Kalman filter over a single scalar axis (state: position + velocity), advancing
/// one discrete frame-step per <see cref="Predict"/> call rather than modeling wall-clock time - see
/// design.md's "four independent 1D Kalman filters, not one coupled 8D filter" decision. Closed-form 2x2
/// covariance math only, no matrix-library dependency.
/// </summary>
public sealed class Axis1DKalmanFilter(double processNoise = 1e-2, double measurementNoise = 1.0)
{
    private double _position;
    private double _velocity;
    private double _p00 = 1, _p01, _p11 = 1;
    private bool _initialized;

    /// <summary>Advances the state by one frame-step (constant-velocity extrapolation) and returns the predicted position. A no-op returning 0 until the first <see cref="Correct"/> call.</summary>
    public double Predict()
    {
        if (!_initialized)
        {
            return _position;
        }

        _position += _velocity;

        // Covariance propagation P' = F P F^T + Q for F = [[1, 1], [0, 1]] (dt = 1 frame-step).
        var p00 = _p00 + (2 * _p01) + _p11 + processNoise;
        var p01 = _p01 + _p11;
        var p11 = _p11 + processNoise;
        (_p00, _p01, _p11) = (p00, p01, p11);

        return _position;
    }

    /// <summary>Feeds back an observed value, correcting the position/velocity estimate. The first call initializes the filter's position directly (zero initial velocity, no prior state to blend with).</summary>
    public void Correct(double measurement)
    {
        if (!_initialized)
        {
            _position = measurement;
            _velocity = 0;
            _initialized = true;
            return;
        }

        var innovation = measurement - _position;
        var s = _p00 + measurementNoise;
        var k0 = _p00 / s;
        var k1 = _p01 / s;

        _position += k0 * innovation;
        _velocity += k1 * innovation;

        var p00 = (1 - k0) * _p00;
        var p01 = (1 - k0) * _p01;
        var p11 = _p11 - (k1 * _p01);
        (_p00, _p01, _p11) = (p00, p01, p11);
    }
}
