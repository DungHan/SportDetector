## Context

Builds directly on the `inference/onnx-runtime` foundation (`OnnxModelLoader`/`OnnxModelPipeline` in `src/NBA.Inference`) and the interface + null-object + onnx-impl pattern already proven for court keypoints (`ICourtKeypointDetector` / `NullCourtKeypointDetector` / `OnnxCourtKeypointDetector` in `src/NBA.Vision`). The raw-view overlay already renders a generic `OverlayAnnotation`, and `MainWindowViewModel.OnFrameArrived` already maps `DetectedKeypoint` results to `OverlayAnnotation.ForPoint(...)` markers on the raw view (drawn over the live captured frame). This change reuses that exact same point-rendering path for player detections - one marker per detection at the bounding box's bottom-center (the player's feet) - rather than introducing the also-available `OverlayShapeKind.Box`/`ForBox`, so no view-layer change is needed at all, not even a new shape branch. See proposal.md for motivation and scope.

## Goals / Non-Goals

**Goals:**
- Define `IPlayerDetector` and get a `NullPlayerDetector` + `OnnxPlayerDetector` implementation wired into the app's per-frame pipeline, following the existing detector pattern exactly.
- Make the ONNX I/O wiring (preprocess → run → postprocess, including NMS) independently testable against a small fixture model, without depending on a real trained model.
- Map results to a foot-point `OverlayAnnotation.ForPoint(...)` marker per detection so they render on the raw view with zero view-layer changes.

**Non-Goals:**
- Training or sourcing a real player-detection model — same placeholder situation the court-keypoint model is in; `NullPlayerDetector` is the real runtime path until one exists.
- Projecting player positions onto the court-calibration homography (minimap). `PlayerDetection` still exposes the full box, not just the foot-point; a follow-on change can feed the same bottom-center point already computed for the raw-view marker into the existing `PointProjector`/`HomographyCalibrator` without changing this change's detector output shape.
- Any player identity, tracking across frames, jersey number, or team-color logic.

## Decisions

**Output tensor convention: verified against a real Ultralytics `yolov8n.onnx` export**, not a guess. Input `images` `[1, 3, 640, 640]`, output `output0` `[1, 4 + numClasses, numCandidates]` (confirmed `[1, 84, 8400]` for the stock COCO 80-class model) - 4 box channels (`cx, cy, w, h`, in the model's input-pixel space, not normalized) followed by one confidence channel per class (no combined confidence+classId column, and no NMS baked in - this is the raw per-anchor head). `OnnxPlayerDetector` reads only the configured `personClassId`'s channel (COCO class 0, "person") directly, rather than computing an argmax over all classes, since only one class is ever needed here.

**NMS runs in postprocessing, not in the model.** Some YOLO ONNX exports bake NMS into the graph (post-NMS output), others don't (raw per-anchor output requiring NMS afterward). Rather than assume one or the other, `OnnxPlayerDetector` always applies IoU-based NMS in C# postprocessing after confidence filtering. If a future exported model already performs NMS internally, running NMS again on an already-deduplicated set is a no-op (no overlapping boxes left to suppress), so this is safe either way and avoids a second detector variant.

**Detection runs per-frame, inline in the same pipeline step as court-keypoint detection**, not on a separate cadence or background thread. Consistent with `design.md`'s existing "single capture→inference→projection pipeline producing a per-frame result object" architecture (`init-nba-vision-platform`); this is a second detector call added to that same per-frame step, not a new pipeline. Performance (running two ONNX models per frame) is a tuning concern for later, not a scope boundary for this change.

**Confidence threshold and NMS IoU threshold are constructor parameters with sensible defaults** (mirrors `OnnxCourtKeypointDetector`'s `confidenceThreshold = 0.5f` pattern) rather than hardcoded, so they can be tuned per-model without a code change once a real model exists.

## Risks / Trade-offs

- [No basketball-specific trained model exists yet] → a stock COCO-pretrained `yolov8n` export is the first real model wired in (general person detection, not basketball-tuned); `NullPlayerDetector` remains the fallback when no model file is present at all. Swapping to a basketball-specific model later (e.g. a YOLO-variant export from Roboflow's `basketball-players-fy4c2`) needs no code change as long as it uses the same raw Ultralytics export shape.
- [Running two ONNX models per frame may be too slow for real-time use] → out of scope to solve here; flagged for a later performance pass once real-world latency is measured.
- [NMS threshold tuning is model-dependent] → exposed as a constructor parameter now rather than hardcoded, so it doesn't require a code change later.
