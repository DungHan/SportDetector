## ADDED Requirements

### Requirement: Veto associations across mismatched team colors
The system SHALL, on frames where per-frame team-color signal is available and informative, refuse to associate a detection with a track when their team-color assignments disagree (compared by which of the frame's two color groups each is nearer to, not by a raw group index) - even when their positions would otherwise satisfy the existing IoU-based matching requirement.

#### Scenario: Two players from different teams cross paths
- **WHEN** a track's motion-predicted box overlaps (by IoU, above threshold) a detection whose sampled color is nearer to the other team's color group than the track's own running color estimate
- **THEN** the system SHALL NOT associate that detection with that track, regardless of the IoU overlap

#### Scenario: Track and detection agree on team color
- **WHEN** a track's motion-predicted box overlaps a detection above the IoU threshold, and both are nearer to the same color group
- **THEN** the system SHALL allow the association to proceed as it would without the color signal

#### Scenario: Too few detections to form a meaningful color split
- **WHEN** a frame has fewer than two player detections
- **THEN** the system SHALL skip the color veto for that frame and associate using IoU alone

#### Scenario: Frame's two color groups are not meaningfully distinct
- **WHEN** a frame's computed two color groups are too close together to represent a real two-team split
- **THEN** the system SHALL skip the color veto for that frame and associate using IoU alone

#### Scenario: Newly spawned track has no color history yet
- **WHEN** a high-confidence detection unmatched in the first association round spawns a new track
- **THEN** the system SHALL seed that track's running color estimate from the spawning detection, without applying the color veto to the spawn itself
