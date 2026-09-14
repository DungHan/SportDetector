## Purpose

Detects the players present in a captured frame as axis-aligned bounding boxes with confidence scores, so downstream features (tracking, jersey recognition, team clustering, court-position projection) have a per-frame set of player locations to consume.

## ADDED Requirements

### Requirement: Detect player bounding boxes per frame
The system SHALL run player detection independently on every captured frame (not cached or reused across frames, unlike per-source calibration and sport classification) and produce zero or more player detections, each with an image-space axis-aligned bounding box and a confidence score.

#### Scenario: Frame contains players above the confidence threshold
- **WHEN** a captured frame is processed and one or more players are detected above the implementation's confidence threshold
- **THEN** the system SHALL return one bounding box and confidence score per detected player

#### Scenario: Frame contains no detectable players
- **WHEN** a captured frame is processed and no players are detected above the confidence threshold
- **THEN** the system SHALL return zero detections for that frame rather than an error

### Requirement: Suppress duplicate overlapping detections
The system SHALL suppress redundant overlapping bounding boxes for what is effectively the same player (non-maximum suppression or equivalent), so a single player is not reported as multiple detections.

#### Scenario: Detector raw output has overlapping boxes for one player
- **WHEN** the underlying model produces multiple high-confidence, heavily-overlapping boxes for the same player
- **THEN** the system SHALL report only the highest-confidence box among them for that player

### Requirement: Graceful degradation without a player-detection model
The system SHALL report zero player detections for every frame, rather than failing or blocking the rest of the pipeline, when no player-detection model is available to load.

#### Scenario: No player-detection model file is present
- **WHEN** the application starts and no player-detection model file is configured or found
- **THEN** the system SHALL report zero player detections for every frame without raising an error visible to the rest of the pipeline

### Requirement: Player detections are rendered on the raw view
The system SHALL make each frame's player detections available as overlay annotations - a point marker at the bounding box's bottom-center (the player's feet), with a label conveying the confidence score - through the existing generic raw-view overlay mechanism, drawn over the live captured frame.

#### Scenario: Players detected in the current frame
- **WHEN** the current frame has one or more player detections
- **THEN** the raw view SHALL render one foot-point marker per detection, positioned at its bounding box's bottom-center, labeled with its confidence score

#### Scenario: No players detected in the current frame
- **WHEN** the current frame has zero player detections
- **THEN** the raw view SHALL render no player markers for that frame
