using NBA.App.Services;

namespace NBA.App.Tests;

public class ModelsConfigTests
{
    [Fact]
    public void LoadFromJsonOrDefault_MissingFile_ReturnsAllDefaults()
    {
        var config = ModelsConfig.LoadFromJsonOrDefault(Path.Combine(Path.GetTempPath(), $"models-{Guid.NewGuid():N}.json"));

        Assert.Equal("court-keypoints.basketball.onnx", config.CourtKeypoints.ModelPath);
        Assert.Equal(1280, config.CourtKeypoints.InputSize);
        Assert.Equal(PlayerDetectionConfig.DefaultClassNames, config.PlayerDetection.ClassNames);
    }

    [Fact]
    public void LoadFromJsonOrDefault_PartialJson_FillsMissingFieldsAndSectionsWithDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"models-{Guid.NewGuid():N}.json");
        try
        {
            // Only overrides one field of one section - everything else (including the whole
            // sportClassifier/jerseyNumber sections) should fall back to defaults.
            File.WriteAllText(path, """{ "courtKeypoints": { "keypointConfidenceThreshold": 0.35 } }""");

            var config = ModelsConfig.LoadFromJsonOrDefault(path);

            Assert.Equal(0.35f, config.CourtKeypoints.KeypointConfidenceThreshold);
            Assert.Equal("court-keypoints.basketball.onnx", config.CourtKeypoints.ModelPath);
            Assert.Equal(1280, config.CourtKeypoints.InputSize);
            Assert.Equal("sport-classifier.onnx", config.SportClassifier.ModelPath);
            Assert.Equal("jersey-number.onnx", config.JerseyNumber.ModelPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromJsonOrDefault_FullJson_OverridesEveryField()
    {
        var path = Path.Combine(Path.GetTempPath(), $"models-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "playerDetection": {
                    "modelPath": "basketball_nba_player-detection_1280_yolov8n.onnx",
                    "inputSize": 1280,
                    "classNames": ["Player", "Ball"],
                    "playerClassId": 0,
                    "confidenceThreshold": 0.4,
                    "iouThreshold": 0.5
                  }
                }
                """);

            var config = ModelsConfig.LoadFromJsonOrDefault(path);

            Assert.Equal("basketball_nba_player-detection_1280_yolov8n.onnx", config.PlayerDetection.ModelPath);
            Assert.Equal(["Player", "Ball"], config.PlayerDetection.ClassNames);
            Assert.Equal(0, config.PlayerDetection.PlayerClassId);
            Assert.Equal(0.4f, config.PlayerDetection.ConfidenceThreshold);
            Assert.Equal(0.5f, config.PlayerDetection.IouThreshold);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
