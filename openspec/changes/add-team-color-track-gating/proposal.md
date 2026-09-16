## Why

`tracking/player-tracking` associates detections to tracks using motion-predicted IoU alone. In practice, when two players from different teams cross paths or stand close together, their predicted boxes can both land within IoU range of either player's detection, and a track's ID can silently swap onto the wrong physical player - IoU has no notion of "these are visibly different people." Separately, `vision/player-detection` is currently wired to a generic COCO "person" class (`personClassId = 0`), even though a newly available multi-class model (`models/player-detection.onnx`, trained on Roboflow's `basketball-players-fy4c2`-style label set) distinguishes `Player` from `Ref` and other on-court object classes - so referees are today indistinguishable from players and can themselves become (and pollute) tracks. This change fixes the class-filtering gap and adds a lightweight team-color signal as a second, independent check alongside IoU, so an association is only made when both position and appearance agree.

## What Changes

- `OnnxPlayerDetector`'s configured class index is pointed at the new model's dedicated `Player` class (verified against that model's own bundled label metadata, not assumed from any UI display order) instead of the generic person class, so referees/officials are no longer detected or tracked as players.
- `PlayerDetection` gains a plain average-color value (mean color sampled from the upper-body/torso portion of the detected box), computed inline in `OnnxPlayerDetector.Detect(...)` from the pixel data already in hand - no new imaging dependency for this.
- On each real detection frame (the existing detection-cadence gate from `throttle-player-detection-cadence` - this does not run on `PredictOnly()` frames), `ByteTrackPlayerTracker` clusters that frame's detection colors into two groups (a simple 2-means split, standing in for "the two teams' jersey colors") and compares each candidate (track, detection) pair's team-color agreement (by nearest-centroid identity, not raw cluster index, since cluster indices are not stable frame-to-frame) before allowing an IoU-based match - a color disagreement vetoes an otherwise IoU-eligible pair.
- Each track keeps its own running (EMA) color estimate, seeded when the track is created and updated whenever the track is matched.
- The color veto only applies when the frame's color signal is actually informative: fewer than two player detections in the frame, or the two computed centroids are too close together to represent a real two-color split, both skip the veto for that frame (IoU-only association, unchanged from today).
- **Explicitly deferred** (raised in the same discussion, intentionally not part of this change): a speed/displacement clamp on association (rejecting matches implying implausibly large player movement in one frame-step) - paused for a later change.
- **Explicitly out of scope**: appearance-based re-identification after a track terminates, perceptual color-space conversion (v1 uses plain RGB/BGR distance), and any UI/rendering change (team color is an internal matching signal only, not displayed).

## Capabilities

### Modified Capabilities
- `vision/player-detection`: detections are now restricted to the model's dedicated player class (excluding referees/officials/other classes a multi-class model may also emit), and each detection additionally carries an approximate average color.
- `tracking/player-tracking`: association gains a team-color consistency veto alongside existing IoU-based matching, applied only when the current frame's color signal is informative.

## Impact

- `src/NBA.Vision/PlayerDetection.cs`: new average-color field on the result record.
- `src/NBA.Vision/OnnxPlayerDetector.cs`: class-index wiring points at the `Player` class; adds upper-body color sampling to `Detect(...)`.
- `src/NBA.App/App.axaml.cs`: updates the constructor argument for the player class index (and model path, if the new model file is renamed/relocated as part of adopting it).
- `src/NBA.Tracking/GreedyIouMatcher.cs`: `Match` gains an optional eligibility predicate parameter, checked before a candidate pair is considered, defaulting to "always eligible" so existing callers/tests are unaffected.
- `src/NBA.Tracking/ByteTrackPlayerTracker.cs`: per-frame 2-means color clustering, per-track EMA color state, and the veto wiring into both association rounds; new constructor parameters for EMA smoothing and minimum centroid separation.
- New/extended unit tests in `tests/NBA.Vision.Tests/` (color sampling) and `tests/NBA.Tracking.Tests/` (clustering, veto logic, degenerate-frame safeguards) - all runnable without real model files, consistent with this codebase's existing testability posture for these two capabilities.
- No changes to rendering/minimap/OCR code paths, and no changes to the sibling `add-jersey-number-ocr` change (independent proposals; both happen to reuse the same "top ~40% of box height" upper-body crop idea, computed separately in each).
