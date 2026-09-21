namespace NBA.Tracking.Tests;

public class Axis1DKalmanFilterTests
{
    [Fact]
    public void Predict_AfterSeveralCorrections_ExtrapolatesVelocity_NotJustLastMeasurement()
    {
        // Constant velocity of 2 units/frame-step: 0, 2, 4, 6, 8, ...
        var filter = new Axis1DKalmanFilter();

        double lastMeasurement = 0;
        for (var step = 0; step < 5; step++)
        {
            filter.Predict();
            lastMeasurement = step * 2;
            filter.Correct(lastMeasurement);
        }

        var predicted = filter.Predict();

        // A naive "hold last value" model would predict exactly lastMeasurement (8). A constant-velocity
        // filter that has picked up on the trend should predict meaningfully ahead of it, toward 10.
        Assert.True(predicted > lastMeasurement + 1.0,
            $"expected prediction well past the last measurement ({lastMeasurement}) toward the true next value (10), got {predicted}");
    }

    [Fact]
    public void Predict_ConvergesTowardTruePositionOverIterations()
    {
        // Constant velocity of 1 unit/frame-step, with a bit of measurement noise.
        var filter = new Axis1DKalmanFilter();
        var noisyOffsets = new[] { 0.3, -0.2, 0.25, -0.15, 0.1, -0.05, 0.08, -0.04 };

        double firstError = double.NaN;
        double lastError = double.NaN;

        for (var step = 0; step < noisyOffsets.Length; step++)
        {
            var truePosition = (double)step;
            var predicted = filter.Predict();
            var error = Math.Abs(predicted - truePosition);

            if (step == 1)
            {
                firstError = error;
            }

            lastError = error;

            filter.Correct(truePosition + noisyOffsets[step]);
        }

        Assert.True(lastError < firstError,
            $"expected prediction error to shrink as more observations arrive: first={firstError}, last={lastError}");
    }

    [Fact]
    public void Position_AfterCorrect_MatchesTheGainBlendedEstimate_WithoutAdvancing()
    {
        var filter = new Axis1DKalmanFilter();
        filter.Correct(0);

        // Predict() before this Correct() call already advanced the state to some prior estimate; Correct()
        // then blends that prior estimate with the new measurement (10) by the Kalman gain - somewhere between
        // the two, not equal to either. Position should report exactly that blended value, unchanged by simply
        // reading it back (no further Predict() call).
        var priorEstimate = filter.Predict();
        filter.Correct(10);
        var corrected = filter.Position;

        Assert.True(corrected > priorEstimate && corrected < 10,
            $"expected the corrected estimate ({corrected}) strictly between the prior prediction ({priorEstimate}) and the new measurement (10)");

        // Reading Position again (no Predict()/Correct() in between) must be stable.
        Assert.Equal(corrected, filter.Position);
    }
}
