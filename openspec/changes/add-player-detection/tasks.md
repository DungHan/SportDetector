## 1. Detector abstraction

- [x] 1.1 Define `PlayerDetection` (image-space bounding box: left/top/right/bottom in pixels, plus `Confidence`) in `src/NBA.Vision/PlayerDetection.cs`, and `IPlayerDetector` (a single `Detect(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride)` returning `IReadOnlyList<PlayerDetection>`) in `src/NBA.Vision/IPlayerDetector.cs`, mirroring `ICourtKeypointDetector`'s shape. Verify the project builds.

## 2. Null implementation (real runtime default until a model exists)

- [x] 2.1 Implement `NullPlayerDetector : IPlayerDetector` in `src/NBA.Vision/NullPlayerDetector.cs` returning an empty list always. Verify with a unit test in `tests/NBA.Vision.Tests/NullPlayerDetectorTests.cs` asserting `Detect(...)` returns zero detections for arbitrary input.

## 3. ONNX implementation

- [x] 3.1 Implement `OnnxPlayerDetector : IPlayerDetector, IDisposable` in `src/NBA.Vision/OnnxPlayerDetector.cs` using `OnnxModelPipeline` (per `inference/onnx-runtime`): preprocess via `ImagePreprocessing.ToNchwTensor`, postprocess assuming a `[1, N, 6]` output tensor (x1, y1, x2, y2, confidence, classId; normalized 0-1 coordinates) per the design.md decision, filtering to the configured person-class index and the confidence threshold. Document the tensor-shape assumption in an XML doc comment the same way `OnnxCourtKeypointDetector` does. Verify the project builds.
- [x] 3.2 Add IoU-based non-max suppression over the confidence-filtered boxes in postprocessing, keeping the highest-confidence box per overlapping cluster. Verify with a unit test that hand-constructs several overlapping boxes at varying confidence and asserts only the highest-confidence one per cluster survives.
- [x] 3.3 Add a small deterministic fixture ONNX model under `tests/NBA.Vision.Tests/Assets/player-detection-fixture.onnx` (same fixture-testing approach as `court-keypoint-fixture.onnx`) and `tests/NBA.Vision.Tests/OnnxPlayerDetectorTests.cs` verifying the preprocess → run → postprocess wiring end-to-end: confidence thresholding, person-class filtering, and correct box coordinate scaling back to image pixel space. Verify `dotnet test tests/NBA.Vision.Tests` passes.

## 4. Pipeline wiring

- [x] 4.1 Wire `OnnxPlayerDetector` in `src/NBA.App/App.axaml.cs` when a configured player-detection model file is present, else fall back to `NullPlayerDetector` — same conditional wiring already used for `ICourtKeypointDetector`. Verify the app still starts with no model file present (falls back to null detector, no exception).
- [x] 4.2 Call the wired `IPlayerDetector` from the per-frame handler (`MainWindowViewModel.OnFrameArrived`, alongside the existing `ICourtKeypointDetector` call) once per captured frame. Verify with a `MainWindowViewModelTests` case that a frame arrival triggers exactly one `Detect` call on a test double.

## 5. Overlay rendering

- [x] 5.1 Map each `PlayerDetection` to `OverlayAnnotation.ForPoint((left + right) / 2, bottom, label: $"{confidence:P0}")` (a marker at the box's bottom-center - the player's feet) in `MainWindowViewModel.OnFrameArrived`, appended to the same annotation list court keypoints already populate, drawn over the live captured frame via the existing point-rendering path. Verify with a `MainWindowViewModelTests` case asserting the raw-view annotation count includes one foot-point annotation per detection, with a `Confidence`-derived label, alongside the existing keypoint point annotations.
- [x] 5.2 Verify a frame with zero player detections produces zero foot-point annotations (no leftover annotations from a prior frame with detections) — extend the existing frame-arrival test coverage.

## 6. Documentation

- [x] 6.1 Update `models/README.md` documenting the expected player-detection model file name/location and its assumed `[1, N, 6]` output tensor convention, flagged as unverified against a real trained model (same framing as the existing court-keypoint model note).
