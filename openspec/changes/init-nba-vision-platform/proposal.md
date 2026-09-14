## Why

There is currently no tooling to turn a screen-captured basketball feed (a game client or a YouTube broadcast) into structured, positionally-accurate data. Building this requires a foundation that does not exist yet: a way to capture frames from an arbitrary window/source, run ONNX-based YOLO models against them, and — the hardest part — calibrate whatever camera angle the source happens to use against a real basketball court so detections can be projected onto a 2D minimap. Every later capability (player tracking, jersey numbers, ball detection, score/state OCR) depends on this foundation being in place first, so it should be built and proven before any detection-heavy feature is attempted.

## What Changes

- Stand up the .NET solution skeleton (`NBA.sln`) with the project layout agreed on for the whole system: `NBA.Capture`, `NBA.Inference`, `NBA.Vision`, `NBA.Tracking`, `NBA.OCR`, `NBA.State`, `NBA.App` (Avalonia). Only the first four are implemented in this change; `NBA.Tracking`, `NBA.OCR`, `NBA.State` are created as empty placeholder projects wired into the solution so later changes can slot in without restructuring.
- Implement window/screen frame capture on Windows (Windows.Graphics.Capture) producing a live frame stream.
- Implement a reusable ONNX Runtime inference wrapper (model load, preprocessing/postprocessing pipeline, DirectML/CPU execution provider selection) that later models (player, ball, jersey, OCR) will plug into.
- Implement a sport-classification step (transfer-learned lightweight image classifier, not a raw general-purpose classifier used zero-shot) that determines which sport a source is showing, run once per source and cached rather than every frame, with manual override. Only basketball is a fully registered/supported sport in this change; the registry is designed so future sports (soccer, tennis, etc., discussed as roadmap context) are added as new entries, not architecture changes.
- Implement court keypoint detection (YOLO-pose style ONNX model, model file supplied externally) plus homography computation (OpenCvSharp4) that projects image coordinates to real-world court coordinates. The keypoint/court geometry schema is parameterized by sport (selected via the classification step above); only the basketball geometry definition is implemented in this change.
- Implement the Avalonia application shell, stacked by default (raw view above minimap view), with two independently toggleable views: (1) the raw captured frame with an extensible detection overlay (court keypoints now; the same generic overlay mechanism is designed to carry player boxes/jersey labels once those phases land), (2) a 2D court minimap that plots the homography-projected points and reserves a status/info panel region for future score/state data (placeholder content only in this change).
- Ship a manual court-corner calibration fallback (user clicks known court reference points) for sources where the keypoint model has not been trained/fine-tuned yet, so the pipeline is usable end-to-end before model quality is proven.
- Establish the training/inference split convention: model training happens out-of-repo in Python/Ultralytics; only exported `.onnx` files under `models/` are consumed by .NET.
- Out of scope for this change (future changes, mentioned here only as roadmap context): player/ball detection, multi-object tracking, jersey number recognition, score/state OCR, team clustering, actual soccer/tennis/other-sport geometry definitions and detection models (only their extension points exist after this change).

## Capabilities

### New Capabilities
- `capture/frame-capture`: Capturing frames from a selected window or screen source as a continuous, consumable frame stream.
- `inference/onnx-runtime`: Loading and running ONNX models (starting with the court keypoint model) through a shared, reusable inference pipeline.
- `vision/sport-classification`: Determining which sport a capture source is showing (basketball fully supported; registry extensible to other sports later), cached per source with manual override.
- `vision/court-calibration`: Deriving a homography from detected (or manually marked) court keypoints — using a sport-selected geometry definition — and projecting image-space points into court-space coordinates.
- `app/dual-view-shell`: The Avalonia application shell providing the two independently toggleable views (raw overlay view, court minimap view) driven by the capture/inference/calibration pipeline, plus the sport indicator/override control.

### Modified Capabilities
(none — greenfield project, no existing specs)

## Impact

- **New code**: entire `NBA.sln` solution and all listed projects; `models/` directory convention for shipped ONNX files.
- **New dependencies**: `Microsoft.ML.OnnxRuntime` (+ `.DirectML` execution provider), `OpenCvSharp4` (+ runtime native package), Avalonia, `Vortice.Windows` or equivalent WinRT capture interop.
- **Platform constraint**: capture uses Windows.Graphics.Capture, so `NBA.Capture` is Windows-only for this change even though the UI framework (Avalonia) is cross-platform; this is called out explicitly in design.md.
- **No existing systems affected** — this is the first change in the repository.
