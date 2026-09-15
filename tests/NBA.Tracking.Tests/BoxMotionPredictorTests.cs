namespace NBA.Tracking.Tests;

public class BoxMotionPredictorTests
{
    [Fact]
    public void Predict_TracksConstantVelocityBox_NotStationary()
    {
        // A 10x20 box moving 5 units/frame-step to the right, starting at (0, 0)-(10, 20).
        var predictor = new BoxMotionPredictor();

        (double Left, double Top, double Right, double Bottom) BoxAt(int step) =>
            (step * 5.0, 0.0, (step * 5.0) + 10.0, 20.0);

        for (var step = 0; step < 5; step++)
        {
            predictor.Predict();
            predictor.Correct(BoxAt(step));
        }

        var predicted = predictor.Predict();
        var lastObserved = BoxAt(4);

        // A stationary/naive model would predict the same box as the last observation. A motion-aware
        // predictor should have picked up the constant rightward velocity and predict further right.
        Assert.True(predicted.Left > lastObserved.Left + 1.0,
            $"expected predicted box to move past the last observed box (Left={lastObserved.Left}), got Left={predicted.Left}");

        // Size should stay stable (~10 wide, ~20 tall) since width/height aren't changing in this sequence.
        Assert.Equal(10.0, predicted.Right - predicted.Left, precision: 0);
        Assert.Equal(20.0, predicted.Bottom - predicted.Top, precision: 0);
    }
}
