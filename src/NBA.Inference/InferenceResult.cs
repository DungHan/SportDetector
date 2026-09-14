namespace NBA.Inference;

/// <summary>A pipeline's structured output for one frame, paired with how long inference took to produce it.</summary>
public sealed class InferenceResult<TOutput>
{
    public required TOutput Output { get; init; }

    public required TimeSpan Latency { get; init; }
}
