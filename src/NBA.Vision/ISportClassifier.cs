namespace NBA.Vision;

public readonly record struct SportClassifierOutput(SportType Sport, float Confidence);

/// <summary>Raw sport classification for a single frame - no confidence-threshold or registry-support logic, just the model's answer.</summary>
public interface ISportClassifier
{
    SportClassifierOutput Classify(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride);
}
