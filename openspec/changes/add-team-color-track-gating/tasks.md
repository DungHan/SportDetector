## 1. Player-class wiring fix

- [x] 1.1 Locate the label metadata bundled with `models/player-detection.onnx` (e.g. `data.yaml`/`classes.txt` from the Roboflow export) and confirm the exact channel index for the `Player` class; verify by inspecting the file rather than assuming display order.
- [x] 1.2 Update `App.axaml.cs`'s `OnnxPlayerDetector` construction to pass the confirmed `Player` class index (rename the constructor parameter from `personClassId` if appropriate); verify by running the app against a real clip and confirming referees no longer produce boxes.
- [x] 1.3 Add a unit test asserting `OnnxPlayerDetector` reads the configured class channel (not a hardcoded one) and returns zero detections when the configured index is out of range for the loaded model's class count.

## 2. Color sampling (`NBA.Vision`)

- [x] 2.1 Add the average-color field to `PlayerDetection.cs`.
- [x] 2.2 Add the upper-body crop-rectangle helper (top ~40% of box height, full width) as a small pure function; verify with unit tests covering normal boxes and small/degenerate boxes.
- [x] 2.3 Implement mean BGR/RGB byte-averaging over that crop region inside `OnnxPlayerDetector.Detect(...)`; verify with a unit test using a synthetic pixel buffer with a known solid color in the crop region, asserting the reported color matches.
- [x] 2.4 Define and document the "not meaningful" color sentinel used when a detection's box is too small/degenerate to sample (per the spec's degenerate-detection scenario); verify with a unit test.

## 3. Association eligibility hook (`NBA.Tracking`)

- [x] 3.1 Add the optional `Func<int,int,bool>? isEligible` parameter to `GreedyIouMatcher.Match`, defaulting to null/always-eligible; verify existing `GreedyIouMatcher` tests still pass unmodified, plus a new test confirming a non-null predicate excludes a pair that would otherwise match by IoU.

## 4. Team-color clustering and per-track state (`ByteTrackPlayerTracker`)

- [x] 4.1 Add a simple 2-means clustering utility over color values (plain Euclidean distance); verify with unit tests covering a clearly-separable two-color set and a near-identical single-color set.
- [x] 4.2 Add the running color EMA field to the private `Track` class, seeded at spawn and updated on every successful match (alongside the existing `Predictor.Correct`/`LostFrames` reset in `ApplyMatch`); verify with a unit test asserting the EMA converges toward repeated matched colors.
- [x] 4.3 Wire the minimum-centroid-separation and EMA-smoothing-factor constructor parameters with starting defaults.
- [x] 4.4 In `Update(...)`, before each association round, compute this frame's 2-means centroids (skipping per the degenerate-frame safeguards when fewer than two detections or centroids too close), and build the `isEligible` closure (nearest-centroid identity comparison, not raw cluster index) passed to `GreedyIouMatcher.Match` for both rounds.
- [x] 4.5 Add unit tests covering the full spec: color-mismatched pair not associated despite IoU overlap; color-matched pair associates normally; veto skipped when fewer than two detections; veto skipped when centroids too close; newly spawned track seeded with its spawning detection's color without the veto applying to the spawn itself.

## 5. Validation

- [x] 5.1 Run the full `NBA.Vision`/`NBA.Tracking` test suites and confirm all new and existing tests pass.
- [ ] 5.2 Run the app against a real or recorded broadcast clip; confirm referees no longer appear as tracked players, and observe whether ID swaps between crossing/nearby players are visibly reduced compared to before this change.
