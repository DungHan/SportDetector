## Purpose

Provides a continuous stream of frames captured from a user-selected window or screen source (a game client or a browser playing a YouTube video), so downstream inference and rendering always have a current frame to work from.

## ADDED Requirements

### Requirement: Enumerate capturable sources
The system SHALL enumerate available capture sources — open top-level windows and connected screens/monitors — and present each with a human-readable label (window title or monitor name) so the user can pick one.

#### Scenario: Listing sources with open windows
- **WHEN** the user opens the source selection list
- **THEN** the system SHALL show every currently open top-level window and every connected monitor, each with a distinguishing label

#### Scenario: Source closed since last enumeration
- **WHEN** a previously listed window has been closed
- **THEN** the system SHALL exclude it from the next enumeration instead of returning a stale entry

### Requirement: Start capture from a selected source
The system SHALL start producing a frame stream from the window or screen the user selects, without requiring an application restart.

#### Scenario: Start capture on a window source
- **WHEN** the user selects an open window as the capture source
- **THEN** the system SHALL begin delivering frames captured from that window's client area

#### Scenario: Start capture on a screen source
- **WHEN** the user selects a monitor as the capture source
- **THEN** the system SHALL begin delivering frames captured from that monitor's full output

### Requirement: Switch capture source at runtime
The system SHALL allow the active capture source to be changed while the application is running, replacing the frame stream with one from the newly selected source without an application restart.

#### Scenario: Switching from one window to another
- **WHEN** the user selects a different source while a capture session is already active
- **THEN** the system SHALL stop delivering frames from the previous source and begin delivering frames from the newly selected source within one second, without restarting the application

#### Scenario: Switching source clears stale downstream state
- **WHEN** the capture source changes
- **THEN** the system SHALL signal downstream consumers (calibration, overlay) that the source changed, so a homography computed for the previous source is not silently applied to frames from the new one

### Requirement: Resilience to source becoming unavailable
The system SHALL detect when the active capture source becomes unavailable (window closed, monitor disconnected) and report a stopped state rather than deliver stale or corrupt frames.

#### Scenario: Captured window is closed
- **WHEN** the window currently being captured is closed by the user or OS
- **THEN** the system SHALL stop the frame stream and report a "source lost" state instead of repeating the last frame indefinitely

### Requirement: Frame delivery keeps pace with latest frame
The system SHALL always make the most recently captured frame available to consumers and SHALL NOT force consumers to process every intermediate frame if they fall behind.

#### Scenario: Consumer is slower than the capture rate
- **WHEN** the inference pipeline takes longer to process a frame than the interval between captures
- **THEN** the system SHALL drop intermediate frames and hand the consumer the latest available frame rather than queueing every frame captured
