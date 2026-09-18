using System.Runtime.InteropServices;
using System.Text.Json;
using NBA.Vision;
using OpenCvSharp;

// Quick, standalone "drop an image in, see the result" harness for whatever ONNX models currently sit in
// models/ - it runs the exact same detector classes (OnnxCourtKeypointDetector, OnnxMultiClassObjectDetector,
// ClipZeroShotSportClassifier) and the same models/models.json tunables that NBA.App wires up, so a result seen
// here matches what the real app would show for the same frame. Not part of the shipped app - see
// tools/clip-text-encoder for the other "offline-only" precedent in this repo.

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        Usage: dotnet run --project tools/model-preview -- <image> [--models-dir <dir>] [--out <path>]

          <image>         Path to a jpg/png/etc. frame to run every configured model against.
          --models-dir    Directory containing models.json + the .onnx files (default: nearest 'models'
                           folder found by walking up from the current directory).
          --out           Where to write the annotated result (default: <image>.preview.png next to the input).
        """);
    return args.Length == 0 ? 1 : 0;
}

var imagePath = args[0];
if (!File.Exists(imagePath))
{
    Console.Error.WriteLine($"Image not found: {imagePath}");
    return 1;
}

var modelsDir = GetOption(args, "--models-dir") ?? FindModelsDirectory()
    ?? throw new InvalidOperationException(
        "Could not find a 'models' directory (containing models.json) by walking up from the current " +
        "directory. Pass --models-dir explicitly.");

var outPath = GetOption(args, "--out") ?? Path.Combine(
    Path.GetDirectoryName(Path.GetFullPath(imagePath)) ?? ".",
    Path.GetFileNameWithoutExtension(imagePath) + ".preview.png");

Console.WriteLine($"models dir : {modelsDir}");

var config = ModelPreviewConfig.LoadFromJsonOrDefault(Path.Combine(modelsDir, "models.json"));

using var source = Cv2.ImRead(imagePath, ImreadModes.Color);
if (source.Empty())
{
    Console.Error.WriteLine($"Failed to decode image: {imagePath}");
    return 1;
}

using var bgra = new Mat();
Cv2.CvtColor(source, bgra, ColorConversionCodes.BGR2BGRA);
var stride = (int)bgra.Step();
var pixels = new byte[stride * bgra.Rows];
Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
var width = bgra.Cols;
var height = bgra.Rows;

using var draw = source.Clone();
var textY = 28;

// --- Sport classification ---
var sportModelPath = Path.Combine(modelsDir, config.SportClassifier.ModelPath);
var sportPromptsPath = Path.Combine(modelsDir, config.SportClassifier.PromptsPath);
if (File.Exists(sportModelPath) && File.Exists(sportPromptsPath))
{
    using var classifier = new ClipZeroShotSportClassifier(
        sportModelPath,
        ClipPromptEmbeddings.LoadFromJson(sportPromptsPath),
        inputSize: config.SportClassifier.InputSize);

    var result = classifier.Classify(pixels, width, height, stride);
    Console.WriteLine($"sport      : {result.Sport} ({result.Confidence:P1})");
    DrawLabel(draw, $"sport: {result.Sport} ({result.Confidence:P1})", 10, textY, Scalar.Yellow);
    textY += 26;
}
else
{
    Console.WriteLine("sport      : skipped (model or prompts file missing)");
}

// --- Court keypoints ---
var keypointModelPath = Path.Combine(modelsDir, config.CourtKeypoints.ModelPath);
if (File.Exists(keypointModelPath) && CourtGeometryRegistry.TryGet(SportType.Basketball, out var geometry))
{
    using var keypointDetector = new OnnxCourtKeypointDetector(
        keypointModelPath,
        geometry,
        inputSize: config.CourtKeypoints.InputSize,
        keypointConfidenceThreshold: config.CourtKeypoints.KeypointConfidenceThreshold,
        detectionConfidenceThreshold: config.CourtKeypoints.DetectionConfidenceThreshold);

    var keypoints = keypointDetector.Detect(pixels, width, height, stride);
    Console.WriteLine($"keypoints  : {keypoints.Count} detected");
    foreach (var kp in keypoints)
    {
        Console.WriteLine($"  {kp.LandmarkName,-24} ({kp.Position.X:F0}, {kp.Position.Y:F0})  {kp.Confidence:P0}");
        Cv2.Circle(draw, (int)kp.Position.X, (int)kp.Position.Y, 6, Scalar.LimeGreen, thickness: -1);
        DrawLabel(draw, kp.LandmarkName, (int)kp.Position.X + 8, (int)kp.Position.Y - 8, Scalar.LimeGreen, scale: 0.4);
    }
}
else
{
    Console.WriteLine("keypoints  : skipped (model missing)");
}

// --- Player / on-court object detection ---
var playerModelPath = Path.Combine(modelsDir, config.PlayerDetection.ModelPath);
if (File.Exists(playerModelPath))
{
    using var objectDetector = new OnnxMultiClassObjectDetector(
        playerModelPath,
        config.PlayerDetection.ClassNames,
        inputSize: config.PlayerDetection.InputSize,
        confidenceThreshold: config.PlayerDetection.ConfidenceThreshold,
        iouThreshold: config.PlayerDetection.IouThreshold,
        playerClassId: config.PlayerDetection.PlayerClassId);

    var detections = objectDetector.Detect(pixels, width, height, stride);
    Console.WriteLine($"players    : {detections.Players.Count} detected");
    foreach (var p in detections.Players)
    {
        Console.WriteLine($"  Player  ({p.Left:F0},{p.Top:F0})-({p.Right:F0},{p.Bottom:F0})  {p.Confidence:P0}");
        DrawBox(draw, p.Left, p.Top, p.Right, p.Bottom, $"Player {p.Confidence:P0}", Scalar.OrangeRed);
    }

    Console.WriteLine($"objects    : {detections.Others.Count} detected");
    foreach (var o in detections.Others)
    {
        Console.WriteLine($"  {o.ClassName,-14} ({o.Left:F0},{o.Top:F0})-({o.Right:F0},{o.Bottom:F0})  {o.Confidence:P0}");
        DrawBox(draw, o.Left, o.Top, o.Right, o.Bottom, $"{o.ClassName} {o.Confidence:P0}", Scalar.DeepSkyBlue);
    }
}
else
{
    Console.WriteLine("players    : skipped (model missing)");
}

Cv2.ImWrite(outPath, draw);
Console.WriteLine($"wrote      : {outPath}");
return 0;

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string? FindModelsDirectory()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "models", "models.json");
        if (File.Exists(candidate))
        {
            return Path.Combine(dir.FullName, "models");
        }

        dir = dir.Parent;
    }

    return null;
}

static void DrawBox(Mat mat, double left, double top, double right, double bottom, string label, Scalar color)
{
    Cv2.Rectangle(mat, new Point(left, top), new Point(right, bottom), color, thickness: 2);
    DrawLabel(mat, label, (int)left, (int)top - 6, color);
}

static void DrawLabel(Mat mat, string text, int x, int y, Scalar color, double scale = 0.55)
{
    Cv2.PutText(mat, text, new Point(x, Math.Max(y, 12)), HersheyFonts.HersheySimplex, scale, color, thickness: 1, lineType: LineTypes.AntiAlias);
}

/// <summary>Trimmed local copy of NBA.App.Services.ModelsConfig's schema - this tool intentionally doesn't
/// reference NBA.App (which drags in Avalonia) just to read a handful of JSON fields.</summary>
internal sealed record SportClassifierPreviewConfig
{
    public string ModelPath { get; init; } = "sport-classifier_224_clip-vitb32.onnx";
    public string PromptsPath { get; init; } = "sport-classifier-prompts.clip.json";
    public int InputSize { get; init; } = 224;
}

internal sealed record CourtKeypointsPreviewConfig
{
    public string ModelPath { get; init; } = "basketball_nba_court-keypoints_1280_yolov8s-pose.onnx";
    public int InputSize { get; init; } = 1280;
    public float KeypointConfidenceThreshold { get; init; } = 0.5f;
    public float DetectionConfidenceThreshold { get; init; } = 0.5f;
}

internal sealed record PlayerDetectionPreviewConfig
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

internal sealed record ModelPreviewConfig
{
    public SportClassifierPreviewConfig SportClassifier { get; init; } = new();
    public CourtKeypointsPreviewConfig CourtKeypoints { get; init; } = new();
    public PlayerDetectionPreviewConfig PlayerDetection { get; init; } = new();

    public static ModelPreviewConfig LoadFromJsonOrDefault(string path)
    {
        if (!File.Exists(path))
        {
            return new ModelPreviewConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ModelPreviewConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new ModelPreviewConfig();
    }
}
