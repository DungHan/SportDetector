## Why

`add-bytetrack-tracking` gave every player a stable track ID and box, but that identity only ever reaches the raw overlay view. `MainWindowViewModel.ProcessFrameArrived` already computes `trackedPlayers` every frame and projects the calibration's *detected keypoints* into court-space for the minimap (`PointProjector.Project(calibration, k.Position)` → `Minimap.SetMarkers(...)`), but never does the same for a track's position — the minimap's own spec already promises to "plot the court-space projections of current detections," yet the only thing it has ever plotted is calibration reference points, not a single actual detection. This blocks anything that needs a player's position on the court diagram, and it is a hard prerequisite for the two follow-on changes already planned (`add-jersey-number-ocr`, `add-jersey-color-detection`): both need an existing per-track marker on the minimap before they have anywhere to attach a number or a fill color. This change closes the gap using entirely existing machinery — `PointProjector`/`HomographyCalibrator` (`vision/court-calibration`) already project any image-space point into court-space; the only missing piece is computing a track's foot point and feeding it through the same path keypoints already use.

## What Changes

- In `MainWindowViewModel.ProcessFrameArrived`'s already-calibrated branch, compute each `TrackedPlayer`'s foot point — bottom-center of its box, `((Left + Right) / 2, Bottom)`, as an `ImagePoint` — project it via the existing `PointProjector.Project(calibration, footPoint)`, and add one `CourtMarker` per track to the same `Minimap.SetMarkers(...)` call, concatenated with (not replacing) the existing keypoint markers.
- Give player markers a `StyleKey` ("player") distinct from the keypoint markers, since both now render on the minimap at the same time and need to be visually distinguishable: add a `StyleKey` field to the shared `AnnotationVisual` model (today dropped when `CourtMarker`/`OverlayAnnotation` are flattened into it) and update `MinimapView.axaml`'s marker template to resolve fill color from it (keypoints keep their current color; players get a new, distinct fixed color). No behavior change to `RawOverlayView.axaml` — it doesn't bind the new field.
- Player markers use the same "no valid calibration → no markers" gating the keypoint markers already use; a track's marker disappears the same frame the track disappears (terminated track, or tracker reset on source switch), matching `Minimap.SetMarkers([])` on source switch and the existing empty-tracks behavior already covered for the raw overlay.
- Explicitly out of scope (planned as independent follow-on changes): jersey-number OCR/multi-frame voting (`add-jersey-number-ocr`) and jersey-color extraction (`add-jersey-color-detection`). This change only makes the per-track circle exist and reserves the `StyleKey`/marker-per-track plumbing those changes will build on; it does not render any number or track-specific color.

## Capabilities

### Modified Capabilities
- `app/dual-view-shell`: the minimap's existing "plots the court-space projections of current detections" promise is fulfilled for tracked players specifically (previously only calibration keypoints were ever plotted).

### New Capabilities
(none)

## Impact

- `src/NBA.App/ViewModels/MainWindowViewModel.cs`: extend the calibrated branch of `ProcessFrameArrived` to project each `trackedPlayers` entry's foot point and include it in `Minimap.SetMarkers(...)`.
- `src/NBA.App/Models/AnnotationVisual.cs`: add a `StyleKey` field; `FromAnnotation` and `MinimapViewModel.SetMarkers` pass it through instead of dropping it.
- `src/NBA.App/Views/MinimapView.axaml` (+ a small new converter): marker template's `Ellipse.Fill` resolved from `StyleKey` instead of a single hardcoded color.
- `tests/NBA.App.Tests/MainWindowViewModelTests.cs`: extend the existing calibrated-minimap coverage to assert one additional marker per tracked player at the correct projected court position, that markers disappear when their track disappears, and that no player markers appear without a valid calibration.
