## Purpose

Associates each frame's independent player detections into persistent tracks — one stable ID per physical player across frames — so downstream features that need identity over time (jersey-number recognition via multi-frame voting, per-player stats, team assignment) have something to key off of, instead of unrelated per-frame boxes.

## ADDED Requirements

### Requirement: Assign stable track IDs across frames
The system SHALL associate each frame's player detections with existing tracks (or create new tracks for unmatched detections) using motion-predicted position and IoU overlap, so the same physical player keeps the same track ID across consecutive frames.

#### Scenario: A detection matches a track's predicted position
- **WHEN** a frame's detection overlaps (by IoU, above threshold) the motion-predicted box of an existing track
- **THEN** the system SHALL update that track with the new detection and keep its existing track ID

#### Scenario: A detection does not match any existing track
- **WHEN** a frame's detection does not overlap any existing track's predicted box above threshold
- **THEN** the system SHALL create a new track with a new, previously-unused track ID

### Requirement: Two-round association using both confidence tiers
The system SHALL run a first association round using only high-confidence detections, then a second round using low-confidence detections (already above `IPlayerDetector`'s own reporting threshold, but below this tracker's high-confidence split) matched only against tracks still unmatched after the first round.

#### Scenario: A track is occluded and its detection drops to low confidence
- **WHEN** an existing track's player is briefly occluded and the current frame's matching detection falls below the high-confidence threshold but is still returned by the detector
- **THEN** the system SHALL match that low-confidence detection to the track in the second round and keep the track's identity, rather than treating the track as unmatched

#### Scenario: A low-confidence detection does not match any existing track
- **WHEN** a low-confidence detection does not overlap any track still unmatched after the first round
- **THEN** the system SHALL discard that detection rather than creating a new track from it

### Requirement: Tolerate brief occlusion without losing identity
The system SHALL keep a track alive, predicting its position via its motion model, for a bounded number of consecutive frames with no matched detection, before terminating it.

#### Scenario: A track has no matching detection for fewer frames than the occlusion buffer
- **WHEN** a track goes unmatched for a number of consecutive frames less than the configured occlusion buffer
- **THEN** the system SHALL keep the track alive at its motion-predicted position and continue attempting to match it against future frames' detections

#### Scenario: A track has no matching detection for longer than the occlusion buffer
- **WHEN** a track goes unmatched for a number of consecutive frames at or beyond the configured occlusion buffer
- **THEN** the system SHALL terminate the track and never reuse its track ID for a different, later track

### Requirement: Track IDs are never reused
The system SHALL never assign a terminated track's ID to a different track, even if a similar detection later appears in roughly the same position.

#### Scenario: A new player appears where a terminated track used to be
- **WHEN** a track has been terminated and a later frame's detection appears at a similar position
- **THEN** the system SHALL assign a new track ID rather than reviving the terminated track's ID
