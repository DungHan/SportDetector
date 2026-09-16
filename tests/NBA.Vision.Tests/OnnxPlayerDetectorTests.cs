namespace NBA.Vision.Tests;

public class OnnxPlayerDetectorTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "player-detection-fixture.onnx");

    // Fixture model: global-average-pools the input into [avgR, avgG, avgB] (each in [0,1], since
    // ImagePreprocessing.ToNchwTensor scales raw bytes by /255), then emits a fixed [1, 6, 4] output - the
    // real YOLOv8-style layout ([1, 4 + numClasses, numCandidates], here numClasses=2, numCandidates=4):
    // channels 0-3 are (cx, cy, w, h) in inputSize(4)-pixel space, channel 4 is the person-class (class 0)
    // score, channel 5 is another class's score. Candidates, once normalized by inputSize and converted to
    // (x1,y1,x2,y2), reproduce the same four boxes as before:
    //   box0 = (0.00, 0.00, 0.50, 0.50, personScore: avgR)
    //   box1 = (0.05, 0.05, 0.55, 0.55, personScore: avgG)  -- heavily overlaps box0 (IoU ~ 0.68)
    //   box2 = (0.60, 0.60, 1.00, 1.00, personScore: avgB)  -- does not overlap box0/box1
    //   box3 = (0.60, 0.00, 1.00, 0.40, personScore: 0 constant, otherScore: 0.99 constant) -- always wrong class
    // This lets a single input drive confidence thresholding, NMS (box0 vs box1), and person-class selection
    // (box3's high score sits on the *other* class's channel) independently, the same channel-driven approach
    // OnnxCourtKeypointDetectorTests uses. The fixture's graph input is named "input" (matching every other
    // fixture in this test project), so tests pass inputName: "input" to override OnnxPlayerDetector's real
    // default of "images" (the verified Ultralytics YOLOv8 ONNX export convention).
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
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4, inputName: "input");
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
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4, inputName: "input");
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
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4, inputName: "input");
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
        // 0.99 confidence but classId 1, which does not match the default playerClassId (0), so it must
        // never be returned regardless of how confident the model is.
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4, inputName: "input");
        var pixels = SolidBgra8(4, 4, b: 0, g: 0, r: 0);

        var detections = detector.Detect(pixels, width: 4, height: 4, stride: 4 * 4);

        Assert.Empty(detections);
    }

    [Fact]
    public void Detect_SingleQualifyingBox_ReportsUpperBodyMeanColor()
    {
        // Same setup as Detect_SingleQualifyingBox_ScalesCoordinatesIndependentlyByWidthAndHeight - box2
        // qualifies. Every pixel in the source frame is the same solid color, so the upper-body sub-rectangle
        // (whatever its exact bounds) must average to that exact color.
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4, inputName: "input");
        var pixels = SolidBgra8(8, 4, b: 255, g: 0, r: 0);

        var detections = detector.Detect(pixels, width: 8, height: 4, stride: 8 * 4);

        var box = Assert.Single(detections);
        Assert.True(box.Color.HasValue);
        Assert.Equal(0, box.Color!.Value.R);
        Assert.Equal(0, box.Color.Value.G);
        Assert.Equal(255, box.Color.Value.B);
    }

    [Fact]
    public void Detect_NonDefaultPlayerClassId_ReadsConfiguredChannelInsteadOfHardcodedOne()
    {
        // box3 has a constant 0.99 confidence on the *other* class channel (classId 1, not the default 0).
        // Configuring playerClassId: 1 must make it the one channel actually read - proving the class channel
        // is driven by the constructor parameter, not hardcoded to channel 4/classId 0.
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4, inputName: "input", playerClassId: 1);
        var pixels = SolidBgra8(4, 4, b: 0, g: 0, r: 0);

        var detections = detector.Detect(pixels, width: 4, height: 4, stride: 4 * 4);

        var box = Assert.Single(detections);
        Assert.Equal(0.99f, box.Confidence, precision: 3);
    }

    [Fact]
    public void Detect_PlayerClassIdOutOfRangeForModelsClassCount_ReturnsZeroDetectionsRatherThanThrowing()
    {
        // The fixture model has numClasses=2 (valid indices 0-1). An index beyond that must degrade to zero
        // detections (matching the rest of this pipeline's "no usable signal -> empty, not an exception"
        // posture), not throw an IndexOutOfRangeException reading past the output tensor's channel range.
        using var detector = new OnnxPlayerDetector(FixturePath, inputSize: 4, inputName: "input", playerClassId: 5);
        var pixels = SolidBgra8(4, 4, b: 255, g: 255, r: 255);

        var detections = detector.Detect(pixels, width: 4, height: 4, stride: 4 * 4);

        Assert.Empty(detections);
    }
}
