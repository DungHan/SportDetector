## Purpose

Recognizes the printed jersey number on each tracked player's body from their bounding box crop, and attaches a stable, confidence-gated number to that player's ByteTrack track ID so downstream consumers have a human-meaningful identity beyond a bare integer ID.

## ADDED Requirements

### Requirement: Recognize jersey number from a tracked player's crop
The system SHALL, on frames where player detection actually ran, crop each `TrackedPlayer`'s upper-body region and attempt to recognize a printed jersey number within it, producing a candidate reading (digit string + confidence) or no reading if none is found.

#### Scenario: Detection frame with a legible number
- **WHEN** player detection runs on a frame and a tracked player's upper-body crop contains a clearly legible printed number
- **THEN** the system produces a candidate reading containing the recognized digit string and a confidence score

#### Scenario: Predicted (non-detection) frame
- **WHEN** a frame is handled by motion prediction only (no player detection was attempted, per the existing detection cadence)
- **THEN** the system does not attempt jersey-number recognition on that frame and does not alter any track's existing jersey-number state

### Requirement: Gate candidate readings by confidence and format
The system SHALL discard a candidate reading unless its recognition confidence is at or above a configured threshold AND its decoded digit string is one or two digits long; discarded readings SHALL NOT be recorded against the track.

#### Scenario: High-confidence, well-formed reading
- **WHEN** a candidate reading has confidence at or above the configured threshold and decodes to 1-2 digits
- **THEN** the reading is accepted and passed to the per-track voting requirement

#### Scenario: Low-confidence reading
- **WHEN** a candidate reading's confidence is below the configured threshold
- **THEN** the reading is discarded and does not affect the track's jersey-number state

#### Scenario: Malformed decoded text
- **WHEN** a candidate reading decodes to text that is not 1-2 digits (e.g. empty, more than 2 characters, or non-digit characters)
- **THEN** the reading is discarded regardless of confidence and does not affect the track's jersey-number state

### Requirement: Lock a track's jersey number by multi-reading agreement
The system SHALL maintain a per-track-ID cache of accepted readings and SHALL lock a track's jersey number to a specific digit string only once a configured number of accepted readings for that track agree on the same digit string.

#### Scenario: Reaching agreement threshold
- **WHEN** a track accumulates the configured number of accepted readings that all decode to the same digit string
- **THEN** the track's jersey number becomes locked to that digit string

#### Scenario: Conflicting readings before lock
- **WHEN** a track has accepted readings that disagree with each other and has not yet reached the configured agreement threshold for any single digit string
- **THEN** the track's jersey number remains unlocked (not yet determined)

### Requirement: Locked jersey number is sticky for the track's lifetime
Once a track's jersey number is locked, the system SHALL continue to report that same number for the track for the remainder of the track's lifetime, and SHALL NOT overwrite or clear it based on later discarded, low-confidence, or disagreeing readings.

#### Scenario: Later low-confidence reading after lock
- **WHEN** a track's jersey number is already locked and a subsequent frame's candidate reading for that track is below the confidence threshold or malformed
- **THEN** the track's reported jersey number remains the previously locked value

#### Scenario: Later disagreeing high-confidence reading after lock
- **WHEN** a track's jersey number is already locked and a subsequent accepted reading for that track decodes to a different digit string
- **THEN** the track's reported jersey number remains the previously locked value (the disagreeing reading does not re-open or change the lock)

#### Scenario: Track never reaches lock before termination
- **WHEN** a track is terminated (per the existing occlusion/lifecycle rules) before ever reaching the agreement threshold
- **THEN** the system reports no jersey number for that track for its entire lifetime; no partial or low-confidence guess is exposed
