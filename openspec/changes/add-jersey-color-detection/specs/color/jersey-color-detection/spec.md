## Purpose

Extracts a player's dominant jersey color from a tracked player's cropped image region, and resolves a stable per-track color across multiple frames, so downstream features (minimap marker fill, future team grouping) have a meaningful per-player color beyond a fixed placeholder.

## ADDED Requirements

### Requirement: Extract a dominant jersey color from a track's cropped region

The system SHALL attempt jersey-color extraction against each currently tracked player's cropped image region (the track's bounding box) once per processed frame, producing either an extracted color with a confidence score, or an explicit "unknown" result.

#### Scenario: Crop contains a legible jersey region

- **WHEN** a tracked player's cropped region is processed and a dominant color is extracted above the implementation's confidence threshold
- **THEN** the system SHALL return that color and its confidence score for the track, for that frame

#### Scenario: Crop is degenerate or too small to sample

- **WHEN** a tracked player's cropped region is empty, out of frame bounds, or too small to sample a meaningful color (e.g. clamped to zero width or height)
- **THEN** the system SHALL return "unknown" for the track, for that frame, rather than a meaningless guess

### Requirement: Multi-frame smoothing resolves one stable color per track

The system SHALL accumulate each frame's per-track extracted color and resolve a track's displayed jersey color only once enough recent samples agree closely enough, so a single frame's noisy or partially-occluded read does not visibly change the track's displayed color.

#### Scenario: A track accumulates enough agreeing samples

- **WHEN** a track's recent extracted colors are close enough to each other for the required number of samples
- **THEN** the system SHALL resolve that track's jersey color to a representative color from those samples

#### Scenario: A track has not yet accumulated enough agreeing samples

- **WHEN** a track has been extracted for fewer frames than needed, or its recent samples disagree too much to settle on one representative color
- **THEN** the system SHALL report the track as having no resolved jersey color yet, rather than guessing from partial or conflicting evidence

#### Scenario: A resolved color is not retroactively replaced by a single later outlier

- **WHEN** a track already has a resolved jersey color and a subsequent frame's extracted color disagrees with it
- **THEN** the system SHALL keep the previously resolved color as the track's displayed color unless enough subsequent samples consistently support a different color

### Requirement: Resolved color does not carry across track termination

The system SHALL discard a track's accumulated color samples and resolved color when that track terminates, and SHALL NOT apply a terminated track's resolved color to any later track, including one that appears at a similar position or later shows a similar jersey color.

#### Scenario: A tracked player's track terminates

- **WHEN** a track terminates (per `tracking/player-tracking`'s track lifecycle)
- **THEN** the system SHALL discard that track's accumulated color samples and resolved color

#### Scenario: A new track later shows a similar jersey color

- **WHEN** a new track is created after an earlier, unrelated track terminated, and the new track's extracted jersey color is similar to the terminated track's last resolved color
- **THEN** the system SHALL treat the new track as having no resolved color yet and accumulate its samples independently, rather than reusing the terminated track's resolved color
