## ADDED Requirements

### Requirement: Exclude non-player persons from detections
The system SHALL report detections only for the underlying model's dedicated player class, configured by class index, so that other on-court persons or objects a multi-class detection model may also emit (e.g. referees/officials, the ball, scoreboard regions) are not reported as player detections.

#### Scenario: Frame contains both players and a referee
- **WHEN** a frame selected for detection (per the configured cadence) contains both players and a referee, and the loaded model distinguishes them as separate classes
- **THEN** the system SHALL return detections only for the player class, excluding the referee

#### Scenario: Configured class index does not match the loaded model
- **WHEN** the configured player-class index does not correspond to a valid class in the loaded model
- **THEN** the system SHALL report zero player detections rather than silently reading an unrelated class's channel

### Requirement: Report an approximate color for each detection
The system SHALL compute and report a plain average color sampled from each detected player's upper-body region, alongside its bounding box and confidence score, for use as an appearance signal by downstream matching.

#### Scenario: Detection with a valid upper-body region
- **WHEN** a player is detected with a bounding box large enough to sample an upper-body region from
- **THEN** the system SHALL report an average color computed from that region alongside the detection's box and confidence

#### Scenario: Detection too small to sample
- **WHEN** a player is detected with a bounding box too small or degenerate to sample a meaningful upper-body region
- **THEN** the system SHALL still report the detection with its box and confidence, using a reported color that downstream consumers can recognize as not meaningful (rather than failing the detection)
