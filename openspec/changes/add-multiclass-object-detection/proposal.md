## Why

`models/player-detection.onnx` is a 9-class Roboflow-trained YOLO model (`Ball`, `Hoop`, `Period`, `Player`, `Ref`, `Shot Clock`, `Team Name`, `Team Points`, `Time Remaining` - verified via the file's own embedded metadata, see `models/README.md`), but `OnnxPlayerDetector` only ever decodes the single `Player` channel and silently discards the other 8. Separately, scoreboard OCR (`src/NBA.OCR`, `NBA.State.ScoreboardTextParser`) crops a hardcoded/manually-configured region (`NormalizedRect.DefaultScoreboardRegion`, the bottom 15% of the frame, or a per-source manual override) every ~1s and OCRs whatever text happens to be inside it, with no awareness of where the scoreboard actually is. Both gaps have the same fix: this model already locates every one of those elements in a single forward pass, so decoding all 9 classes from that one pass - instead of discarding 8 of them - both surfaces the non-player detections for visibility and gives scoreboard OCR a precise, detected crop region to use instead of a guessed one, without any additional inference cost.

## What Changes

- `OnnxPlayerDetector`'s postprocessing decodes every class channel in one inference pass (per-class threshold + NMS), not just the configured `Player` channel. The existing `Player`-only output consumed by tracking (`IPlayerDetector.Detect(...)` -> `PlayerDetection`) keeps its exact current shape and behavior - no change visible to `tracking/player-tracking` or anything downstream of it.
- A new output surfaces the other 8 classes' detections (box + class + confidence) from that same pass, exposed via a new interface/method (design.md decides the exact shape) rather than a second model load or a second `Run(...)` call.
- The raw view renders one overlay box per detection of every non-`Player` class this frame, each labeled with its class name, alongside the existing tracked-player boxes.
- Scoreboard OCR's crop-region selection changes: when this frame's (or the most recent detection frame's) scoreboard-related classes (`Period`, `Shot Clock`, `Team Name`, `Team Points`, `Time Remaining`) produced any detections, their union bounding box becomes the OCR crop region for that check, instead of the fixed/manual `NormalizedRect`. When none of those classes were detected, behavior is unchanged (falls back to `NormalizedRect.DefaultScoreboardRegion` / `SourceProfile.ScoreboardRegion`). `IScoreboardOcrEngine`, `FrameCropper`, and `ScoreboardTextParser` are unchanged - only where the crop rectangle comes from changes.

## Capabilities

### New Capabilities
- `vision/on-court-object-detection`: detects every non-`Player` class (`Ball`, `Hoop`, `Period`, `Ref`, `Shot Clock`, `Team Name`, `Team Points`, `Time Remaining`) the player-detection model's multi-class output already contains, from the same inference pass `vision/player-detection` already runs, and renders each as a labeled box on the raw view.
- `ocr/scoreboard-recognition`: this repo's first formal spec for scoreboard OCR - scoped to this change's actual behavior change (how the crop region is chosen: detected scoreboard-object boxes when available, else the existing default/manual region), not a full retroactive spec of pre-existing OCR/text-parsing internals that aren't changing.

### Modified Capabilities
(none - `vision/player-detection`'s externally observable `Player`-class behavior is asserted unchanged by this change, verified by regression tests in tasks.md; sharing one inference pass is an implementation detail, not a requirement change.)

## Impact

- `src/NBA.Vision/OnnxPlayerDetector.cs` (or a renamed/restructured successor - design.md decides): postprocessing decodes all class channels, not one.
- `src/NBA.Vision/`: new detection result type(s) for non-`Player` classes (box + class + confidence).
- `src/NBA.App/App.axaml.cs`: wiring for the new output alongside the existing `IPlayerDetector` wiring.
- `src/NBA.App/ViewModels/MainWindowViewModel.cs`: renders non-`Player` detections as overlay annotations; scoreboard-crop-region selection (~line 300-312) sources from detected scoreboard-object boxes when available.
- `src/NBA.App/Views/RawOverlayView.axaml.cs`: per-class box color/label, extending `CreateControls(...)`'s current single-fixed-color box rendering.
- New unit tests in `tests/NBA.Vision.Tests/` (multi-class postprocessing, per-class NMS, Player-path regression) and `tests/NBA.App.Tests/` (crop-region selection: union-box-when-detected vs. fallback-when-not).
- No changes to `IScoreboardOcrEngine`, `FrameCropper`, `ScoreboardTextParser`, or `tracking/player-tracking`.
