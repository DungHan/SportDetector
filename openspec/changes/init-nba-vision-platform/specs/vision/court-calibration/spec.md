## Purpose

Derives the mapping between a captured frame's pixel coordinates and real-world playing-surface coordinates, so every other detection (players, ball, and later features) can be projected onto a standard 2D minimap regardless of the source's camera angle. The playing-surface geometry (keypoint layout and real-world dimensions) is selected by the source's classified sport, not hardcoded to one sport.

## ADDED Requirements

### Requirement: Playing-surface geometry is selected by sport
The system SHALL select which playing-surface geometry (named landmarks and their real-world coordinates) to calibrate against based on the current source's sport (from `vision/sport-classification`, automatic or manually overridden), drawn from a registry of geometry definitions keyed by sport. In this change, only the basketball court geometry definition is implemented; the registry mechanism SHALL support adding further sports' geometry later without changing the homography computation logic.

#### Scenario: Source classified as a supported sport
- **WHEN** the current source's sport is basketball
- **THEN** the system SHALL calibrate using the basketball court geometry definition

#### Scenario: Source classified as an unsupported sport
- **WHEN** the current source's sport is recognized but has no registered geometry definition (e.g., soccer, tennis, in this change)
- **THEN** the system SHALL report calibration as unavailable for that source rather than falling back to the basketball geometry

#### Scenario: Source sport is unknown
- **WHEN** the current source has no confident sport classification and none has been manually set
- **THEN** the system SHALL report calibration as unavailable until a sport is determined or manually set

### Requirement: Compute homography from detected court keypoints
The system SHALL compute a homography matrix from a set of detected court keypoints (image-space) matched against their known real-world court-space coordinates, using the landmark set from the selected sport's geometry definition.

#### Scenario: Sufficient keypoints detected
- **WHEN** at least 4 non-collinear court keypoints are detected in a frame with acceptable confidence
- **THEN** the system SHALL compute a homography matrix mapping image coordinates to court coordinates

#### Scenario: Insufficient keypoints detected
- **WHEN** fewer than 4 usable court keypoints are detected
- **THEN** the system SHALL report that calibration is not yet possible for the current frame rather than computing an unreliable homography

### Requirement: Manual calibration fallback
The system SHALL allow a user to manually mark known court reference points — drawn from the selected sport's geometry definition's landmark set — on the current frame and compute a homography from those manual points, as a fallback when automatic keypoint detection is unavailable or unreliable for a given source. Manual calibration SHALL only be available once a supported sport is selected (automatically or manually) for the source.

#### Scenario: User marks reference points manually
- **WHEN** the user clicks at least 4 known court reference locations on the displayed frame and identifies which court landmark each corresponds to
- **THEN** the system SHALL compute a homography from the manually marked points

#### Scenario: User provides fewer than the minimum points
- **WHEN** the user attempts to finish manual calibration with fewer than 4 marked points
- **THEN** the system SHALL reject the calibration attempt and indicate how many more points are needed

### Requirement: Project image-space points to court-space
The system SHALL use the current homography to transform any given image-space point (e.g., a detection's location) into court-space coordinates expressed in the standard court's real-world units.

#### Scenario: Projecting a point with valid calibration
- **WHEN** a homography has been computed for the current source and an image-space point is supplied
- **THEN** the system SHALL return the corresponding court-space coordinates

#### Scenario: Projecting without a valid calibration
- **WHEN** no homography has been computed yet for the current source
- **THEN** the system SHALL indicate that projection is unavailable rather than returning a meaningless coordinate

### Requirement: Calibration is scoped per capture source and sport
The system SHALL treat a computed homography as valid only for the capture source (and camera angle) and the sport it was computed for, and SHALL invalidate the current homography when the capture source changes or when the source's sport classification changes.

#### Scenario: Capture source changes after calibration
- **WHEN** the active capture source is switched to a different window/screen
- **THEN** the system SHALL discard the previous homography and require recalibration (automatic or manual) before projecting points for the new source

#### Scenario: Sport classification changes for the same source
- **WHEN** the sport associated with the current source changes (automatic reclassification or manual override)
- **THEN** the system SHALL discard any homography computed against the previous sport's geometry and require recalibration against the new sport's geometry

### Requirement: Persist and reuse calibration per source
The system SHALL allow a computed homography to be saved and associated with an identifiable source (e.g., a named window/stream), and SHALL offer to reuse a previously saved calibration when the same source is selected again.

#### Scenario: Reselecting a previously calibrated source
- **WHEN** the user selects a capture source that has a previously saved calibration and the frame composition appears unchanged
- **THEN** the system SHALL offer to reuse the saved homography instead of requiring recalibration from scratch
