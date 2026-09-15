## MODIFIED Requirements

### Requirement: Tolerate brief occlusion without losing identity
The system SHALL keep a track alive, predicting its position via its motion model, for a bounded number of consecutive detection attempts with no matched detection, before terminating it. Frames on which detection did not run (per the configured detection cadence) SHALL NOT count toward this bound, since no detection was attempted on those frames.

#### Scenario: A track has no matching detection for fewer detection attempts than the occlusion buffer
- **WHEN** a track goes unmatched for a number of consecutive detection attempts less than the configured occlusion buffer
- **THEN** the system SHALL keep the track alive at its motion-predicted position and continue attempting to match it against future detection attempts

#### Scenario: A track has no matching detection for longer than the occlusion buffer
- **WHEN** a track goes unmatched for a number of consecutive detection attempts at or beyond the configured occlusion buffer
- **THEN** the system SHALL terminate the track and never reuse its track ID for a different, later track

#### Scenario: A frame arrives between detection attempts
- **WHEN** a frame arrives on which detection did not run (per the configured detection cadence)
- **THEN** the system SHALL NOT increment any track's unmatched-attempt count for that frame

## ADDED Requirements

### Requirement: Advance motion prediction without detection
The system SHALL provide a way to advance every live track's motion-predicted position by one frame-step and report the resulting boxes, without attempting association against any detections, without affecting any track's occlusion-buffer count, and without creating or terminating tracks - for use on frames where detection did not run.

#### Scenario: Motion is advanced on a frame with no detection attempt
- **WHEN** the system advances motion prediction for a frame on which detection did not run
- **THEN** every live track SHALL report its motion-predicted box for that frame, keeping its existing track ID, with its occlusion-buffer count unchanged

#### Scenario: No live tracks exist when motion is advanced
- **WHEN** the system advances motion prediction and there are no live tracks
- **THEN** the system SHALL report zero tracks for that frame rather than an error
