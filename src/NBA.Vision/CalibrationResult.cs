namespace NBA.Vision;

public sealed class CalibrationResult
{
    public required bool Success { get; init; }

    public CalibrationData? Calibration { get; init; }

    public string? FailureReason { get; init; }

    public static CalibrationResult Ok(CalibrationData calibration) => new() { Success = true, Calibration = calibration };

    public static CalibrationResult Fail(string reason) => new() { Success = false, FailureReason = reason };
}
