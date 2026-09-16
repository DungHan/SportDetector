## Why

`add-bytetrack-tracking` gives every player a stable `TrackId` but explicitly deferred "jersey-number OCR or any multi-frame voting on visual content read from a track's crop" as a separate, independent model and a separate change. Right now a `TrackedPlayer` is just a box + confidence + ID - nothing in the pipeline can answer "which player is this" beyond an arbitrary integer. This change adds that missing identity signal: recognizing the jersey number printed on each tracked player's body and attaching it to that player's track, so downstream consumers (stats, minimap labels, future team assignment) have a human-meaningful label instead of a bare track ID.

## What Changes

- Add a new `NBA.Vision` capability that, on real detection frames (see `throttle-player-detection-cadence`), crops each `TrackedPlayer`'s upper-body region and runs a PP-OCRv4 Detection -> Angle-Classification -> Recognition pipeline over ONNX Runtime (`Microsoft.ML.OnnxRuntime`, already used by `OnnxPlayerDetector`/`OnnxCourtKeypointDetector`) to read the printed number.
  - Det locates the number's region as a rotated quadrilateral within the upper-body crop (position/angle vary with player pose - no separate player-pose/keypoint model is added; Det's own quadrilateral output stands in for that).
  - Cls corrects the quadrilateral's orientation (0°/180° flip) before recognition.
  - Rec reads the (perspective-warped) region constrained to a digits-only whitelist (jersey numbers are 1-2 digits, no letters).
- A reading is accepted only if Rec's confidence is above a configurable threshold (default range 0.85-0.90) **and** the decoded string matches the 1-2 digit format - anything else is discarded, not recorded.
- Add a per-`TrackId` jersey-number cache with a voting rule: a track's number becomes "locked" only after a configurable number (default 2-3) of accepted readings agree; once locked, the cached number is returned for that track for the rest of its lifetime, and is never overwritten or cleared by later low-confidence, disagreeing, or missing readings.
- `TrackedPlayer` (or a companion per-track lookup keyed by `TrackId`) exposes the current jersey-number state (locked number, or "not yet determined") for consumers such as overlay rendering.
- Recognition only runs on frames where `IPlayerDetector.Detect(...)` actually ran (real detection frames per the existing cadence gate) - `PredictOnly()` frames reuse whatever jersey-number state a track already has, since there's no new crop worth re-running expensive OCR on for a motion-predicted box.
- Explicitly out of scope: migrating the existing scoreboard OCR (`NBA.OCR`, native Mac Vision / Windows OCR) onto this new ONNX pipeline - it continues to work as-is, unchanged, on its own native engines. No shared "one Rec engine for both scoreboard and jersey" architecture in this change.
- Explicitly out of scope: a dedicated player-pose/keypoint model for affine correction - Det's rotated-quadrilateral output is relied on instead.
- Explicitly out of scope: team assignment, jersey color, or any use of the recognized number beyond attaching it to a track.

## Capabilities

### New Capabilities
- `vision/jersey-number-recognition`: cropping a tracked player's upper-body region, running a PP-OCRv4 Det/Cls/Rec ONNX pipeline over it with a digits-only whitelist, gating accepted readings by confidence and format, and voting per-`TrackId` to a sticky locked number that survives later missed or low-confidence readings.

### Modified Capabilities
(none - `tracking/player-tracking` and `vision/player-detection` are consumed as-is; this change only adds a new reader of `TrackedPlayer`/detection-frame timing, it does not change their requirements)

## Impact

- New files in `src/NBA.Vision/`: `IJerseyNumberRecognizer.cs`, `JerseyNumberReading.cs` (result type: digits + confidence), `OnnxJerseyNumberRecognizer.cs` (Det/Cls/Rec pipeline + whitelist decoding), `NullJerseyNumberRecognizer.cs` (missing-model fallback, matching the existing detector/keypoint pattern).
- New files in `src/NBA.Tracking/` (or `src/NBA.Vision/`, see design.md): a per-`TrackId` jersey-number cache/voter that tracks accepted readings and exposes the locked state.
- `src/NBA.App/ViewModels/MainWindowViewModel.cs`: on real detection frames, crop each `TrackedPlayer`'s upper-body region and feed it through the recognizer + voter; render the locked jersey number (when present) alongside the existing track-ID label.
- `src/NBA.App/App.axaml.cs`: construct `OnnxJerseyNumberRecognizer` (or `NullJerseyNumberRecognizer` if the model file is absent), matching the existing conditional wiring for `OnnxPlayerDetector`/`OnnxCourtKeypointDetector`.
- New ONNX model asset(s) for PP-OCRv4 Det/Cls/Rec (exported, not the native Paddle Inference runtime) - path/licensing/provenance tracked in design.md.
- New test project or additions to `tests/NBA.Vision.Tests/` covering whitelist decoding, confidence/format gating, and the per-track voting/lock lifecycle without needing real model files.
- No changes to `src/NBA.OCR/` (scoreboard OCR untouched).
