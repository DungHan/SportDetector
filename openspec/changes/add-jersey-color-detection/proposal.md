## Why

Every tracked-player marker on the minimap is currently filled with the same fixed placeholder color (`"player"` → `#F2F2F2`, `MarkerStyleToBrushConverter.Convert`) — it conveys "this is a player marker, not a keypoint marker" but nothing about *which team* that player belongs to. `add-player-minimap-projection`'s design explicitly reserved the `CourtMarker`/`AnnotationVisual` `StyleKey` plumbing and its fixed-placeholder-fill decision for this change ("Computing anything track-specific about color is `add-jersey-color-detection`'s job"). This change extracts each tracked player's actual jersey color from their crop and uses it to color that player's own minimap marker, so the minimap starts visually distinguishing the two teams instead of showing every player identically.

## What Changes

- New `NBA.JerseyColor` project: `IJerseyColorExtractor`, reading one track's cropped image region and returning an extracted dominant color + confidence, or "unknown" for a degenerate/empty crop. Unlike `vision/player-detection` or the planned `ocr/jersey-number-recognition`, this is classical pixel analysis (dominant-color histogram over a torso sub-region of the crop), not an ONNX model — so, unlike those two, it ships a real working implementation in this change with no trained-model placeholder gap.
- Per-track temporal smoothing: accumulate each frame's extracted color per `TrackId` and resolve a stable color only once enough samples agree closely enough (adapting `ocr/jersey-number-recognition`'s "don't flicker on one bad frame" posture to a continuous color value instead of a discrete vote). Resolved state is discarded when a track terminates (mirrors `IPlayerTracker`/vote-aggregator termination semantics already established by `tracking/player-tracking` and the planned `ocr/jersey-number-recognition`) — a resolved color is never carried forward to a different track ID.
- `CourtMarker`/`AnnotationVisual` gain a `Color` field (resolved hex color, nullable) alongside the existing `StyleKey`. `MainWindowViewModel.ProcessFrameArrived` crops each tracked player's box, runs it through the extractor, feeds the result into the color resolver, and sets the player marker's `Color` once one resolves; the marker keeps its current fixed placeholder fill until then.
- `MinimapView.axaml`'s marker fill binding prefers a resolved `Color` when present, falling back to the existing `StyleKey`-driven placeholder fill otherwise — extending `MarkerStyleToBrushConverter` rather than replacing it, so keypoint markers (which never carry a `Color`) are unaffected.
- Explicitly out of scope: team assignment/grouping (clustering players' resolved colors into "team A" / "team B"), roster matching, jersey number (`add-jersey-number-ocr`), and coloring the raw-overlay box (only the minimap marker fill changes, matching `add-jersey-number-ocr`'s precedent of only touching the minimap, not the raw overlay).

## Capabilities

### New Capabilities
- `color/jersey-color-detection`: extracting a player's dominant jersey color from a tracked crop, with multi-frame smoothing to produce one stable color per track.

### Modified Capabilities
- `app/dual-view-shell`: the minimap's per-tracked-player marker is filled with that track's resolved jersey color once available, instead of always using the fixed "player" placeholder color.

## Impact

- `src/NBA.JerseyColor/`: new project — `IJerseyColorExtractor`, the dominant-color extraction implementation, and the per-track color resolver/smoother. Registered in `NBA.slnx`.
- `tests/NBA.JerseyColor.Tests/`: new test project (registered in `NBA.slnx`), covering extraction on synthetic crops and the resolver's smoothing/termination behavior with hand-built per-frame sequences — no model or fixture asset needed.
- `src/NBA.App/Models/CourtMarker.cs`, `src/NBA.App/Models/AnnotationVisual.cs`: add nullable `Color` field.
- `src/NBA.App/Converters/MarkerStyleToBrushConverter.cs` (or a new converter alongside it): resolve marker fill from `Color` when present, else fall back to today's `StyleKey` behavior.
- `src/NBA.App/Views/MinimapView.axaml`: marker `Ellipse.Fill` binding updated to consider `Color`.
- `src/NBA.App/ViewModels/MainWindowViewModel.cs`: per-track crop extraction, extractor invocation, resolver wiring, marker `Color` assignment — same `ProcessFrameArrived` region as the planned jersey-number-OCR wiring.
- `src/NBA.App/App.axaml.cs`: construct `IJerseyColorExtractor` and the color resolver and pass them into `MainWindowViewModel`'s constructor (unconditional — no missing-model fallback needed, since this capability has no trained-model dependency).
