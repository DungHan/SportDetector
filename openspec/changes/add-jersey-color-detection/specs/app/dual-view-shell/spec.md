## MODIFIED Requirements

### Requirement: Minimap plots tracked players' court positions

The minimap SHALL project each currently tracked player's foot point (the bottom-center of their bounding box) into court-space using the current source's valid calibration, and SHALL plot one marker per track, updating every frame in step with the tracker's output, visually distinguishable from the calibration-keypoint markers already plotted on the minimap. Each player marker SHALL be filled with that track's resolved jersey color (`color/jersey-color-detection`) once one exists, falling back to a fixed placeholder fill for that track until a color resolves.

#### Scenario: Tracked players are plotted alongside calibration markers

- **WHEN** a valid calibration exists for the current source/sport and the tracker reports one or more tracked players for the current frame
- **THEN** the minimap SHALL show one marker per tracked player, positioned at that track's projected foot point, in addition to any calibration-keypoint markers already shown

#### Scenario: A track's marker disappears with the track

- **WHEN** a previously tracked player's track is no longer present in the tracker's output for the current frame (terminated, or the tracker was reset by a source switch)
- **THEN** the minimap SHALL no longer show a marker for that track on the next rendered frame

#### Scenario: No player markers without a valid calibration

- **WHEN** no valid calibration exists yet for the current source (per the existing "Minimap enabled without calibration" scenario)
- **THEN** the minimap SHALL NOT plot any player markers, consistent with not plotting incorrect points

#### Scenario: Player marker uses the placeholder fill before a jersey color resolves

- **WHEN** a tracked player's marker is plotted and jersey-color detection has not yet resolved a stable color for that track
- **THEN** the minimap SHALL fill that marker with the existing fixed placeholder color

#### Scenario: Player marker uses the resolved jersey color once available

- **WHEN** a tracked player's marker is plotted and jersey-color detection has resolved a stable color for that track
- **THEN** the minimap SHALL fill that marker with the resolved color instead of the placeholder color
