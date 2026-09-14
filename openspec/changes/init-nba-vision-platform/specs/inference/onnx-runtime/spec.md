## Purpose

Provides a shared, reusable pipeline for loading ONNX models and running inference on captured frames, so every detection model in the system (court keypoints now; player, ball, jersey, OCR later) is executed the same consistent way.

## ADDED Requirements

### Requirement: Load an ONNX model from disk
The system SHALL load a model from a specified `.onnx` file path and report a clear failure if the file is missing, unreadable, or not a valid ONNX model, instead of crashing.

#### Scenario: Loading a valid model
- **WHEN** a valid `.onnx` file path is provided
- **THEN** the system SHALL load the model and make it available to run inference

#### Scenario: Loading a missing or invalid model file
- **WHEN** the provided path does not exist or does not contain a valid ONNX model
- **THEN** the system SHALL surface a descriptive error identifying the path and reason, and SHALL NOT crash the application

### Requirement: Select execution provider with fallback
The system SHALL attempt to run inference on an accelerated execution provider (DirectML on Windows) and SHALL fall back to CPU execution if the accelerated provider is unavailable, without requiring user intervention.

#### Scenario: DirectML available
- **WHEN** a DirectML-capable GPU and drivers are present
- **THEN** the system SHALL run inference using the DirectML execution provider

#### Scenario: DirectML unavailable
- **WHEN** no DirectML-capable device is available
- **THEN** the system SHALL run inference on the CPU execution provider and continue operating rather than failing to start

### Requirement: Run inference on a single frame
The system SHALL accept a single captured frame, run the loaded model's preprocessing, inference, and postprocessing steps, and return structured results (e.g., detected keypoints/boxes with confidence scores) for that frame.

#### Scenario: Successful inference on a frame
- **WHEN** a frame is submitted to a loaded model
- **THEN** the system SHALL return a structured result set containing each detected element's location and confidence score

#### Scenario: Frame does not match expected model input
- **WHEN** a submitted frame has a resolution or format the model's preprocessing cannot normalize automatically
- **THEN** the system SHALL resize/convert the frame as needed before inference rather than failing the request

### Requirement: Report per-frame inference latency
The system SHALL measure and expose the time taken to run inference on each frame, so the application can display performance information and make skip/throttle decisions.

#### Scenario: Latency exposed after inference
- **WHEN** inference completes for a frame
- **THEN** the system SHALL report the elapsed inference time alongside the result
