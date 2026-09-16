## Purpose

Detects every on-court/broadcast-overlay object class the player-detection model's multi-class output contains besides `Player` (`Ball`, `Hoop`, `Period`, `Ref`, `Shot Clock`, `Team Name`, `Team Points`, `Time Remaining`), from the same inference pass `vision/player-detection` already runs, and makes each detection visible on the raw view.

## ADDED Requirements

### Requirement: Detect every non-Player class from one shared inference pass
The system SHALL decode detections for every class other than `Player` from the same underlying model inference `vision/player-detection` already runs against a captured/cropped frame, without running a second model load or a second inference pass to obtain them.

#### Scenario: A detection frame is processed
- **WHEN** a captured frame is processed on a detection frame (per `vision/player-detection`'s existing detection cadence)
- **THEN** the system SHALL produce zero or more detections for each non-`Player` class present in that frame, each with an image-space bounding box, class identity, and confidence score, from that same inference pass

#### Scenario: A class has no detections above threshold this frame
- **WHEN** a given non-`Player` class has no detection above the implementation's confidence threshold in the current frame
- **THEN** the system SHALL report zero detections for that class for that frame, rather than an error

### Requirement: Suppress duplicate overlapping detections per class
The system SHALL suppress redundant overlapping bounding boxes for the same physical object within each class independently (non-maximum suppression or equivalent), the same way `vision/player-detection` already does for the `Player` class, so one object is not reported as multiple detections and one class's boxes never suppress another class's boxes.

#### Scenario: One physical object produces multiple overlapping candidate boxes
- **WHEN** the underlying model produces multiple high-confidence, heavily-overlapping candidate boxes for the same class and the same physical object
- **THEN** the system SHALL report only the highest-confidence box among them for that object

#### Scenario: Two different classes' boxes overlap
- **WHEN** a detection of one non-`Player` class spatially overlaps a detection of a different class (or of `Player`) in the same frame
- **THEN** the system SHALL NOT suppress either detection on account of the other - suppression only compares detections within the same class

### Requirement: Non-Player detections are rendered on the raw view
The system SHALL make each frame's non-`Player` detections available as overlay annotations on the raw view - one labeled box per detection, labeled with that detection's class name - through the existing generic raw-view overlay mechanism, alongside tracked-player boxes.

#### Scenario: Non-Player classes are detected in the current frame
- **WHEN** the current frame has one or more detections of any non-`Player` class
- **THEN** the raw view SHALL render one labeled box per such detection, positioned at its bounding box, labeled with its class name

#### Scenario: No non-Player detections in the current frame
- **WHEN** the current frame has zero non-`Player` detections
- **THEN** the raw view SHALL render no non-`Player` boxes for that frame

### Requirement: Graceful degradation without a detection model
The system SHALL report zero detections for every non-`Player` class, rather than failing or blocking the rest of the pipeline, whenever `vision/player-detection` itself is in its own degraded (no-model) state.

#### Scenario: No player-detection model file is present
- **WHEN** the application starts and no player-detection model file is configured or found (per `vision/player-detection`'s existing degraded-path requirement)
- **THEN** the system SHALL report zero detections for every non-`Player` class for every frame without raising an error visible to the rest of the pipeline
