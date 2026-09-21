## ADDED Requirements

### Requirement: Report a smoothed track position
The system SHALL report a confirmed track's box as a smoothed estimate that blends the track's motion-predicted/filtered position with the current detection attempt's matched raw detection box, rather than the raw detection box verbatim, so per-frame detector noise is damped without introducing perceptible lag behind the player's real motion.

#### Scenario: A matched detection differs slightly from the predicted box
- **WHEN** a track matches a detection whose box differs from the track's motion-predicted box by an amount consistent with ordinary per-frame detector noise
- **THEN** the system SHALL report a blended/smoothed box between the predicted and detected boxes, not the raw detection box unmodified

#### Scenario: A track's real position moves steadily across frames
- **WHEN** a track matches detections that consistently move in one direction across consecutive detection attempts
- **THEN** the system SHALL report a smoothed position that follows that real motion without a visible lag that would make the reported box trail behind the actual detection
