## Context

See proposal.md - Why/What Changes for motivation and the exact call sites. Relevant current state:

- `MainWindowViewModel.OnFrameArrived` (src/NBA.App/ViewModels/MainWindowViewModel.cs:151-175) runs synchronously on whatever thread delivers frames (macOS: `MacFrameSource.PollLoopAsync`'s background poll task at a fixed ~33ms tick; Windows: the `Direct3D11CaptureFramePool` callback thread) - detection cadence today equals frame-arrival cadence equals render cadence, with no decoupling.
- `ByteTrackPlayerTracker.Update` (src/NBA.Tracking/ByteTrackPlayerTracker.cs:25-99) already does three things in one call: (1) advance every track's motion prediction one step, (2) two-round IoU association against the frame's detections, (3) age/terminate unmatched tracks and spawn new ones. Only step (1) is safe to run on a frame where no detection was attempted - steps (2)/(3) assume the detections passed in are a complete, trustworthy account of what's in the frame, which isn't true if detection didn't run at all.
- `add-bytetrack-tracking`'s design.md already established "frame-stepped motion prediction, not wall-clock time" - one predict step per processed frame, regardless of real elapsed time - specifically to keep the tracker deterministic and unit-testable. This change's cadence is deliberately frame-count-based (not a wall-clock timer) for the same reason: it stays consistent with that existing decision instead of introducing a second, wall-clock-based notion of time into the same subsystem.

## Goals / Non-Goals

**Goals:**
- Cut YOLO inference frequency to roughly `1/N` of captured frames while keeping every frame's rendered box populated (via motion prediction on the frames in between).
- Keep the occlusion buffer (`maxLostFrames`) meaning "N consecutive missed real detections," not "N consecutive frames," now that frames and detection attempts diverge - a track's real-world occlusion tolerance in wall-clock time should not silently shrink or grow just because cadence changed, only because `maxLostFrames` itself changed.
- Keep the change mechanically small and localized to `MainWindowViewModel` + `NBA.Tracking` - no new threads, queues, or background workers.

**Non-Goals:**
- Tuning the cadence value `N` against real broadcast footage / measured latency - a starting default is chosen here (see Decisions) and exposed as a constructor parameter to retune later without a code change, matching this codebase's existing posture on every other unvalidated threshold (`OnnxPlayerDetector`'s confidence/IoU thresholds, `ByteTrackPlayerTracker`'s own thresholds).
- Throttling court-keypoint detection or scoreboard OCR - keypoint detection has its own, separate performance story (a second ONNX model, already flagged as a deferred concern in `add-player-detection`'s design.md) and OCR is already throttled to ~1Hz by different, existing logic. Both are out of scope here.
- Adaptive/dynamic cadence (e.g. slowing down further under CPU load, or speeding up when few players are on screen) - a fixed constructor-parameter cadence only.
- Moving detection or tracking onto a background thread/worker queue - cadence reduction alone is the chosen lever; threading is a separate, larger architectural change not needed to get the win here.

## Decisions

**Cadence is counted in captured frames, gated in `MainWindowViewModel`, not inside the detector or tracker.** `IPlayerDetector`/`IPlayerTracker` stay cadence-agnostic ("detect whatever you're given," "track whatever you're told to associate or predict") - the decision of *when* to call which one belongs to the orchestrator that already owns frame sequencing and the tracker's lifecycle (`MainWindowViewModel` already owns `_playerTracker.Reset()` on source switch). This avoids threading a cadence concept through two capabilities that don't otherwise need to know about it.

**A new `IPlayerTracker.PredictOnly()` method, not calling `Update([])` on skipped frames.** Calling `Update` with an empty detection list would run the real association/aging logic with zero detections to match against - every live track would count as "unmatched this call" and its `LostFrames` would increment, exactly as if a real detection pass had come back empty. That's wrong here: no detection was attempted, so nothing was observed to *not* match. Doing this naively would make tracks time out `N` times faster than `maxLostFrames` was tuned for, silently defeating the "occlusion buffer counts detection attempts" requirement (tracking/player-tracking's modified "Tolerate brief occlusion" requirement). A separate method makes "no detection was attempted" a distinct, explicit case rather than an ambiguous zero-detections `Update` call.

**`PredictOnly()` and `Update()` share a private predict-step helper in `ByteTrackPlayerTracker`** (currently ad hoc first-3-lines of `Update`, src/NBA.Tracking/ByteTrackPlayerTracker.cs:31-35) rather than duplicating that loop, since both methods need to advance every track's motion model by exactly one step before doing (or not doing) anything else.

**Cadence default: every 3rd frame (`N = 3`).** At the capture pipeline's fixed ~30fps polling tick (`MacFrameSource.PollInterval`), this runs YOLO at ~10fps while still repainting a motion-predicted box on every frame (~30fps), matching the un-tuned-default posture already used elsewhere in this codebase (e.g. `ByteTrackPlayerTracker`'s `maxLostFrames = 30`, `OnnxPlayerDetector`'s confidence threshold) - a reasonable starting point exposed as a constructor parameter, not a measured number.

**Cadence is a frame count, not a wall-clock interval**, unlike the scoreboard OCR throttle (`frame.Timestamp - _lastScoreboardCheckAt >= TimeSpan.FromSeconds(1)`, MainWindowViewModel.cs:201). OCR's throttle is time-based because it's gating a fundamentally time-scoped concern ("the scoreboard doesn't change faster than ~1Hz"). Detection cadence instead needs to stay in the same units as the tracker's own frame-stepped motion model (see Context) so "N frames between detections" and "the tracker's per-frame predict step" refer to the same clock; mixing a wall-clock gate here with a frame-stepped tracker underneath would decouple the two in a way the existing motion model doesn't account for.

**Frame counter resets alongside `_playerTracker.Reset()`** (`MainWindowViewModel.SelectSourceAsync`, line 113), so a fresh source always gets an immediate real detection on its first frame (index 0) rather than starting with N frames of prediction against zero live tracks.

## Risks / Trade-offs

- [Boxes lag real player motion by up to `N-1` frames between real detections] → mitigated by the existing Kalman motion model (already relied upon for occlusion prediction); acceptable at the chosen default given ~10fps real detection is still well above human-perceptible lag for this UI. Revisit `N` if observed to look wrong against real footage (Non-Goal above already flags this as deferred tuning, not a scope boundary).
- [A new track can only be spawned on a real detection frame, so a player who appears mid-cadence is "invisible" for up to `N-1` frames] → same bound as the lag above; already true today in miniature (a player is invisible until the very next captured frame in the current every-frame design) - just a larger, still-bounded window.
- [Occlusion buffer's real-world (wall-clock) duration now scales with `N`] → intentional per the modified tracking/player-tracking requirement (buffer counts detection attempts, not frames); if this makes tracks survive occlusion "too long" in wall-clock terms at a given `N`, tune `maxLostFrames` down rather than reintroducing frame-counted aging.
