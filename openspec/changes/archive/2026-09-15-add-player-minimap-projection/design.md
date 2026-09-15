## Context

Builds on three already-existing pieces, unchanged by this design:
- `vision/court-calibration`'s `PointProjector.Project(CalibrationData, ImagePoint) -> CourtPoint` and `CourtCalibrationCoordinator.GetValidCalibration(sourceKey, sport)` (`src/NBA.Vision`).
- `tracking/player-tracking`'s `IPlayerTracker.Update(...) -> IReadOnlyList<TrackedPlayer>` (`TrackId`, `Left`, `Top`, `Right`, `Bottom`, `Confidence`), from `add-bytetrack-tracking`.
- `app/dual-view-shell`'s `MinimapViewModel.SetMarkers(IEnumerable<CourtMarker>)`, where `CourtMarker` (`X`, `Y`, `Label?`, `StyleKey?`) is court-space meters, converted to pixels and flattened into `AnnotationVisual` for `MinimapView.axaml`'s `ItemsControl`.

`MainWindowViewModel.ProcessFrameArrived` already computes both `trackedPlayers` (line ~168, used for the raw overlay's labeled boxes) and, further down in the calibrated branch, `calibration`-projected keypoint `CourtMarker`s (line ~210-215, the only thing ever passed to `Minimap.SetMarkers`). This change inserts a second marker-producing step into that same branch, sourced from `trackedPlayers` instead of `keypoints`, and merges the two into one `SetMarkers` call.

## Goals / Non-Goals

**Goals:**
- Every tracked player visible in the current frame gets one court-space marker on the minimap, at their foot position, updating every frame in lockstep with the raw overlay's boxes (same `trackedPlayers` result already computed — no second detection/tracking pass).
- Visually distinguish player markers from the existing calibration-keypoint markers now that both appear on the minimap at once.
- Establish marker-per-track plumbing (a `StyleKey`, a stable per-track identity via `CourtMarker.Label`) that `add-jersey-number-ocr` and `add-jersey-color-detection` can extend without another pass through `MainWindowViewModel`'s wiring.

**Non-Goals:**
- Jersey number, jersey color, or team assignment — reserved for `add-jersey-number-ocr` / `add-jersey-color-detection`, which this change's `StyleKey`/marker plumbing is built to support.
- Clipping or otherwise handling a projected marker that lands outside the diagram's drawn bounds (e.g., a player near the edge of a poorly calibrated view) — the existing keypoint-marker path has the same characteristic today; not newly introduced by this change, and not addressed here.
- Removing, replacing, or hiding the existing keypoint debug markers — they stay exactly as-is, unrelated to this change; player markers are additive.
- Any smoothing/interpolation of a track's projected position — the marker moves exactly as the underlying box does frame-to-frame.

## Decisions

**Foot point is computed inline in `MainWindowViewModel`, not added to `NBA.Tracking`.** `((Left + Right) / 2, Bottom)` is a minimap-projection concern, not a tracking concern — `TrackedPlayer` stays the plain box+ID+confidence result `add-bytetrack-tracking` deliberately kept minimal (no rendering-specific derived fields).

**Player markers reuse `CourtMarker` and go through the same `Minimap.SetMarkers(...)` call as keypoint markers** (one concatenated list), not a second `Minimap.PlayerMarkers` collection. `MinimapViewModel`/`MinimapView.axaml` already only know about one generic marker collection; a parallel collection would duplicate that plumbing for no behavioral gain in this change's scope.

**`CourtMarker.Label` is set to `$"#{TrackId}"` for a player marker**, mirroring the raw overlay's box label convention, even though `MinimapView.axaml`'s template doesn't render `Label` text today (same as keypoint markers' `LandmarkName`, also stored but unrendered). Kept for parity and debuggability; actually rendering it is explicitly the concern of `add-jersey-number-ocr` (the circle's interior), not this change.

**Visual distinction is a `StyleKey` resolved to a fill color via a converter, not a second `DataTemplate`/`DataTemplateSelector`.** The only thing that varies per marker type today is fill color, so a converter (`StyleKey -> IBrush`) is the smallest change that achieves it. A structurally bigger difference (e.g., a player marker needing a nested `TextBlock` once jersey numbers land) can upgrade this to a `DataTemplateSelector` in `add-jersey-number-ocr` without this change needing to anticipate that shape.

**`AnnotationVisual` (shared by `RawOverlayViewModel.Visuals` and `MinimapViewModel.MarkerVisuals`) gains the `StyleKey` field**, rather than introducing a minimap-only visual type — it is already the single flattening step both view models funnel through. `RawOverlayView.axaml`'s template simply won't bind the new field, so the raw view's rendering is unaffected.

**Fixed placeholder fill for the "player" `StyleKey`** (a single color distinct from the keypoints' existing one), not a computed or randomized per-track color. Computing anything track-specific about color is `add-jersey-color-detection`'s job; this change only needs "player markers are visibly not keypoint markers."

## Risks / Trade-offs

- [No bounds-checking on projected player markers] → a player detected far outside the calibrated court region could project to a point far outside the diagram's drawn area; this is latent behavior the keypoint-marker path already has today, not newly introduced here.
- [Foot point uses the raw box bottom edge, no smoothing] → a track's box jitters frame-to-frame (inherent to per-frame detection + IoU association, not this change), so its minimap marker will jitter too. Acceptable for a first version; revisit only if this proves visibly distracting once tested against real footage.
