namespace NBA.Tracking;

/// <summary>
/// Predicts a tracked box's motion one frame-step at a time using four independent <see cref="Axis1DKalmanFilter"/>s
/// (box center-x, center-y, width, height) - see design.md's motion-model decision. Translates between the
/// (Left, Top, Right, Bottom) box shape every other type in this codebase uses and the (cx, cy, w, h) shape the
/// underlying filters operate on.
/// </summary>
public sealed class BoxMotionPredictor
{
    private readonly Axis1DKalmanFilter _cx = new();
    private readonly Axis1DKalmanFilter _cy = new();
    private readonly Axis1DKalmanFilter _w = new();
    private readonly Axis1DKalmanFilter _h = new();

    /// <summary>Advances the motion model by one frame-step and returns the predicted box.</summary>
    public (double Left, double Top, double Right, double Bottom) Predict()
    {
        var cx = _cx.Predict();
        var cy = _cy.Predict();
        var w = _w.Predict();
        var h = _h.Predict();

        return (cx - (w / 2), cy - (h / 2), cx + (w / 2), cy + (h / 2));
    }

    /// <summary>Feeds back an observed box, correcting the motion model's state.</summary>
    public void Correct((double Left, double Top, double Right, double Bottom) box)
    {
        _cx.Correct((box.Left + box.Right) / 2);
        _cy.Correct((box.Top + box.Bottom) / 2);
        _w.Correct(box.Right - box.Left);
        _h.Correct(box.Bottom - box.Top);
    }
}
