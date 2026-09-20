## ADDED Requirements

### Requirement: Delay track visibility until confirmed
The system SHALL treat a newly spawned track as an unconfirmed candidate until it has matched a detection on a configured number of consecutive detection attempts starting from its spawning attempt. An unconfirmed candidate SHALL NOT be included in any track list the system reports to a caller, and SHALL NOT expose a track ID to a caller, until it becomes confirmed. A candidate that fails to match on any detection attempt before reaching that count SHALL be discarded immediately, without entering the occlusion-buffer path used for confirmed tracks, and its would-be track ID SHALL never be assigned to a later track.

#### Scenario: A single spurious detection never becomes visible
- **WHEN** a detection with no real corresponding player spawns a candidate track, and that candidate is unmatched on the very next detection attempt
- **THEN** the system SHALL discard the candidate without ever including it in a reported track list

#### Scenario: A candidate matches on every attempt through confirmation
- **WHEN** a newly spawned candidate matches a detection on each consecutive detection attempt up to the configured confirmation count
- **THEN** the system SHALL include it in the reported track list from that point on, with a stable track ID, and SHALL continue tracking it exactly as any other confirmed track

#### Scenario: A candidate breaks its matching streak before confirmation
- **WHEN** a newly spawned candidate fails to match a detection on any attempt before reaching the configured confirmation count
- **THEN** the system SHALL discard the candidate at that point rather than retrying it against later detection attempts or aging it through the occlusion buffer

#### Scenario: Confirmed tracks are unaffected by the confirmation mechanism
- **WHEN** a track has already been confirmed
- **THEN** its subsequent occlusion handling, association, and termination SHALL behave exactly as it would without this requirement

### Requirement: Suppress duplicate tracks representing the same player
After association and any new-track spawning for a detection attempt complete, the system SHALL compare every pair of live tracks (confirmed or unconfirmed candidate) by IoU of their current boxes. When a pair's IoU is at or above a configured near-total-overlap threshold, the system SHALL keep the track with the longer unbroken matched history and terminate the other, releasing the terminated track's ID such that it is never reused.

#### Scenario: A coasting track and a freshly spawned track converge on the same player
- **WHEN** a track coasting on its motion prediction and a separately spawned track both currently occupy boxes that overlap at or above the duplicate-overlap threshold
- **THEN** the system SHALL terminate the track with the shorter matched history and continue reporting only the other

#### Scenario: An unconfirmed candidate duplicates an already-established track
- **WHEN** a newly spawned unconfirmed candidate's box overlaps an existing confirmed track's box at or above the duplicate-overlap threshold
- **THEN** the system SHALL discard the candidate (it never reaches confirmation) and continue reporting only the established track

#### Scenario: Two tracks with ordinary spacing are unaffected
- **WHEN** two live tracks' current boxes overlap below the duplicate-overlap threshold
- **THEN** the system SHALL NOT terminate either track as a result of this requirement

#### Scenario: Deterministic tie-break on equal history
- **WHEN** two tracks whose boxes overlap at or above the duplicate-overlap threshold have identical matched-history length
- **THEN** the system SHALL deterministically keep the same one of the pair every time given the same inputs (e.g. by lower track ID), rather than making a non-deterministic or order-dependent choice
