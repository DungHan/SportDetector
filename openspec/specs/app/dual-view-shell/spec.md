# dual-view-shell Specification

## Purpose

TBD - Update Purpose after archive. (No prior main spec existed for this capability to carry a Purpose forward from; fill in a real description of the dual raw/minimap view shell.)

## Requirements

### Requirement: Minimap plots tracked players' court positions
The minimap SHALL project each currently tracked player's foot point (the bottom-center of their bounding box) into court-space using the current source's valid calibration, and SHALL plot one marker per track, updating every frame in step with the tracker's output, visually distinguishable from the calibration-keypoint markers already plotted on the minimap. Each player marker SHALL display a visible label: the track's resolved jersey number (`ocr/jersey-number-recognition`) once one exists, falling back to the raw track ID for that track until a jersey number resolves.

#### Scenario: Tracked players are plotted alongside calibration markers
- **WHEN** a valid calibration exists for the current source/sport and the tracker reports one or more tracked players for the current frame
- **THEN** the minimap SHALL show one marker per tracked player, positioned at that track's projected foot point, in addition to any calibration-keypoint markers already shown

#### Scenario: A track's marker disappears with the track
- **WHEN** a previously tracked player's track is no longer present in the tracker's output for the current frame (terminated, or the tracker was reset by a source switch)
- **THEN** the minimap SHALL no longer show a marker for that track on the next rendered frame

#### Scenario: No player markers without a valid calibration
- **WHEN** no valid calibration exists yet for the current source (per the existing "Minimap enabled without calibration" scenario)
- **THEN** the minimap SHALL NOT plot any player markers, consistent with not plotting incorrect points

#### Scenario: Player marker shows the track ID before a jersey number resolves
- **WHEN** a tracked player's marker is plotted and jersey-number recognition has not yet resolved a stable number for that track
- **THEN** the minimap SHALL label that marker with the track's raw track ID

#### Scenario: Player marker shows the resolved jersey number once available
- **WHEN** a tracked player's marker is plotted and jersey-number recognition has resolved a stable number for that track
- **THEN** the minimap SHALL label that marker with the resolved jersey number instead of the track ID
