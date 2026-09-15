## Why

`MainWindowViewModel.OnFrameArrived` currently runs the full YOLO player-detection model on every single captured frame, synchronously, on the frame-arrival thread. Both `add-player-detection` and `add-bytetrack-tracking` explicitly deferred this cost ("performance is a tuning concern for later, not a scope boundary" / "real-time performance tuning ... is a non-goal"). The tracker (`ByteTrackPlayerTracker`) that exists specifically to carry player identity across frames already has a per-track motion model (`BoxMotionPredictor`, four independent Kalman filters) capable of predicting a box's position without a new detection - but nothing in the current pipeline uses it that way, since every frame both detects and associates. Running the expensive model less often, while still advancing motion prediction every frame, cuts inference cost with a bounded, predictable accuracy trade-off (a stale box between real detections, smoothed by the existing Kalman model) instead of an unbounded one.

## What Changes

- Add a frame counter to `MainWindowViewModel` (reset alongside the existing `_playerTracker.Reset()` call on source switch) that gates the YOLO `_playerDetector.Detect(...)` call to every Nth captured frame instead of every frame.
- Add `IPlayerTracker.PredictOnly()`: advances every track's motion model by one frame-step and returns the resulting boxes, without running detection association, without aging (`LostFrames`), and without spawning or terminating tracks - used on the frames where detection is skipped, so tracked boxes still move smoothly between real detections instead of freezing.
- On frames where detection runs, behavior is unchanged: `_playerDetector.Detect(...)` followed by `_playerTracker.Update(...)` (association, aging, spawn/terminate), exactly as today.
- Refactor `ByteTrackPlayerTracker`'s existing predict-step loop (the first few lines of `Update`) into a small private helper shared by `Update` and the new `PredictOnly`, rather than duplicating it.
- **BREAKING** (internal API only, no external consumers): `IPlayerTracker` gains a new member (`PredictOnly`) that any future additional implementation must also provide. `ByteTrackPlayerTracker` remains the only implementation today.
- Update doc comments that currently assert "runs on every frame" / "called exactly once per processed frame" (`IPlayerDetector.cs`, `IPlayerTracker.cs`, `ByteTrackPlayerTracker.cs`) to describe the new cadence-gated behavior.

## Capabilities

### Modified Capabilities
- `vision/player-detection`: the "Detect player bounding boxes per frame" requirement changes from running on every captured frame to running on a configurable cadence (every Nth frame).
- `tracking/player-tracking`: adds a predict-only mode for frames where no detection was attempted, and clarifies that the occlusion buffer (`maxLostFrames`) counts consecutive detection *attempts*, not raw captured frames, now that the two can differ.

## Impact

- `src/NBA.App/ViewModels/MainWindowViewModel.cs`: `OnFrameArrived` gains a frame counter and a branch between `_playerTracker.Update(...)` (detection frames) and `_playerTracker.PredictOnly()` (skipped frames); `SelectSourceAsync` resets the counter.
- `src/NBA.Tracking/IPlayerTracker.cs`: new `PredictOnly()` member.
- `src/NBA.Tracking/ByteTrackPlayerTracker.cs`: implements `PredictOnly()`; predict-step loop factored into a shared private helper.
- No changes to `NBA.Vision` (the detector itself is unaware of cadence - it's still "detect whatever frame you're given"), no changes to rendering/minimap/OCR code paths (they already just consume whatever `TrackedPlayer` list they're handed).
- No new dependencies, no schema/model changes.
