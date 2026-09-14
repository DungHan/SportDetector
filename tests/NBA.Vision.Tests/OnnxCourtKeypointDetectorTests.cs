namespace NBA.Vision.Tests;

public class OnnxCourtKeypointDetectorTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Assets", "court-keypoint-fixture.onnx");

    // Fixture model: mimics Ultralytics' real YOLOv8-pose export layout - output0 shape [1, 11, 2]
    // (4 unused box values + 1 class confidence + 2 keypoints' worth of x/y/vis, per anchor; 2 anchors).
    // Anchor 0 ("winner") has a fixed high confidence (0.95) and keypoint values derived from the input
    // image's average RGB: keypoint 0 = (x: avgR*4, y: avgG*4, vis: avgB), keypoint 1 = (x: avgG*4, y: avgB*4,
    // vis: avgR) - a channel permutation between the two keypoints, same rationale as the old fixture: proves
    // postprocessing reads each keypoint's own slice, not always keypoint 0. Anchor 1 ("loser") has a fixed
    // low confidence (0.05) and sentinel keypoint values (999) - if best-anchor selection is ever wrong, these
    // obviously-wrong numbers surface in assertions instead of silently passing.
    private static CourtGeometryDefinition TwoLandmarkGeometry() => new(
        Sport: SportType.Basketball,
        DisplayName: "Fixture geometry",
        Landmarks: [new CourtLandmark("A", 0, 0, KeypointIndex: 0), new CourtLandmark("B", 1, 1, KeypointIndex: 1)],
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
    public void Detect_AboveThreshold_ReturnsBothLandmarksWithCorrectPositions_FromBestAnchor()
    {
        using var detector = new OnnxCourtKeypointDetector(FixturePath, TwoLandmarkGeometry(), inputSize: 4, keypointConfidenceThreshold: 0.5f);
        // avgR=1.0, avgG=0.8, avgB=1.0. If the wrong (loser) anchor were read, positions would be ~999-scale instead.
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
    public void Detect_BelowKeypointThreshold_OmitsLandmark()
    {
        using var detector = new OnnxCourtKeypointDetector(FixturePath, TwoLandmarkGeometry(), inputSize: 4, keypointConfidenceThreshold: 0.5f);
        // avgR=0.2 (keypoint B's vis channel), avgB=0.2 (keypoint A's vis channel) - both below threshold.
        var pixels = SolidBgra8(4, b: 51, g: 255, r: 51);

        var keypoints = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Empty(keypoints);
    }

    [Fact]
    public void Detect_BelowDetectionThreshold_ReturnsEmpty_EvenWithConfidentKeypoints()
    {
        // The winning anchor's fixture-fixed detection confidence is 0.95 - a threshold above that must
        // reject the whole frame regardless of how confident the individual keypoints would have been.
        using var detector = new OnnxCourtKeypointDetector(FixturePath, TwoLandmarkGeometry(), inputSize: 4, detectionConfidenceThreshold: 0.99f);
        var pixels = SolidBgra8(4, b: 255, g: 204, r: 255);

        var keypoints = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Empty(keypoints);
    }

    [Fact]
    public void Detect_LandmarkWithoutKeypointIndex_IsSkipped()
    {
        var geometry = new CourtGeometryDefinition(
            Sport: SportType.Basketball,
            DisplayName: "Fixture geometry (manual-only landmark)",
            Landmarks: [new CourtLandmark("A", 0, 0, KeypointIndex: 0), new CourtLandmark("ManualOnly", 0, 0)],
            SurfaceWidthMeters: 1,
            SurfaceLengthMeters: 1);
        using var detector = new OnnxCourtKeypointDetector(FixturePath, geometry, inputSize: 4);
        var pixels = SolidBgra8(4, b: 255, g: 204, r: 255);

        var keypoints = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Single(keypoints);
        Assert.Equal("A", keypoints[0].LandmarkName);
    }

    [Fact]
    public void Detect_KeypointIndexBeyondModelOutput_IsSkippedGracefully()
    {
        var geometry = new CourtGeometryDefinition(
            Sport: SportType.Basketball,
            DisplayName: "Fixture geometry (out-of-range index)",
            Landmarks: [new CourtLandmark("OutOfRange", 0, 0, KeypointIndex: 5)],
            SurfaceWidthMeters: 1,
            SurfaceLengthMeters: 1);
        using var detector = new OnnxCourtKeypointDetector(FixturePath, geometry, inputSize: 4);
        var pixels = SolidBgra8(4, b: 255, g: 204, r: 255);

        var keypoints = detector.Detect(pixels, 4, 4, 4 * 4);

        Assert.Empty(keypoints);
    }

    [Fact]
    public void Sport_ReturnsBoundGeometrysSport()
    {
        using var detector = new OnnxCourtKeypointDetector(FixturePath, TwoLandmarkGeometry(), inputSize: 4);

        Assert.Equal(SportType.Basketball, detector.Sport);
    }
}
