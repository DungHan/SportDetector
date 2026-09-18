using System.Text.Json;

namespace NBA.App.Services;

public sealed record SportClassifierConfig
{
    public string ModelPath { get; init; } = "sport-classifier_224_clip-vitb32.onnx";
    public string PromptsPath { get; init; } = "sport-classifier-prompts.clip.json";
    public int InputSize { get; init; } = 224;
}

public sealed record CourtKeypointsConfig
{
    public string ModelPath { get; init; } = "basketball_nba_court-keypoints_1280_yolov8s-pose.onnx";
    public int InputSize { get; init; } = 1280;
    public float KeypointConfidenceThreshold { get; init; } = 0.5f;
    public float DetectionConfidenceThreshold { get; init; } = 0.5f;
}

public sealed record PlayerDetectionConfig
{
    public static readonly IReadOnlyList<string> DefaultClassNames =
        ["Ball", "Hoop", "Period", "Player", "Ref", "Shot Clock", "Team Name", "Team Points", "Time Remaining"];

    public string ModelPath { get; init; } = "basketball_nba_player-detection_1280_yolov8m.onnx";
    public int InputSize { get; init; } = 1280;
    public IReadOnlyList<string> ClassNames { get; init; } = DefaultClassNames;
    public int PlayerClassId { get; init; } = 3;
    public float ConfidenceThreshold { get; init; } = 0.35f;
    public float IouThreshold { get; init; } = 0.6f;
}

public sealed record JerseyNumberConfig
{
    public string ModelPath { get; init; } = "jersey-number.onnx";
    public int InputSize { get; init; } = 64;
    public float ConfidenceThreshold { get; init; } = 0.5f;
}

/// <summary>
/// Per-model tunables tied to whichever exported ONNX file currently sits in <c>models/</c> - loaded from
/// <c>models/models.json</c> so swapping in a retrained/re-exported model (new resolution, new class list,
/// new tuned thresholds; see models/README.md's naming convention) only requires editing that file, not
/// <c>App.axaml.cs</c>. A missing file, missing sections, and missing individual fields all fall back to the
/// defaults declared on each record (today's previously-hardcoded values) - this is purely additive, so the
/// app behaves identically with no <c>models.json</c> present at all.
/// </summary>
public sealed record ModelsConfig
{
    public SportClassifierConfig SportClassifier { get; init; } = new();
    public CourtKeypointsConfig CourtKeypoints { get; init; } = new();
    public PlayerDetectionConfig PlayerDetection { get; init; } = new();
    public JerseyNumberConfig JerseyNumber { get; init; } = new();

    public static ModelsConfig LoadFromJsonOrDefault(string path)
    {
        if (!File.Exists(path))
        {
            return new ModelsConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ModelsConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ModelsConfig();
    }
}
