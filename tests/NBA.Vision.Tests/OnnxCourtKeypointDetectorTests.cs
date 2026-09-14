namespace NBA.Vision.Tests;

public class OnnxCourtKeypointDetectorTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "court-keypoint-fixture.onnx");

    // Fixture model: global-average-pools the input into [avgR, avgG, avgB] (each in [0,1], since
    // ImagePreprocessing.ToNchwTensor scales raw bytes by /255), then emits output shape [1, 2, 3] where
    // landmark 0 = (x: avgR, y: avgG, conf: avgB) and landmark 1 = (x: avgG, y: avgB, conf: avgR) - a
    // channel permutation between the two landmarks, chosen so a test can prove postprocessing reads each
    // landmark's own slice of the output tensor rather than always landmark 0.
    private static CourtGeometryDefinition TwoLandmarkGeometry() => new(
        Sport: SportType.Basketball,
        DisplayName: "Fixture geometry",
        Landmarks: [new CourtLandmark("A", 0, 0), new CourtLandmark("B", 1, 1)],
        SurfaceWidthMeters: 1,
        SurfaceLengthMeters: 1);

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
    public void Detect_AboveThreshold_ReturnsBothLandmarksWithDistinctPositionsInGeometryOrder()
    {
        using var detector = new OnnxCourtKeypointDetector(FixturePath, TwoLandmarkGeometry(), inputSize: 4, confidenceThreshold: 0.5f);
        // avgR=1.0, avgG=0.8, avgB=1.0 (scaled from bytes below) -> both landmarks' confidence channels are >= threshold.
        var pixels = SolidBgra8(4, b: 255, g: 204, r: 255);

        var keypoints = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Equal(2, keypoints.Count);
        Assert.Equal("A", keypoints[0].LandmarkName);
        Assert.Equal(4.0, keypoints[0].Position.X, precision: 3); // avgR(1.0) * width(4)
        Assert.Equal(3.2, keypoints[0].Position.Y, precision: 3); // avgG(0.8) * height(4)
        Assert.Equal("B", keypoints[1].LandmarkName);
        Assert.Equal(3.2, keypoints[1].Position.X, precision: 3); // avgG(0.8) * width(4)
        Assert.Equal(4.0, keypoints[1].Position.Y, precision: 3); // avgB(1.0) * height(4)
    }

    [Fact]
    public void Detect_BelowThreshold_OmitsLandmark()
    {
        using var detector = new OnnxCourtKeypointDetector(FixturePath, TwoLandmarkGeometry(), inputSize: 4, confidenceThreshold: 0.5f);
        // avgR=0.2 (landmark B's confidence channel), avgB=0.2 (landmark A's confidence channel) - both below threshold.
        var pixels = SolidBgra8(4, b: 51, g: 255, r: 51);

        var keypoints = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Empty(keypoints);
    }

    [Fact]
    public void Detect_GeometryWithFewerLandmarksThanModelOutput_OnlyReturnsGeometrysLandmarks()
    {
        var oneLandmarkGeometry = new CourtGeometryDefinition(
            Sport: SportType.Basketball,
            DisplayName: "Fixture geometry (single landmark)",
            Landmarks: [new CourtLandmark("OnlyOne", 0, 0)],
            SurfaceWidthMeters: 1,
            SurfaceLengthMeters: 1);
        using var detector = new OnnxCourtKeypointDetector(FixturePath, oneLandmarkGeometry, inputSize: 4, confidenceThreshold: 0.5f);
        var pixels = SolidBgra8(4, b: 255, g: 204, r: 255);

        var keypoints = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Single(keypoints);
        Assert.Equal("OnlyOne", keypoints[0].LandmarkName);
    }

    [Fact]
    public void Sport_ReturnsBoundGeometrysSport()
    {
        using var detector = new OnnxCourtKeypointDetector(FixturePath, TwoLandmarkGeometry(), inputSize: 4);

        Assert.Equal(SportType.Basketball, detector.Sport);
    }
}
