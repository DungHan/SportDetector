using NBA.Vision;

namespace NBA.Vision.Tests;

public class OnnxSportClassifierTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "sport-classifier-fixture.onnx");

    // Fixture model: logit_basketball = 10*avgR, logit_other = 10*avgB (see Assets generation script).
    private static byte[] SolidBgra8(int size, byte b, byte g, byte r)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    [Fact]
    public void Classify_PredominantlyRedImage_ConfidentlyReportsBasketball()
    {
        using var classifier = new OnnxSportClassifier(FixturePath, ["basketball", "other"], inputSize: 4);
        var pixels = SolidBgra8(4, b: 0, g: 0, r: 255);

        var result = classifier.Classify(pixels, 4, 4, 4 * 4);

        Assert.Equal(SportType.Basketball, result.Sport);
        Assert.True(result.Confidence > 0.9f, $"expected high confidence, got {result.Confidence}");
    }

    [Fact]
    public void Classify_PredominantlyBlueImage_ConfidentlyReportsOther()
    {
        using var classifier = new OnnxSportClassifier(FixturePath, ["basketball", "other"], inputSize: 4);
        var pixels = SolidBgra8(4, b: 255, g: 0, r: 0);

        var result = classifier.Classify(pixels, 4, 4, 4 * 4);

        Assert.Equal(new SportType("other"), result.Sport);
        Assert.True(result.Confidence > 0.9f);
    }

    [Fact]
    public void Classify_NeutralGrayImage_ProducesLowConfidence()
    {
        using var classifier = new OnnxSportClassifier(FixturePath, ["basketball", "other"], inputSize: 4);
        var pixels = SolidBgra8(4, b: 128, g: 128, r: 128); // avgR == avgB -> equal logits -> ~0.5 confidence

        var result = classifier.Classify(pixels, 4, 4, 4 * 4);

        Assert.True(result.Confidence < 0.6f, $"expected low confidence for a neutral image, got {result.Confidence}");
    }

    [Fact]
    public void Coordinator_EndToEnd_WithRealOnnxClassifier_RedImageYieldsConfidentBasketball()
    {
        // Proves the full stack - OnnxModelPipeline -> OnnxSportClassifier -> SportClassificationCoordinator's
        // threshold/registry evaluation - works together, not just each piece in isolation.
        using var classifier = new OnnxSportClassifier(FixturePath, ["basketball", "other"], inputSize: 4);
        var directory = Path.Combine(Path.GetTempPath(), $"nba-profiles-{Guid.NewGuid():N}");
        try
        {
            var coordinator = new SportClassificationCoordinator(classifier, new FileSourceProfileStore(directory));
            var pixels = SolidBgra8(4, b: 0, g: 0, r: 255);

            var result = coordinator.ClassifyOrGetCached("source-1", () => new SportClassificationCoordinator.FrameSnapshot(pixels, 4, 4, 16));

            Assert.Equal(SportType.Basketball, result.Sport);
            Assert.Equal(SportClassificationStatus.Confident, result.Status);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
