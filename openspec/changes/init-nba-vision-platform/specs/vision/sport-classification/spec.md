## Purpose

Determines which sport a capture source is currently showing, so the rest of the pipeline (court geometry, and later phases' detection model sets) can select the correct sport-specific configuration automatically instead of assuming a single fixed sport.

## ADDED Requirements

### Requirement: Classify the sport shown by a capture source
The system SHALL classify which sport a capture source is currently showing from its captured frames, using a classifier trained for whole-scene broadcast/game frames (not a raw, un-adapted general-purpose image classifier applied zero-shot), and SHALL report a confidence score alongside the result.

#### Scenario: Confident classification of a supported sport
- **WHEN** classification is run on a source showing basketball with clearly visible court/gameplay
- **THEN** the system SHALL report the sport as basketball with a confidence score

#### Scenario: Low-confidence classification
- **WHEN** classification confidence falls below the system's acceptance threshold
- **THEN** the system SHALL report the sport as unknown rather than committing to a low-confidence guess

### Requirement: Sport registry is extensible without changing the classifier's interface
The system SHALL maintain a registry of supported sports that can be extended by adding a new sport entry, without changing the classification interface or the code that consumes a classification result. Only basketball SHALL be a fully registered/supported sport in this change.

#### Scenario: Classifier recognizes a sport not yet in the registry
- **WHEN** the classifier identifies a sport (e.g., soccer, tennis) that is not yet a registered/supported entry
- **THEN** the system SHALL report the source as "recognized but not supported" and SHALL NOT apply basketball's configuration to it

#### Scenario: Adding a new supported sport later
- **WHEN** a new sport is added to the registry in a future change
- **THEN** it SHALL NOT require changes to the classification pipeline or to code that reads a classification result — only a new registry entry and its own configuration

### Requirement: Classification result is cached per source, not re-run every frame
The system SHALL classify a source's sport at most once per source selection (reusing the cached result from `SourceProfile` on subsequent frames), rather than running classification on every frame, and SHALL allow the cached result to be re-checked or manually overridden.

#### Scenario: Reselecting a previously classified source
- **WHEN** the user selects a capture source that already has a cached sport classification in its `SourceProfile`
- **THEN** the system SHALL reuse the cached classification instead of re-running the classifier

#### Scenario: Manual override of classification
- **WHEN** the user manually sets the sport for the current source (e.g., because the automatic classification was wrong or below threshold)
- **THEN** the system SHALL use the manually set sport for that source going forward and SHALL persist it in the source's `SourceProfile`, taking precedence over the automatic classifier's result
