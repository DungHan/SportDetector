namespace NBA.JerseyOcr;

/// <summary>One recognition attempt's outcome for a single tracked player, for a single frame.</summary>
public readonly record struct JerseyNumberRecognitionResult(int? Number, float Confidence);
