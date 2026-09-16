## Purpose

Reads score, clock, and period information from the broadcast scoreboard by cropping a region of the captured frame and running OCR on it, choosing that crop region as precisely as possible so recognition doesn't have to search or guess where the scoreboard is.

## ADDED Requirements

### Requirement: Crop region prefers detected scoreboard-object boxes
The system SHALL choose the scoreboard OCR crop region from the current source's most recent scoreboard-related object detections (`vision/on-court-object-detection`'s `Period`, `Shot Clock`, `Team Name`, `Team Points`, and `Time Remaining` classes) when at least one such detection is available, using the union of their bounding boxes as the crop region, instead of a fixed or manually-configured region.

#### Scenario: One or more scoreboard-related classes were detected
- **WHEN** a scoreboard OCR check runs and the current source has at least one recent detection of `Period`, `Shot Clock`, `Team Name`, `Team Points`, or `Time Remaining`
- **THEN** the system SHALL crop the region bounding the union of those detections' boxes and run OCR on that crop

#### Scenario: Detected scoreboard region updates as detections move
- **WHEN** the most recent scoreboard-related object detections' union bounding box differs from the one used on the previous OCR check
- **THEN** the system SHALL use the updated union bounding box for the current check, rather than continuing to use a stale region

### Requirement: Crop region falls back to the default/manual region when nothing is detected
The system SHALL fall back to the source's manually-configured scoreboard region, or the default bottom-of-frame region when no manual override exists, whenever no scoreboard-related class has any recent detection for the current source.

#### Scenario: No scoreboard-related class has ever been detected for this source
- **WHEN** a scoreboard OCR check runs and the current source has no recent detection of any scoreboard-related class
- **THEN** the system SHALL crop the source's manually-configured scoreboard region if one is set, or the default bottom-of-frame region otherwise, and run OCR on that crop

### Requirement: Recognized text is parsed into a structured scoreboard reading
The system SHALL run OCR on the chosen crop region and parse the recognized text into a structured reading (team codes, scores, game clock, shot clock, where present), independent of which mechanism chose the crop region.

#### Scenario: OCR recognizes scoreboard text
- **WHEN** OCR recognizes one or more lines of text within the chosen crop region
- **THEN** the system SHALL attempt to parse a structured scoreboard reading from that text, using whichever fields it can confidently identify
