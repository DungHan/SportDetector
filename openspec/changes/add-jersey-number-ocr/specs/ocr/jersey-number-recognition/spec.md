## Purpose

Recognizes a player's printed jersey number from a tracked player's cropped image region, and resolves a stable per-track number across multiple frames, so downstream features (marker labels, roster/team assignment) have a meaningful player identity beyond an arbitrary track ID.

## ADDED Requirements

### Requirement: Recognize a jersey number from a track's cropped region
The system SHALL attempt jersey-number recognition against each currently tracked player's cropped image region (the track's bounding box) once per processed frame, producing either a recognized number with a confidence score, or an explicit "unrecognized" result.

#### Scenario: Crop contains a legible jersey number
- **WHEN** a tracked player's cropped region is processed and a jersey number is recognized above the implementation's confidence threshold
- **THEN** the system SHALL return that number and its confidence score for the track, for that frame

#### Scenario: Crop does not contain a legible jersey number
- **WHEN** a tracked player's cropped region is processed and no jersey number is recognized above the confidence threshold (e.g. number occluded, player facing away, box too small)
- **THEN** the system SHALL return "unrecognized" for the track, for that frame, rather than a low-confidence guess

### Requirement: Multi-frame voting resolves one stable number per track
The system SHALL accumulate each frame's per-track recognition result and resolve a track's displayed jersey number only once one candidate number has accumulated enough votes, so a single misread frame does not change the track's displayed number.

#### Scenario: A track accumulates enough votes for one number
- **WHEN** a track's accumulated recognition results have one candidate number reaching the required vote threshold
- **THEN** the system SHALL resolve that track's jersey number to the winning candidate

#### Scenario: A track has not yet accumulated enough votes
- **WHEN** a track has been recognized for fewer frames than needed for any candidate to reach the vote threshold, or its votes are split with no single candidate reaching it
- **THEN** the system SHALL report the track as having no resolved jersey number yet, rather than guessing from partial evidence

#### Scenario: A resolved number is not retroactively displaced by a single later disagreement
- **WHEN** a track already has a resolved jersey number and a subsequent frame's recognition result disagrees with it
- **THEN** the system SHALL keep the previously resolved number as the track's displayed number unless the disagreeing candidate itself accumulates enough votes to overtake it

### Requirement: Vote history does not carry across track termination
The system SHALL discard a track's accumulated votes and resolved number when that track terminates, and SHALL NOT apply a terminated track's resolved number to any later track, including one that appears at a similar position or later shows the same number.

#### Scenario: A tracked player's track terminates
- **WHEN** a track terminates (per `tracking/player-tracking`'s track lifecycle)
- **THEN** the system SHALL discard that track's accumulated votes and resolved number

#### Scenario: A new track later shows the same jersey number
- **WHEN** a new track is created after an earlier, unrelated track terminated, and the new track's recognized jersey number matches the terminated track's last resolved number
- **THEN** the system SHALL treat the new track as having no resolved number yet and accumulate its votes independently, rather than reusing the terminated track's resolved number

### Requirement: Graceful degradation without a jersey-number recognition model
The system SHALL report every track as "unrecognized" for every frame, rather than failing or blocking the rest of the pipeline, when no jersey-number recognition model is available to load.

#### Scenario: No jersey-number recognition model file is present
- **WHEN** the application starts and no jersey-number recognition model file is configured or found
- **THEN** the system SHALL report "unrecognized" for every track on every frame without raising an error visible to the rest of the pipeline
