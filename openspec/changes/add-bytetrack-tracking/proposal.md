## Why

`add-player-detection` produces per-frame player bounding boxes but explicitly left "multi-object tracking / stable player IDs across frames" out of scope. Right now every frame's detections are independent — the same physical player gets a new, unrelated box every frame, with no notion of identity. Nothing that needs identity over time (jersey-number recognition via multi-frame voting, per-player stats, team assignment) can be built on top of raw per-frame detections alone. This change adds that missing identity layer: a multi-object tracker that consumes `IPlayerDetector`'s per-frame `PlayerDetection` boxes and produces a stable track ID per player, persisted across frames.

## What Changes

- Add an `IPlayerTracker` abstraction in `src/NBA.Tracking` (currently an empty scaffold project) with one implementation, `ByteTrackPlayerTracker`, following the ByteTrack algorithm's core idea: associate *every* detection box to existing tracks by predicted-motion IoU, not just high-confidence ones.
  - **First-round association**: match high-confidence detections against tracks' Kalman-predicted boxes by IoU.
  - **Second-round association**: match remaining *low*-confidence detections (ones `IPlayerDetector` already returns above its own threshold, but below this change's stricter high-confidence split) against tracks still unmatched after round one. This recovers tracks through motion blur/partial occlusion that would otherwise be dropped, which is ByteTrack's headline advantage over SORT-family trackers that discard low-confidence boxes outright.
  - Low-confidence detections are only ever used to keep an existing track alive — they never spawn a new track (that requires an unmatched high-confidence detection), since a low-confidence box alone is too noisy a basis for a brand-new identity.
  - Tracks unmatched in both rounds are kept alive (position held via motion prediction) for a bounded number of frames (occlusion buffer) before being terminated, rather than being dropped the instant a single frame's detection is missed.
- No new/missing-model degraded path is needed here (unlike the detector/keypoint changes) — this is a pure algorithm over already-in-memory boxes, not something backed by an external model file, so there is exactly one implementation and no `Null*` fallback.
- Wire `IPlayerTracker` into `MainWindowViewModel`'s per-frame pipeline: `_playerDetector.Detect(...)` output is fed into the tracker every frame, and the resulting per-track boxes are rendered on the raw view as `OverlayAnnotation.ForBox(...)` (already supports a label) with the track ID as the label, replacing today's unlabeled foot-point markers for player detections.
- Out of scope for this change (future change): jersey-number OCR and its multi-frame voting (needs this change's stable track IDs as an input and is a materially separate concern — an independent OCR/classification model, not a tracking concern); mapping tracks onto the court-calibration homography; team/jersey-color clustering; re-identification after a track is fully terminated (i.e., no long-term Re-ID embedding — this change is motion+IoU association only, matching ByteTrack's own scope).

## Capabilities

### New Capabilities
- `tracking/player-tracking`: Associating per-frame player detections into stable, persistent tracks (one ID per physical player) using motion-predicted IoU association across two confidence tiers, tolerating brief occlusion/missed detections without losing identity.

### Modified Capabilities
(none — `vision/player-detection` is reused as-is, unchanged; tracking is a new consumer of its output, not a change to its behavior)

## Impact

- New files in `src/NBA.Tracking/`: `IPlayerTracker.cs`, `TrackedPlayer.cs` (track ID + box + confidence result type), `ByteTrackPlayerTracker.cs`, plus small supporting types for the per-axis motion predictor (see design.md).
- `src/NBA.App/App.axaml.cs`: construct a `ByteTrackPlayerTracker` and pass it into `MainWindowViewModel` (no conditional/model-path wiring needed, unlike the detector).
- `src/NBA.App/ViewModels/MainWindowViewModel.cs`: feed each frame's `PlayerDetection`s through the tracker and map the resulting `TrackedPlayer`s to `OverlayAnnotation.ForBox(...)` (labeled with the track ID) instead of the current unlabeled foot-point markers.
- New test project `tests/NBA.Tracking.Tests/` (added to `NBA.slnx`), covering the association logic, occlusion-buffer lifecycle, and ID stability directly against `ByteTrackPlayerTracker` (no model/fixture assets needed, since there's no ONNX model in this change).
- `tests/NBA.App.Tests/MainWindowViewModelTests.cs`: extend frame-arrival coverage to assert tracked/labeled box annotations instead of unlabeled foot-points for player detections.
