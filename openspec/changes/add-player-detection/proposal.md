## Why

`init-nba-vision-platform` built the capture → inference → calibration → overlay foundation but deliberately left all detection-heavy features for later phases. Player detection is the first of those phases: without per-frame player bounding boxes, nothing downstream (tracking, jersey OCR, team clustering) has anything to consume, and the raw-view overlay currently only ever renders court keypoints. This change adds the first real per-frame detector, reusing the ONNX Runtime pipeline and generic overlay/detector patterns the foundation change was explicitly designed to carry.

## What Changes

- Add an `IPlayerDetector` abstraction (mirroring `ICourtKeypointDetector`) with two implementations:
  - `OnnxPlayerDetector`: runs a YOLO-family object-detection ONNX model through the existing `OnnxModelPipeline`/`OnnxModelLoader` (`inference/onnx-runtime`), post-processing raw output into player bounding boxes + confidence scores, gated by a confidence threshold and (for overlapping boxes) NMS.
  - `NullPlayerDetector`: reports zero detections, used when no model file is present, so the app degrades gracefully exactly like `NullCourtKeypointDetector` does today.
- Run player detection once per captured frame (not once per source, unlike sport classification) as part of the existing per-frame pipeline in `NBA.App`.
- Map each detected player box + confidence to the existing generic `OverlayAnnotation` model as a point marker at the box's bottom-center (the player's feet), with a confidence-bearing label, so it renders on the raw view — over the live captured frame — through the already-built overlay mechanism (the same point-rendering path court keypoints already use). No raw-view rework, and the foot-point is also exactly what a follow-on change would feed into `court-calibration`'s homography projection.
- No trained player-detection `.onnx` model is being added in this change (same situation the court-keypoint model was in); `models/` gains a documented expectation for a `player-detection.*.onnx` file, and the app wires `NullPlayerDetector` at runtime until one is supplied. Preprocess → run → postprocess wiring is unit-tested against a small deterministic fixture model (same pattern as `OnnxCourtKeypointDetectorTests`), independent of real-model accuracy.
- Out of scope for this change (future changes): multi-object tracking / stable player IDs across frames, jersey number OCR, team/jersey-color clustering, ball detection, mapping detected players onto the court-calibration homography (that only needs an image-space point — a follow-on change can feed a box's foot-point into the existing `court-calibration` projection without changing this change's output shape).

## Capabilities

### New Capabilities
- `vision/player-detection`: Detecting players in a captured frame as bounding boxes with confidence scores, once per frame, degrading to zero detections when no model is available.

### Modified Capabilities
(none — `inference/onnx-runtime` and the raw-view overlay mechanism are reused as-is, with no requirement-level changes; player detection is a new consumer, not a change to their existing behavior)

## Impact

- New files in `src/NBA.Vision/`: `IPlayerDetector.cs`, `OnnxPlayerDetector.cs`, `NullPlayerDetector.cs`, `PlayerDetection.cs` (box + confidence result type).
- `src/NBA.App/ViewModels/MainWindowViewModel.cs` (or equivalent frame-arrival handler): add player detection to the per-frame pipeline and map results to `OverlayAnnotation`s alongside existing court-keypoint annotations.
- `src/NBA.App/App.axaml.cs`: wire `OnnxPlayerDetector` when a model file is present, else `NullPlayerDetector` — same conditional wiring already used for the court-keypoint detector.
- New tests in `tests/NBA.Vision.Tests/`: `OnnxPlayerDetectorTests.cs` plus a small fixture `.onnx` model under `tests/NBA.Vision.Tests/Assets/`.
- `models/README.md`: document the expected player-detection model file name/shape convention.
