namespace NBA.Vision.Tests;

public class OnnxPlayerDetectorTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "player-detection-fixture.onnx");

    // Fixture model: global-average-pools the input into [avgR, avgG, avgB] (each in [0,1], since
    // ImagePreprocessing.ToNchwTensor scales raw bytes by /255), then emits a fixed [1, 4, 6] output where:
    //   box0 = (0.00, 0.00, 0.50, 0.50, conf: avgR, classId: 0)
    //   box1 = (0.05, 0.05, 0.55, 0.55, conf: avgG, classId: 0)  -- heavily overlaps box0 (IoU ~ 0.68)
    //   box2 = (0.60, 0.60, 1.00, 1.00, conf: avgB, classId: 0)  -- does not overlap box0/box1
    //   box3 = (0.60, 0.00, 1.00, 0.40, conf: 0.99 constant,     classId: 1)  -- always wrong class
    // This lets a single input drive confidence thresholding, NMS (box0 vs box1), and person-class filtering
    // (box3) independently, the same channel-driven approach OnnxCourtKeypointDetectorTests uses.
    private static byte[] SolidBgra8(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
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
    public void Detect_SingleQualifyingBox_ScalesCoordinatesIndependentlyByWidthAndHeight()
    {
        // avgR = avgG = 0 (below threshold, box0/box1 excluded), avgB = 1.0 (box2 qualifies).
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4);
        var pixels = SolidBgra8(8, 4, b: 255, g: 0, r: 0);

        var detections = detector.Detect(pixels, width: 8, height: 4, stride: 8 * 4);

        var box = Assert.Single(detections);
        Assert.Equal(4.8, box.Left, precision: 3);
        Assert.Equal(2.4, box.Top, precision: 3);
        Assert.Equal(8.0, box.Right, precision: 3);
        Assert.Equal(4.0, box.Bottom, precision: 3);
        Assert.Equal(1.0f, box.Confidence, precision: 3);
    }

    [Fact]
    public void Detect_OverlappingBoxes_SuppressesLowerConfidenceOne()
    {
        // avgR = 1.0 (box0), avgG = 0.8 (box1, overlaps box0), avgB = 0 (box2 excluded).
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4);
        var pixels = SolidBgra8(4, 4, b: 0, g: 204, r: 255);

        var detections = detector.Detect(pixels, width: 4, height: 4, stride: 4 * 4);

        var box = Assert.Single(detections);
        Assert.Equal(0.0, box.Left, precision: 3);
        Assert.Equal(2.0, box.Right, precision: 3); // box0's x2 (0.5) * width(4)
        Assert.Equal(1.0f, box.Confidence, precision: 3);
    }

    [Fact]
    public void Detect_OverlappingBoxes_KeepsWhicheverHasHigherConfidence_NotJustTheFirst()
    {
        // avgG = 1.0 (box1), avgR = 0.8 (box0, overlaps box1), avgB = 0 (box2 excluded) - confidences flipped
        // relative to the previous test, proving NMS keeps the higher-confidence box regardless of output order.
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4);
        var pixels = SolidBgra8(4, 4, b: 0, g: 255, r: 204);

        var detections = detector.Detect(pixels, width: 4, height: 4, stride: 4 * 4);

        var box = Assert.Single(detections);
        Assert.Equal(0.2, box.Left, precision: 3); // box1's x1 (0.05) * width(4)
        Assert.Equal(2.2, box.Right, precision: 3); // box1's x2 (0.55) * width(4)
        Assert.Equal(1.0f, box.Confidence, precision: 3);
    }

    [Fact]
    public void Detect_FiltersOutWrongClass_EvenWithHighConfidence()
    {
        // avgR = avgG = avgB = 0: box0/box1/box2 fall below the confidence threshold. box3 has a constant
        // 0.99 confidence but classId 1, which does not match the default personClassId (0), so it must
        // never be returned regardless of how confident the model is.
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4);
        var pixels = SolidBgra8(4, 4, b: 0, g: 0, r: 0);

        var detections = detector.Detect(pixels, width: 4, height: 4, stride: 4 * 4);

        Assert.Empty(detections);
    }
}
