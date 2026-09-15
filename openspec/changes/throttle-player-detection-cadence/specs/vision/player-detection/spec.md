## MODIFIED Requirements

### Requirement: Detect player bounding boxes per frame
The system SHALL run player detection on a configurable cadence of captured frames (every Nth frame, not necessarily every frame) and produce zero or more player detections, each with an image-space axis-aligned bounding box and a confidence score, for each frame on which detection runs.

#### Scenario: Frame contains players above the confidence threshold
- **WHEN** a frame selected for detection (per the configured cadence) is processed and one or more players are detected above the implementation's confidence threshold
- **THEN** the system SHALL return one bounding box and confidence score per detected player

#### Scenario: Frame contains no detectable players
- **WHEN** a frame selected for detection (per the configured cadence) is processed and no players are detected above the confidence threshold
- **THEN** the system SHALL return zero detections for that frame rather than an error

#### Scenario: Frame falls between detection cadence steps
- **WHEN** a captured frame arrives that does not fall on the configured detection cadence
- **THEN** the system SHALL NOT run player detection for that frame, relying instead on the tracker's motion-predicted positions (see `tracking/player-tracking`) to represent players for that frame
