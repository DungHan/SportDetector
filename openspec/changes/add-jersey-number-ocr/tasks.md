## 1. Model assets

- [ ] 1.1 Source PP-OCRv4 Det/Cls/Rec ONNX exports (verify license/provenance per design.md's Open Question) and place them in the existing models directory as `jersey-det.onnx`, `jersey-cls.onnx`, `jersey-rec.onnx`; verify each loads via `OnnxModelLoader.Load` without error.

## 2. Recognition pipeline (`src/NBA.Vision`)

- [ ] 2.1 Add `JerseyNumberReading.cs` (digit string + confidence result type).
- [ ] 2.2 Add `IJerseyNumberRecognizer.cs` with a single `Recognize(ReadOnlySpan<byte> bgra8Pixels, int width, int height, int stride) -> JerseyNumberReading?` member, doc-commented as operating on an already-cropped upper-body region (not a full frame).
- [ ] 2.3 Add `NullJerseyNumberRecognizer.cs` (always returns `null`), matching `NullPlayerDetector`'s pattern.
- [ ] 2.4 Implement `OnnxJerseyNumberRecognizer.cs`: three `OnnxModelPipeline` instances (Det, Cls, Rec); Det produces rotated quadrilaterals, Cls corrects orientation, Rec decodes digits with non-digit logits masked out before CTC collapse. Verify with a unit test using a hand-constructed/mocked pipeline (no real model file) asserting the digit-only masking and confidence propagation.
- [ ] 2.5 Add the upper-body crop helper (top ~40% of a box, full width) as a small, independently testable function; verify with unit tests covering box-to-crop-rectangle math (including edge cases like a very short box).
- [ ] 2.6 Wire `App.axaml.cs`: construct `OnnxJerseyNumberRecognizer` when all three model files exist, else `NullJerseyNumberRecognizer`, following the existing `File.Exists` + `Null*` conditional pattern.

## 3. Confidence/format gate and per-track voting

- [ ] 3.1 Add the confidence + 1-2 digit format gate as a small pure function; verify with unit tests covering: high-confidence well-formed reading accepted, low-confidence reading rejected, malformed decoded text rejected regardless of confidence.
- [ ] 3.2 Add `JerseyNumberVoter` (per-`TrackId` cache): accepts `(TrackId, JerseyNumberReading)`, accumulates agreeing readings, locks after the configured agreement count, and exposes the locked number (or "not yet determined") per track; verify with unit tests covering: lock after N agreeing readings, no lock on disagreement below threshold, locked value unaffected by later disagreeing/low-confidence readings, no reading ever recorded before lock is discarded silently.
- [ ] 3.3 Add pruning: given the current frame's live `TrackId` set, remove voter entries for any `TrackId` no longer present; verify with a unit test simulating a track disappearing between frames.

## 4. Pipeline wiring (`MainWindowViewModel`)

- [ ] 4.1 On real detection frames only (the existing `_playerTracker.Update(...)` branch, not `PredictOnly()`), for each `TrackedPlayer` whose jersey number is not yet locked, crop the upper-body region and call the recognizer; feed accepted readings into the voter. Skip recognition entirely for already-locked tracks.
- [ ] 4.2 After processing the frame's tracks, prune the voter using the current frame's live `TrackId` set (task 3.3).
- [ ] 4.3 Render the locked jersey number (when present) alongside the existing track-ID label in the overlay annotation for that track.
- [ ] 4.4 Extend `MainWindowViewModelTests` (or add a new test) asserting: a predicted-only frame does not invoke the recognizer; a real detection frame invokes it once per unlocked track; a locked track's rendered label includes its jersey number.

## 5. Validation

- [ ] 5.1 Run the full test suite for `NBA.Vision`, `NBA.Tracking`/voter, and `NBA.App` and confirm all new and existing tests pass.
- [ ] 5.2 Run the app against a real or recorded broadcast clip with the model files present; confirm jersey numbers appear and stay stable (do not flicker or change) once locked, and that recognition is skipped (no per-frame stutter) on predicted frames.
