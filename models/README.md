# models/

Exported ONNX model files consumed by `NBA.Inference`. Models are **not trained in this repository** — training happens out-of-repo in Python/Ultralytics (or equivalent); only the exported `.onnx` artifact is committed here. See `design.md` in `openspec/changes/init-nba-vision-platform/` for the training/inference split rationale.

## Naming convention

`<capability>.<sport>.onnx`, for example:

- `court-keypoints.basketball.onnx` — court keypoint detection model consumed by `vision/court-calibration`.
- `sport-classifier.onnx` — the sport classification model consumed by `vision/sport-classification` (not sport-specific by definition, so no `<sport>` segment).

A model file is not required for the application to run: every model-backed capability has a documented degraded/manual path when its model file is absent (e.g., manual court calibration, "unknown" sport classification) — see the relevant `specs/*/spec.md` for the exact fallback behavior.
