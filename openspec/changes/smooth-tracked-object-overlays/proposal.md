## Why

Both the player boxes and the ball marker visibly jitter/drift on screen even though detection itself is working: `ByteTrackPlayerTracker` runs a Kalman motion predictor per track but discards its filtered estimate and reports the raw per-frame detection box instead, so a track's displayed box snaps around with every frame's raw YOLO noise. The ball has no tracker at all — the raw view and minimap both render whichever frame's highest-confidence `Ball` detection happened to come back, so a single missed frame makes the marker vanish and a single false positive elsewhere on court makes it jump. Both are the same underlying gap: position drawn straight from a noisy single-frame detector output, with no temporal filtering.

## What Changes

- `tracking/player-tracking` (modified): a confirmed track's reported box is a smoothed estimate (blended from the Kalman-filtered/predicted box and the current frame's raw detection box) instead of the raw detection box verbatim, damping frame-to-frame jitter while still tracking real motion without a perceptible lag.
- `tracking/ball-tracking` (new): a single-object tracker for the `Ball` class - smooths the reported position (same predict/correct + blend approach as the player-tracking change above, minus multi-track association since there is at most one ball) and coasts on its last known motion-predicted position for a short, bounded number of consecutive missed detections instead of disappearing immediately, so a single dropped frame doesn't blank the marker. A miss beyond that bound reports no ball, same as today.
- `vision/on-court-object-detection` (new requirement added to this not-yet-archived capability): the raw-view box and minimap marker for the `Ball` class are sourced from `tracking/ball-tracking`'s smoothed/coasted output instead of the current frame's raw `Ball` detections directly. Every other non-`Player` class (`Hoop`, `Period`, `Ref`, etc.) is unaffected - they keep rendering straight from that frame's raw detections, since they aren't the "drifting" complaint and don't need identity over time.

## Capabilities

### New Capabilities
- `tracking/ball-tracking`: Smooths per-frame `Ball` detections into a single filtered position over time, coasting through brief missed-detection gaps instead of the marker disappearing or jumping.

### Modified Capabilities
- `tracking/player-tracking`: A confirmed track's reported box position is now a smoothed estimate rather than the raw per-frame detection box. (Written as an ADDED requirement in this change's delta - `tracking/player-tracking` has no archived main spec yet, so there is nothing existing to modify a delta against.)

Note: `tracking/player-tracking` and `vision/on-court-object-detection` were introduced by earlier changes (`add-bytetrack-tracking` et al., `add-multiclass-object-detection`) that are implemented in code but were never archived, so neither has a main spec under `openspec/specs/` yet. This change's deltas for both are therefore written as `ADDED Requirements` (new, uniquely-named requirements) rather than `MODIFIED Requirements`, to keep `openspec validate --strict` clean given the missing baseline - not because these are newly-introduced capabilities.

## Impact

- `src/NBA.Tracking/ByteTrackPlayerTracker.cs`: `ApplyMatch`/`Track` gain a smoothed reported-box calculation (blend of `Predictor`'s filtered box and the raw detection box) instead of assigning `LastBox = box` verbatim.
- `src/NBA.Tracking/`: new `IBallTracker`/`BallTracker` (or similarly named) single-object tracker, reusing the existing per-axis motion predictor used by `ByteTrackPlayerTracker` where practical.
- `src/NBA.App/App.axaml.cs`: construct and wire the new ball tracker alongside the existing player tracker.
- `src/NBA.App/ViewModels/MainWindowViewModel.cs`: feed each detection frame's raw `Ball` detections through the new tracker; source the `Ball` raw-view annotation and minimap `ballMarker` from its output instead of `_lastOtherDetections` directly (other non-`Player` classes keep reading `_lastOtherDetections` as today).
- `tests/NBA.Tracking.Tests/`: new tests for the ball tracker (smoothing, coast-on-miss, miss-beyond-bound reports nothing); extend `ByteTrackPlayerTrackerTests` for smoothed-box reporting.
- `tests/NBA.App.Tests/MainWindowViewModelTests.cs`: extend ball-marker coverage for the coast-through-a-missed-frame case.
