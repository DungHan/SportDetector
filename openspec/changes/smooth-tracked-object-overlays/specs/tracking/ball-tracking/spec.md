## Purpose

Smooths `vision/on-court-object-detection`'s per-frame `Ball` detections into a single temporally-filtered position, coasting through brief missed-detection gaps so the ball's on-screen marker neither disappears on a single missed frame nor jumps to a stray false-positive detection elsewhere on court.

## ADDED Requirements

### Requirement: Smooth the ball's reported position across frames
The system SHALL maintain a single filtered position estimate for the `Ball` class, updated from each detection attempt's `Ball` detection (if any), and SHALL report a smoothed position that blends the filtered estimate with the new detection rather than reporting the raw detection verbatim.

#### Scenario: A detection attempt has one Ball detection
- **WHEN** a detection attempt returns exactly one `Ball` detection
- **THEN** the system SHALL update the filtered estimate from it and report a smoothed position blending the filtered estimate with that detection

#### Scenario: A detection attempt has multiple Ball detections
- **WHEN** a detection attempt returns more than one `Ball` detection (e.g. a false positive alongside the real ball)
- **THEN** the system SHALL use only the highest-confidence detection to update the filtered estimate, consistent with reporting one ball position per attempt

### Requirement: Coast through brief missed detections
The system SHALL continue reporting a motion-predicted ball position for a bounded number of consecutive detection attempts with no `Ball` detection, rather than immediately reporting no ball position.

#### Scenario: Consecutive misses stay within the coast bound
- **WHEN** a detection attempt returns no `Ball` detection and the number of consecutive such misses since the last detection is within the configured coast bound
- **THEN** the system SHALL report the motion-predicted ball position rather than reporting no ball

#### Scenario: Consecutive misses exceed the coast bound
- **WHEN** the number of consecutive detection attempts with no `Ball` detection reaches or exceeds the configured coast bound
- **THEN** the system SHALL report no ball position until a new `Ball` detection arrives

### Requirement: Re-acquire immediately without a confirmation delay
Unlike player tracking's multi-frame confirmation gate (which exists to protect a per-player identity from a single spurious detection), the ball has no identity to protect - only a position - so the system SHALL resume reporting from a single new `Ball` detection immediately, with no minimum number of consecutive matching detection attempts required first.

#### Scenario: A new detection arrives after the coast bound was exceeded
- **WHEN** the system is in the no-ball-reported state because the coast bound was exceeded, and a later detection attempt returns a `Ball` detection
- **THEN** the system SHALL immediately reseed the filtered estimate from that detection and resume reporting a position from that same attempt, without waiting for further consecutive detections first
