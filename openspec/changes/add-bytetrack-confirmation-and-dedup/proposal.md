## Why

`ByteTrackPlayerTracker` (`add-bytetrack-tracking`) already implements ByteTrack's core two-round high/low-confidence association plus a motion-prediction occlusion buffer, and `add-team-color-track-gating` layered a team-color veto on top. Comparing it against the reference ByteTrack implementation (the `supervision` library, as used by `roboflow/sports`) surfaces two stabilization mechanisms this project's tracker does not yet have, both of which map directly onto observed failure modes:

- **A single-frame false-positive detection immediately becomes a permanent track ID.** A new track is spawned and returned to the caller from one high-confidence detection in round one, with no minimum survival streak. A one-off misfire (crowd, ad-board, ball momentarily boxed as a player) therefore shows up as a fully-labeled tracked player for at least one frame, and consumes a track ID that is never reused (`add-bytetrack-tracking`'s "Track IDs are never reused" requirement), leaving a permanent gap in the ID sequence for a detection that was never a real player.
- **Nothing detects when two live tracks have converged onto the same physical player.** A track coasting on its motion prediction after a missed match, and a track freshly spawned nearby (e.g. from a detection whose IoU with the coasting track's drifted prediction fell just under threshold), can end up representing the same player with two different IDs simultaneously, both rendered until one of them ages out via `maxLostFrames`.

Upstream ByteTrack addresses both with `minimum_consecutive_frames` (a track is only externally visible once it has matched for N consecutive frames) and `remove_duplicate_tracks` (an end-of-frame pass that collapses two near-fully-overlapping tracks down to the older one). This change ports both mechanisms into `ByteTrackPlayerTracker`, consistent with how `add-team-color-track-gating` previously layered a new mechanism onto the same class without disturbing its existing two-round matching or occlusion handling.

## What Changes

- Add a confirmation requirement to new tracks: a track spawned from an unmatched high-confidence detection is only included in `Update`'s/`PredictOnly`'s returned track list, and only reports a stable track ID to the caller, after it has matched on a configured number of consecutive `Update` calls immediately following its spawn. A candidate that fails to match on any of those calls is discarded outright (not aged through the normal `maxLostFrames` lost-track path) and never reuses a track ID slot.
- Add a post-association duplicate-track suppression pass: after each `Update` call's association rounds (and new-track spawning) complete, compare every pair of live tracks by IoU; when two tracks' current boxes overlap above a configured near-total-overlap threshold, keep the one with the earlier spawn time (more consecutive matched history) and terminate the other, releasing it exactly like a normal occlusion-buffer termination (its ID is never reused).
- Both new thresholds (`minimumConsecutiveFrames`, `duplicateIouThreshold`) are constructor parameters, following this class's existing convention (`highConfidenceThreshold`, `maxLostFrames`, etc. are all constructor parameters, not hardcoded).

## Capabilities

### Modified Capabilities

- `tracking/player-tracking`: adds a confirmation delay before a newly spawned track becomes visible/ID-stable, and adds a duplicate-track suppression pass after association. Note: this capability's spec has not yet been archived into `openspec/specs/` — its current requirements are spread across three completed, unarchived changes (`add-bytetrack-tracking`, `add-team-color-track-gating`, `throttle-player-detection-cadence`), each contributing an unmerged delta under `openspec/changes/*/specs/tracking/player-tracking/spec.md`. This change's delta spec is written against the union of those three deltas as the effective current behavior.

## Impact

- `src/NBA.Tracking/ByteTrackPlayerTracker.cs`: new confirmation-tracking state per `Track`, a new duplicate-suppression step in `Update`, two new constructor parameters.
- `tests/NBA.Tracking.Tests/ByteTrackPlayerTrackerTests.cs`: new coverage for confirmation delay and duplicate suppression, plus verification that existing behaviors (two-round matching, occlusion buffer, team-color veto, ID-never-reused) are unaffected.
- No change to `IPlayerTracker`'s interface shape, `MainWindowViewModel` wiring, or overlay rendering — `TrackedPlayer`'s output contract is unchanged, only which tracks appear in it and when.
