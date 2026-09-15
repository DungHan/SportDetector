namespace NBA.OCR;

/// <summary>One line of text recognized within a cropped scoreboard region, with the engine's own confidence score.</summary>
public readonly record struct ScoreboardOcrLine(string Text, float Confidence);
