namespace NBA.JerseyOcr.Tests;

public class OnnxJerseyNumberRecognizerTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "jersey-number-fixture.onnx");

    // Fixture model: GlobalAveragePool's the [1,3,4,4] input into [avgR, avgG, avgB], then a fixed linear map
    // produces a [1,101] logits output (100 number classes + 1 "no number" class, index 100):
    //   class 7   <- 10 * avgR
    //   class 3   <- 1  * avgB
    //   class 100 <- 10 * avgG
    // avgR=1 makes class 7 win with high softmax confidence (~0.995). avgG=1 makes class 100 (no number) win
    // with the same high confidence. avgB=0.4 makes class 3 the argmax, but its softmax confidence against 100
    // near-zero competitors is only ~0.015 - below the default 0.5 threshold.
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
    public void Recognize_ConfidentNumberClass_ReturnsThatNumber()
    {
        using var recognizer = new OnnxJerseyNumberRecognizer(FixturePath, inputSize: 4);
        var pixels = SolidBgra8(4, b: 0, g: 0, r: 255); // avgR = 1.0

        var result = recognizer.Recognize(pixels, width: 4, height: 4, stride: 4 * 4, cropLeft: 0, cropTop: 0, cropRight: 4, cropBottom: 4);

        Assert.Equal(7, result.Number);
        Assert.True(result.Confidence > 0.9f);
    }

    [Fact]
    public void Recognize_ConfidentNoNumberClass_ReturnsUnrecognized()
    {
        using var recognizer = new OnnxJerseyNumberRecognizer(FixturePath, inputSize: 4);
        var pixels = SolidBgra8(4, b: 0, g: 255, r: 0); // avgG = 1.0

        var result = recognizer.Recognize(pixels, width: 4, height: 4, stride: 4 * 4, cropLeft: 0, cropTop: 0, cropRight: 4, cropBottom: 4);

        Assert.Null(result.Number);
    }

    [Fact]
    public void Recognize_NumberClassBelowConfidenceThreshold_ReturnsUnrecognized()
    {
        using var recognizer = new OnnxJerseyNumberRecognizer(FixturePath, inputSize: 4, confidenceThreshold: 0.5f);
        var pixels = SolidBgra8(4, b: 102, g: 0, r: 0); // avgB = 0.4 -> class 3 wins but confidence ~0.015

        var result = recognizer.Recognize(pixels, width: 4, height: 4, stride: 4 * 4, cropLeft: 0, cropTop: 0, cropRight: 4, cropBottom: 4);

        Assert.Null(result.Number);
    }

    [Fact]
    public void Recognize_DegenerateCropRectangle_ReturnsUnrecognized_WithoutRequiringSpecificModelOutput()
    {
        using var recognizer = new OnnxJerseyNumberRecognizer(FixturePath, inputSize: 4);
        var pixels = SolidBgra8(4, b: 0, g: 0, r: 255); // would otherwise resolve to a confident number

        var result = recognizer.Recognize(pixels, width: 4, height: 4, stride: 4 * 4, cropLeft: 10, cropTop: 10, cropRight: 20, cropBottom: 20);

        Assert.Null(result.Number);
    }
}
