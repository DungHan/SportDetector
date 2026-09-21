## ADDED Requirements

### Requirement: Ball detections are rendered from ball-tracking's smoothed output
For the `Ball` class specifically, the raw-view box and any other rendering of the ball's position SHALL come from `tracking/ball-tracking`'s smoothed/coasted output for the current detection attempt, rather than that attempt's raw `Ball` detections directly. Every other non-`Player` class is unaffected by this requirement and continues to render directly from that attempt's raw detections.

#### Scenario: A detection attempt has a Ball detection
- **WHEN** the current detection attempt includes a `Ball` detection
- **THEN** the raw view SHALL render the `Ball` box at `tracking/ball-tracking`'s smoothed position for that attempt, not the raw detection box directly

#### Scenario: A detection attempt is missing a Ball detection but ball-tracking is still coasting
- **WHEN** the current detection attempt has no raw `Ball` detection but `tracking/ball-tracking` is still within its coast bound
- **THEN** the raw view SHALL still render the `Ball` box at the coasted position

#### Scenario: Ball-tracking reports no ball position
- **WHEN** `tracking/ball-tracking` reports no ball position (coast bound exceeded, or no `Ball` detection has ever been seen)
- **THEN** the raw view SHALL render no `Ball` box for that frame
