namespace NBA.Vision.Tests;

public class ClipZeroShotSportClassifierTests
{
    // Fixture "vision encoder": spatially average-pools the (already CLIP-normalized) input into a 3-dim
    // embedding - for a solid-color image this just returns that color's normalized per-channel values
    // unchanged (see Assets generation script referenced in the fixture's own ONNX metadata comment).
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "clip-vision-encoder-fixture.onnx");

    // CLIP's standard input normalization constants - duplicated here (not imported from the production
    // class, which keeps them private) so the test derives its expected prompt directions independently,
    // the same way OnnxSportClassifierTests derives expectations from raw pixel values rather than from
    // OnnxSportClassifier's internals.
    private static readonly float[] Mean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] Std = [0.26862954f, 0.26130258f, 0.27577711f];

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

    private static float[] NormalizedChannels(float r, float g, float b) =>
        [(r - Mean[0]) / Std[0], (g - Mean[1]) / Std[1], (b - Mean[2]) / Std[2]];

    private static float[] L2Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        return vector.Select(v => v / norm).ToArray();
    }

    private static IReadOnlyList<ClipSportPrompt> BasketballVsOtherPrompts() =>
    [
        new ClipSportPrompt(SportType.Basketball, L2Normalize(NormalizedChannels(r: 1, g: 0, b: 0))),
        new ClipSportPrompt(new SportType("other"), L2Normalize(NormalizedChannels(r: 0, g: 0, b: 1))),
    ];

    [Fact]
    public void Classify_PredominantlyRedImage_ConfidentlyReportsBasketball()
    {
        using var classifier = new ClipZeroShotSportClassifier(FixturePath, BasketballVsOtherPrompts(), inputSize: 4);
        var pixels = SolidBgra8(4, b: 0, g: 0, r: 255);

        var result = classifier.Classify(pixels, 4, 4, 4 * 4);

        Assert.Equal(SportType.Basketball, result.Sport);
        Assert.True(result.Confidence > 0.99f, $"expected high confidence, got {result.Confidence}");
    }

    [Fact]
    public void Classify_PredominantlyBlueImage_ConfidentlyReportsOther()
    {
        using var classifier = new ClipZeroShotSportClassifier(FixturePath, BasketballVsOtherPrompts(), inputSize: 4);
        var pixels = SolidBgra8(4, b: 255, g: 0, r: 0);

        var result = classifier.Classify(pixels, 4, 4, 4 * 4);

        Assert.Equal(new SportType("other"), result.Sport);
        Assert.True(result.Confidence > 0.99f, $"expected high confidence, got {result.Confidence}");
    }

    [Fact]
    public void Classify_WithLowLogitScale_ProducesLowConfidence_ShowingScaleMustBeCalibrated()
    {
        // Demonstrates design.md's warning: the logit-scale/threshold cannot be assumed from the fine-tuned
        // classifier's old value - an uncalibrated (too low) scale collapses confidence toward chance level
        // even for an image that isn't genuinely ambiguous, so this has to be tuned against real data, not guessed.
        using var classifier = new ClipZeroShotSportClassifier(FixturePath, BasketballVsOtherPrompts(), inputSize: 4, logitScale: 0.3f);
        var pixels = SolidBgra8(4, b: 128, g: 128, r: 128);

        var result = classifier.Classify(pixels, 4, 4, 4 * 4);

        Assert.True(result.Confidence < 0.6f, $"expected low confidence with an uncalibrated logit scale, got {result.Confidence}");
    }

    [Fact]
    public void Constructor_WithNoPrompts_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ClipZeroShotSportClassifier(FixturePath, [], inputSize: 4));
    }

    [Fact]
    public void Constructor_WithOnlyOnePrompt_Throws()
    {
        // A single prompt makes softmax always output 1.0 regardless of actual similarity - meaningless
        // confidence, so this is rejected rather than silently producing it.
        var onePrompt = new[] { new ClipSportPrompt(SportType.Basketball, L2Normalize(NormalizedChannels(r: 1, g: 0, b: 0))) };
        Assert.Throws<ArgumentException>(() => new ClipZeroShotSportClassifier(FixturePath, onePrompt, inputSize: 4));
    }

    [Fact]
    public void Classify_PromptEmbeddingDimensionMismatch_Throws()
    {
        var mismatchedPrompts = new[] // model emits dim-3; the basketball entry here is deliberately dim-2
        {
            new ClipSportPrompt(SportType.Basketball, new float[] { 1f, 0f }),
            new ClipSportPrompt(new SportType("other"), L2Normalize(NormalizedChannels(r: 0, g: 0, b: 1))),
        };
        using var classifier = new ClipZeroShotSportClassifier(FixturePath, mismatchedPrompts, inputSize: 4);
        var pixels = SolidBgra8(4, b: 0, g: 0, r: 255);

        Assert.Throws<InvalidOperationException>(() => classifier.Classify(pixels, 4, 4, 4 * 4));
    }

    [Fact]
    public void Coordinator_EndToEnd_WithRealClipClassifier_RedImageYieldsConfidentBasketball()
    {
        // Proves ClipZeroShotSportClassifier plugs into the existing SportClassificationCoordinator/
        // ISportClassifier contract with no changes needed there - same proof OnnxSportClassifierTests
        // already does for the (now superseded) fine-tuned classifier.
        using var classifier = new ClipZeroShotSportClassifier(FixturePath, BasketballVsOtherPrompts(), inputSize: 4);
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
