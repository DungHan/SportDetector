## Purpose

Provides the Avalonia application shell that lets a user pick and switch a capture source, calibrate it, and independently view the raw captured frame with detection overlays and/or a 2D court minimap driven by the same underlying data.

## ADDED Requirements

### Requirement: Source selection and switching in the UI
The application SHALL let the user browse and select a capture source from the UI, and SHALL let them switch to a different source at any time while running, reflecting the change in the displayed views without restarting the application.

#### Scenario: Selecting a source on startup
- **WHEN** the user opens the source picker and selects a window or screen
- **THEN** the application SHALL begin displaying frames from that source

#### Scenario: Switching source mid-session
- **WHEN** the user selects a different source while frames are already being displayed
- **THEN** the application SHALL switch the displayed frames to the new source and prompt the user to (re)calibrate for it

### Requirement: Raw view with extensible detection overlay, independently toggleable
The application SHALL provide a raw view showing the captured frame with an overlay layer drawn on top, and SHALL let the user show or hide this view independently of the minimap view. The overlay layer SHALL be driven by a generic collection of annotations (shape + optional label + style), so it can render whatever detection types the current pipeline produces — court keypoints today — without the view itself needing to know about specific detection types added by later phases (e.g., player boxes, jersey number labels).

#### Scenario: Toggling the raw overlay view on
- **WHEN** the user enables the raw overlay view
- **THEN** the application SHALL render the current frame with the latest overlay annotations (e.g., court keypoints/calibration reference points) drawn on top, updating as new frames arrive

#### Scenario: Toggling the raw overlay view off
- **WHEN** the user disables the raw overlay view
- **THEN** the application SHALL stop rendering that view while continuing to process frames for any other enabled view

#### Scenario: Overlay renders a new annotation type without view changes
- **WHEN** the pipeline produces a detection type the overlay layer has not rendered before (e.g., a labeled box instead of a point), expressed using the same generic annotation shape/label/style model
- **THEN** the raw view SHALL render it correctly without requiring changes to the view itself

### Requirement: Court minimap view, independently toggleable
The application SHALL provide a 2D court minimap view that plots the court-space projections of current detections onto a standard court diagram, and SHALL let the user show or hide this view independently of the raw overlay view.

#### Scenario: Toggling the minimap view on
- **WHEN** the user enables the minimap view and a valid calibration exists for the current source
- **THEN** the application SHALL render a standard court diagram with the latest projected points plotted, updating as new frames arrive

#### Scenario: Minimap enabled without calibration
- **WHEN** the user enables the minimap view but no valid calibration exists yet for the current source
- **THEN** the application SHALL show the empty court diagram with a prompt to calibrate, instead of plotting incorrect points

### Requirement: Reserved status/info panel within the minimap view
The minimap view SHALL include a status/info panel region, separate from the court diagram itself, for displaying textual match information (e.g., score, game state). This change does not populate it with real data; the panel SHALL render a placeholder value when no data source is wired up, and SHALL render supplied text when a later capability provides it, without requiring a layout change.

#### Scenario: Info panel with no data source wired up
- **WHEN** the minimap view is enabled and no status/info data source is connected (current state, since score/state detection is a later phase)
- **THEN** the panel SHALL show a placeholder (e.g., "Score: not detected") instead of being blank or omitted

#### Scenario: Info panel receives data from a later capability
- **WHEN** a status/info value becomes available from a data source connected to the panel
- **THEN** the panel SHALL display that value in the same reserved region without any layout change

### Requirement: Default stacked layout
The application SHALL default to a vertically stacked layout with the raw overlay view docked above the minimap view, matching the two views' natural reading order (source frame on top, derived court diagram below).

#### Scenario: Default arrangement on first run
- **WHEN** the application starts with both views enabled and no prior layout preference stored
- **THEN** the raw overlay view SHALL be docked above the minimap view

### Requirement: Both views can run concurrently
The application SHALL support the raw overlay view and the minimap view being visible at the same time (stacked per the default layout), each updating independently from the same underlying frame/detection pipeline.

#### Scenario: Both views enabled simultaneously
- **WHEN** the user enables both the raw overlay view and the minimap view
- **THEN** the application SHALL update both views concurrently from the same detection results for each processed frame

### Requirement: In-app manual calibration workflow
The application SHALL provide an in-app workflow for the manual court calibration fallback, letting the user click reference points on the raw view and identify the corresponding court landmark for each, using the landmark set of the current source's selected sport.

#### Scenario: User completes manual calibration from the UI
- **WHEN** the user starts manual calibration, clicks at least 4 reference points on the raw view, and assigns each a court landmark
- **THEN** the application SHALL submit those points for homography computation and, on success, enable the minimap view to start plotting projected points

### Requirement: Sport indicator and manual override
The application SHALL display the current source's classified (or manually set) sport, and SHALL let the user manually set or override it from the UI.

#### Scenario: Automatic classification shown
- **WHEN** a source has an automatic sport classification result (confident or "unknown")
- **THEN** the application SHALL display that result to the user

#### Scenario: User overrides the sport
- **WHEN** the user manually selects a sport for the current source from the UI
- **THEN** the application SHALL use that sport going forward for the source and, if it differs from the previous sport, SHALL prompt for recalibration

#### Scenario: Manual calibration unavailable for unsupported sport
- **WHEN** the current source's sport is "recognized but not supported" or unknown
- **THEN** the application SHALL disable the manual calibration workflow and indicate why, instead of offering a landmark set that doesn't apply
