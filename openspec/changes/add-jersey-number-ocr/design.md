## Context

See proposal.md - Why/What Changes for motivation. Relevant current state:

- `TrackedPlayer` (`src/NBA.Tracking/TrackedPlayer.cs`) is `(TrackId, Left, Top, Right, Bottom, Confidence)` - no jersey-number field, and `ByteTrackPlayerTracker` never reuses a terminated `TrackId` (`add-bytetrack-tracking`'s design.md).
- `MainWindowViewModel.OnFrameArrived` (per `throttle-player-detection-cadence`) already distinguishes "real detection frames" (`_playerDetector.Detect(...)` + `_playerTracker.Update(...)`) from "predicted frames" (`_playerTracker.PredictOnly()`, no fresh detection). This change's recognition step only runs on the former.
- The existing ONNX detector pattern (`OnnxPlayerDetector`, `OnnxCourtKeypointDetector`) is: one `OnnxModelPipeline<TInput, TOutput>` (`src/NBA.Inference/OnnxModelPipeline.cs`) per loaded model, wired in `App.axaml.cs` behind a `File.Exists(modelPath)` check with a `Null*` fallback when the file is absent - `OnnxModelPipeline`'s own doc comment already anticipates this: "court keypoints today; player/ball/jersey/OCR models in later phases."
- `src/NBA.OCR` (scoreboard) is native-OS OCR (Apple Vision / Windows.Media.Ocr), not ONNX, and per this change's proposal stays untouched - there is no existing ONNX OCR engine in this repo to build on; this change introduces the first one.

## Goals / Non-Goals

**Goals:**
- Recognize jersey numbers using the same `Microsoft.ML.OnnxRuntime` + `OnnxModelPipeline` infrastructure every other model in this repo already uses, rather than a new inference stack.
- Keep the recognition cost bounded to real detection frames only, and to the players actually being tracked (not a full-frame scan) - it should not add a second, independently-scaled inference cost on top of `throttle-player-detection-cadence`'s existing cadence work.
- Make the confidence/format gate and per-track voting logic fully unit-testable without any ONNX model file, the same way `ByteTrackPlayerTracker`'s association logic is testable without a detector model.

**Non-Goals:**
- Migrating scoreboard OCR onto this pipeline, or any shared-engine architecture between the two OCR use cases (see proposal.md).
- A player-pose/keypoint model for affine correction (see proposal.md) - Det's own rotated-quadrilateral output is used instead.
- Multi-frame *appearance* re-identification (recovering a jersey number for a track that already terminated) - locking is scoped to one track's lifetime, matching `tracking/player-tracking`'s existing no-re-ID stance.
- Tuning confidence/agreement-count defaults against real broadcast footage - exposed as constructor parameters with starting defaults, same posture as every other unvalidated threshold in this codebase.

## Decisions

**Three separate ONNX sessions (Det, Cls, Rec), each its own `OnnxModelPipeline` instance, not one combined model.** PP-OCRv4 ships as three distinct trained models with different input/output shapes (a segmentation-style detector, a binary angle classifier, a CTC-style sequence recognizer). This matches the existing "one model = one pipeline instance" pattern exactly (`OnnxModelPipeline`'s doc comment) rather than inventing a new multi-model wrapper shape. `OnnxJerseyNumberRecognizer` owns all three pipelines internally and exposes one `Recognize(crop) -> JerseyNumberReading?` method - callers never see the three-stage split.

**Scoreboard's Rec-only shortcut does not apply here - jersey recognition always runs Det -> Cls -> Rec.** A player's upper-body crop has unknown number position/size/rotation (pose-dependent), unlike the scoreboard's fixed, upright ROI. Det locates the number's region as a rotated quadrilateral; Cls corrects any 180° flip; Rec reads the perspective-warped result. Skipping Det (feeding the whole crop straight to Rec, scoreboard-style) was considered and rejected - Rec assumes an already-tight, already-horizontal single line of text, which a raw upper-body crop is not.

**No player-pose/keypoint model; Det's rotated quadrilateral stands in for affine correction.** Considered adding a dedicated pose/keypoint model to un-rotate the crop before OCR, but this repo has no player-body keypoint model today (only court keypoints), and adding one is a materially larger change than jersey OCR itself. Det already produces an oriented quadrilateral per detected text region; that quadrilateral's corners are used to perspective-warp just the number region before Cls/Rec. If real footage later shows Det's own angle estimate is too unreliable at extreme player rotation, revisit then - not a reason to add a pose model preemptively.

**Digit-only whitelist is enforced by masking Rec's per-timestep character probabilities before CTC decode, not by retraining or swapping the model.** Standard PP-OCR Rec models decode over a large character set (digits, Latin letters, punctuation). Restricting output to `0-9` is done by zeroing (or `-inf`-ing) every non-digit character's logit/probability at each decode timestep before greedy CTC collapse, so decode-time constraints - not a different weight file - produce the digits-only guarantee. The same technique is not applied to the scoreboard path (out of scope; it keeps its current native OCR).

**Jersey-number cache/voter is a new component in `NBA.Vision`, keyed by `TrackId`, owned by the caller (`MainWindowViewModel`) - not folded into `ByteTrackPlayerTracker`.** `ByteTrackPlayerTracker`'s `Track` is a private, sealed implementation detail with no extension point, and `tracking/player-tracking` has no reason to know about OCR results (per proposal.md's "Modified Capabilities: none"). A standalone `JerseyNumberVoter` (or similarly named class) holds a `Dictionary<int, VoterState>` and is fed `(TrackId, JerseyNumberReading)` pairs by the caller; it has no dependency on `ByteTrackPlayerTracker` itself.

**Stale cache entries are pruned using the current frame's live `TrackId` set, not a separate expiry timer.** Since `ByteTrackPlayerTracker` never reuses a terminated `TrackId` (existing decision, `add-bytetrack-tracking`), a voter entry for a `TrackId` no longer present in the current frame's `Update`/`PredictOnly` output belongs to a track that will never be seen again and is safe to drop. Each call into the voter passes the current frame's full live `TrackId` set so it can remove everything else, keeping the cache bounded to currently-visible players rather than growing unboundedly over a long video.

**Recognition runs once per tracked player per real detection frame, not once per frame overall.** Cost scales with (real-detection-frame rate) x (players on screen) - already reduced ~3x by `throttle-player-detection-cadence`'s cadence gate, and further bounded to only players that exist as tracks (not a full-frame text scan). A track that is already locked SHALL still skip re-running recognition entirely (not just skip recording the result) once locked, since the spec already requires the locked value to be immutable - there is no reason to keep paying Det/Cls/Rec cost per frame for a player whose number is already resolved.

**Confidence threshold and agreement count are constructor parameters** (defaults: confidence `0.87`, agreement count `3`), consistent with every other unvalidated-but-tunable threshold already in this codebase (`OnnxPlayerDetector`'s confidence/IoU thresholds, `ByteTrackPlayerTracker`'s `maxLostFrames`/thresholds, `throttle-player-detection-cadence`'s cadence `N`).

**Upper-body crop is the top ~40% of the `TrackedPlayer` box height, full width** - a fixed fraction, not a learned region. Jersey numbers sit on the torso; using a fixed top band avoids needing a pose model (see above) while still excluding legs/shorts. This fraction is a named constant, adjustable without a design change if real footage shows it needs tuning.

**Model wiring follows the existing `File.Exists` + `Null*` pattern**, extended to three model paths (`jersey-det.onnx`, `jersey-cls.onnx`, `jersey-rec.onnx` under the existing models directory): `OnnxJerseyNumberRecognizer` is constructed only if all three files exist; otherwise `NullJerseyNumberRecognizer` (always returns no reading) is wired in `App.axaml.cs`, exactly like `IPlayerDetector`/`ICourtKeypointDetector` today.

## Risks / Trade-offs

- [Det/Cls/Rec three-stage cost per player per detection frame] → bounded by the existing detection cadence and by skipping already-locked tracks entirely (see Decisions); if still too slow in practice, the cadence-frame divisor or agreement count are the tuning levers, not a pipeline redesign.
- [Fixed top-40%-of-box crop instead of pose-aware region] → may clip or miss the number on unusual poses (crouching, arms raised); acceptable starting point given no pose model exists, revisit the fraction (not the no-pose-model decision) if observed against real footage.
- [Digit-only whitelist masking assumes a standard CTC-style Rec output] → if the sourced PP-OCRv4 ONNX export's output layer differs from the assumed shape, the masking step needs adjusting; flagged as an implementation-time verification, not a design change.
- [No re-identification across track termination] → a player who leaves and re-enters frame gets a new `TrackId` and starts jersey-number voting over from zero, same as `tracking/player-tracking`'s existing no-re-ID stance; consistent, not a new gap introduced by this change.
- [PP-OCRv4 ONNX export provenance/license unverified at design time] → see Open Questions.

## Open Questions

- Exact source and license of the PP-OCRv4 Det/Cls/Rec ONNX exports to bundle (e.g. a community export vs. exporting from the official PaddleOCR weights directly) - needs verification before the model files are added to the repo/build, but does not change this design, the specs, or the task breakdown.
