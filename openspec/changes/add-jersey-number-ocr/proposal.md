## Why

Every tracked player's minimap marker is currently labeled with its track ID (`$"#{t.TrackId}"`, `MainWindowViewModel.ProcessFrameArrived`) — a number the tracker assigns arbitrarily and never reuses, not the player's actual printed jersey number. It resets to a new value any time a track is occluded past `maxLostFrames` and restarts, so it can't identify *who* a marker represents across a broadcast, let alone against a roster. `add-bytetrack-tracking`'s design already flagged jersey-number OCR with multi-frame voting as the intended next consumer of track IDs, and the now-archived `add-player-minimap-projection` reserved the `CourtMarker`/`StyleKey`/`Label` plumbing specifically so this and `add-jersey-color-detection` would have a marker to attach to. This change reads the printed number off each track's own crop and, once enough frames agree, shows that instead.

## What Changes

- New `NBA.JerseyOcr` capability: `IJerseyNumberRecognizer`, reading one track's cropped image region and returning a recognized number + confidence, or "unrecognized." Backed by `OnnxJerseyNumberRecognizer` (no trained model shipped in this change — same posture as `vision/court-calibration`'s keypoint model), with a `NullJerseyNumberRecognizer` degraded path that always returns "unrecognized," matching the existing `NullPlayerDetector`/`NullCourtKeypointDetector` pattern. (Originally planned to reuse the pre-scaffolded `NBA.OCR` project, but a concurrently-merged scoreboard-OCR change claimed that project for unrelated broadcast-text recognition first — this capability lives in its own `NBA.JerseyOcr` project instead.)
- Per-track multi-frame voting: accumulate each frame's recognized number per `TrackId`, resolve to a stable number only once one candidate has enough accumulated votes (avoids flipping the displayed number on a single bad frame). Vote history for a track is discarded when its track terminates (mirrors `IPlayerTracker.Reset()`/track-termination semantics already established by `tracking/player-tracking`) — a track ID is never assumed to carry a number forward across a termination/restart.
- `MainWindowViewModel.ProcessFrameArrived` crops each tracked player's box out of the current frame, runs it through the recognizer, feeds the result into the vote aggregator, and uses the resolved jersey number (once one exists) as that track's `CourtMarker.Label` instead of `$"#{t.TrackId}"`; falls back to the track ID label until a number resolves.
- `MinimapView.axaml`'s marker template renders `CourtMarker.Label` as visible text on/inside the marker circle — today `Label` is carried on `CourtMarker`/`AnnotationVisual` but the minimap's `Ellipse` template never displays it, so this is the first change that actually surfaces it there.
- Explicitly out of scope: jersey **color** extraction (separate planned change, `add-jersey-color-detection`), team assignment or roster matching, and any form of re-identification (a track that terminates and a new track that later shows the same jersey number are never merged — matches the Re-ID boundary `add-bytetrack-tracking`'s design already drew).

## Capabilities

### New Capabilities
- `ocr/jersey-number-recognition`: recognizing a player's printed jersey number from a tracked crop, with multi-frame voting to produce one stable number per track.

### Modified Capabilities
- `app/dual-view-shell`: the minimap's per-tracked-player marker (added by `add-player-minimap-projection`) is now labeled with the resolved jersey number instead of the raw track ID once recognition resolves, and the minimap's marker template renders that label visibly for the first time.

## Impact

- `src/NBA.JerseyOcr/`: new project (registered in `NBA.slnx`) with `IJerseyNumberRecognizer`, `OnnxJerseyNumberRecognizer`, `NullJerseyNumberRecognizer`, and the per-track vote aggregator.
- `tests/NBA.JerseyOcr.Tests/`: new test project (registered in `NBA.slnx`, mirroring `tests/NBA.Tracking.Tests`'s deterministic, no-model-file style — hand-constructed recognition sequences, no fixture assets needed for the voting logic).
- `src/NBA.Inference/ImagePreprocessing.cs`: likely needs a crop-region-aware preprocessing path (today's `ToNchwTensor` always maps the full source frame; a track's box is a sub-region of it) — finalized in design.md.
- `src/NBA.App/ViewModels/MainWindowViewModel.cs`: crop extraction per tracked player, recognizer invocation, vote aggregation wiring, marker label resolution.
- `src/NBA.App/App.axaml.cs`: wire `IJerseyNumberRecognizer` (model-file-present check + `Null` fallback, matching the existing detector-wiring pattern) and the vote aggregator into `MainWindowViewModel`'s constructor.
- `src/NBA.App/Views/MinimapView.axaml`: marker `DataTemplate` renders `Label` as text.
