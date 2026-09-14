## Context

Greenfield repository. See proposal.md for motivation. This design covers the .NET solution structure and the five foundation capabilities (`capture/frame-capture`, `inference/onnx-runtime`, `vision/sport-classification`, `vision/court-calibration`, `app/dual-view-shell`), plus one cross-cutting pattern (per-source profile persistence) that later phases (player/ball/jersey/score, and additional sports) are expected to reuse rather than reinvent.

The user separately asked whether the system should eventually support sports beyond basketball (e.g., soccer). Decision: yes, the architecture is made sport-parameterized in this change (sport classification step + a sport-keyed court geometry registry), but only basketball is actually implemented/shippable — other sports get the extension point, not working detection. See the two new "Decisions" entries below.

## Goals / Non-Goals

**Goals:**
- A runnable Avalonia app that captures a window/screen, can be calibrated against a real court (auto or manual), and shows both a raw overlay view and a minimap view, either or both enabled at once.
- A capture and inference architecture that later phases (player detection, tracking, jersey OCR, score OCR) can plug into without restructuring the solution.
- A reusable "per-source profile" persistence pattern (used here for calibration and sport classification) that Phase 4's score/state ROI (discussed with the user, not part of this change) is expected to reuse.
- An architecture that is sport-parameterized (classification step + geometry registry) so a future sport is added as a registry entry, not a redesign — while this change ships only basketball.

**Non-Goals:**
- Player/ball/jersey/score detection models and logic — future changes.
- Cross-platform capture (macOS/Linux screen capture) — `NBA.Capture` is Windows-only for this change; Avalonia is chosen for the UI specifically so the rest of the app isn't blocked on that later. **Superseded for macOS**: the user asked directly whether Mac could get a real backend too; a CoreGraphics/CoreFoundation-based `MacFrameSource`/`MacCaptureSourceEnumerator` was added afterwards (see tasks.md's 2.6 addendum and the new "Capture: a second CoreGraphics-backed backend for macOS" Decision below). Linux remains out of scope — no backend, `Fake*` only.
- Model training pipeline — training happens out-of-repo in Python/Ultralytics; this change only consumes exported `.onnx` files.
- Any actual court-keypoint model weights — this change implements the inference/calibration *pipeline*; a trained keypoint model is a separate content/training effort. Until one exists, the manual calibration fallback (already in the `vision/court-calibration` spec) is the primary path exercised end-to-end.
- Any sport other than basketball actually working — soccer/tennis/etc. get a registry extension point (geometry registry entry, sport-classification registry entry) but no geometry definition, no trained classifier examples, and no detection models in this change.

## Decisions

### Solution layout
```
NBA.sln
├── src/
│   ├── NBA.Capture/       # Windows.Graphics.Capture wrapper → IFrameSource stream
│   ├── NBA.Inference/     # ONNX Runtime wrapper: model load, EP selection, pre/postprocess pipeline
│   ├── NBA.Vision/        # OpenCvSharp4-based homography compute/apply; sport classification; sport-keyed court geometry registry
│   ├── NBA.Tracking/      # placeholder project, empty — Phase 2
│   ├── NBA.OCR/           # placeholder project, empty — Phase 4
│   ├── NBA.State/         # placeholder project, empty — Phase 2+
│   └── NBA.App/           # Avalonia shell: source picker, raw overlay view, minimap view, calibration UI
├── models/                 # exported .onnx files (court keypoint model lands here once trained)
```
Placeholder projects are created now (empty, referenced in the `.sln`) so later changes add files to an existing project rather than restructuring the solution — restructuring an in-use solution is more disruptive than starting a project empty.

**Alternative considered**: a single `NBA.App` monolith with folders instead of separate projects. Rejected because `NBA.Capture` (Windows-only) and `NBA.Vision`/`NBA.Inference` (should be UI-framework-agnostic) have genuinely different platform/dependency boundaries — separate projects enforce that boundary at compile time instead of by convention.

### Capture: Windows.Graphics.Capture via a pluggable `IFrameSource`
`NBA.Capture` exposes an `IFrameSource` abstraction (start/stop/current-frame/source-lost-event) implemented today by a Windows.Graphics.Capture-backed class. `NBA.App` depends only on `IFrameSource`, not on the WinRT types directly.

**Why**: keeps the WinRT/Windows-specific surface contained to one project, and matches the frame-capture spec's requirement that switching sources means tearing down one capture session and starting another — that lifecycle lives entirely inside the `IFrameSource` implementation.

**Alternative considered**: GDI `BitBlt`-based capture. Rejected — noticeably higher latency/CPU cost and doesn't support window-only capture as cleanly as Windows.Graphics.Capture.

### Capture: a second CoreGraphics-backed backend for macOS
Added after the rest of this change, once the user asked directly whether Mac could get real capture too (originally a stated Non-Goal). `MacFrameSource`/`MacCaptureSourceEnumerator` (`src/NBA.Capture/Mac/`) implement the same `IFrameSource`/`ICaptureSourceEnumerator` interfaces using CoreGraphics/CoreFoundation plain-C P/Invoke (`CGWindowListCreateImage`/`CGDisplayCreateImage` for frames, `CGWindowListCopyWindowInfo`/`CGGetActiveDisplayList` for enumeration) — no Objective-C/Swift bridge needed. `NBA.App`'s `CapturePlatform` now picks the backend at **runtime** via `OperatingSystem.IsMacOS()` (not at the TFM level like the Windows/non-Windows split, since the same `net10.0` TFM covers both macOS and any other non-Windows OS this solution might run on).

**Why CoreGraphics over ScreenCaptureKit**: ScreenCaptureKit (macOS 12.3+) is Apple's current recommendation and would give a native push-based frame callback like Windows.Graphics.Capture, but it has no C API — using it from .NET would require writing and maintaining a Swift/ObjC native shim. CoreGraphics's relevant functions are plain C, callable via direct `DllImport`, at the cost of being poll-based (one snapshot per call) rather than push-based, so `MacFrameSource` drives it from a ~30fps `PeriodicTimer` loop instead of an event subscription. Validated by a throwaway P/Invoke spike before writing the production version (see tasks.md's 2.6 addendum).

**A real bug this surfaced**: the initial `StartAsync`/`StopAsync` implementation cleared `LatestFrameBuffer` and updated `State` *before* actually waiting for the previous poll loop to observe cancellation, so a capture already in flight on the old loop could publish a stale frame just after the "clear." This was invisible against `FakeFrameSource`'s synchronous contract tests (nothing there is actually concurrent) and only surfaced when a harness exercised the real `MacFrameSource` end-to-end. Fixed by fully awaiting the old poll loop's shutdown before clearing state/starting the next one — worth remembering as a general lesson: **an `IFrameSource` implementation with any real concurrency needs its own end-to-end check, not just the shared contract tests**, since the fakes can't exercise a race that only exists once there's an actual background loop.

**Known gap vs. the Windows enumerator**: `kCGWindowName` (the window title) is frequently empty even for ordinary app windows, unlike Win32's `GetWindowText` — so the Mac source picker's display name leans on the owning app's name (`kCGWindowOwnerName`) with the title appended only when present, rather than title-first like the Windows side.

### Inference: ONNX Runtime with DirectML→CPU fallback, one model = one pipeline instance
`NBA.Inference` defines a generic `OnnxModelPipeline<TInput, TOutput>` that owns: session creation (try DirectML EP, catch and retry with CPU EP), a preprocessing delegate, and a postprocessing delegate specific to each model type. The court keypoint model gets one instance; later models get their own instances with their own pre/post logic, reusing the same session-management and EP-fallback code.

**Why**: EP selection and fallback is identical across every model this project will ever load — factoring it out now means Phase 2/3/4 models don't reimplement it.

### Sport classification: fine-tuned lightweight classifier, not zero-shot ImageNet, cached per source
`NBA.Vision` classifies a source's sport using a small classifier (e.g., MobileNet/EfficientNet-lite backbone) with a replaced classification head, fine-tuned on a small labeled set of whole-frame broadcast/game screenshots per sport. It is deliberately **not** a stock ImageNet-1k model's raw top-1 output (e.g., its `basketball`/`soccer ball`/`tennis ball` classes) used zero-shot.

**Why**: ImageNet's ball-related classes were trained on object-centric close-ups (the ball dominating the frame), not wide broadcast/game scenes where the ball is small and off-center and the dominant visual signal is court/field surface, players, crowd, and UI chrome — a substantial distribution shift that makes zero-shot classification unreliable for this task. Fine-tuning a small head on the project's own whole-scene screenshots (a handful of classes, a few hundred images each) reuses the backbone's learned features while actually fitting the task, and stays cheap to train — this was discussed directly with the user and is the approach they agreed matches the problem better than the zero-shot approach they'd first considered.

**Training data note (broadcast vs. game footage)**: The whole-scene screenshots used to fine-tune this classifier are deliberately sourced from **both real broadcast footage and video game captures** (e.g., NBA 2K-style titles) treated as equivalent for this task. This is intentional, not a shortcut: these games are art-directed specifically to mimic real broadcast camera framing, HUD/scoreboard chrome, and court/crowd composition, so at the whole-scene level relevant to *which sport is this* they present a very similar distribution to real broadcast — making game screenshots a valid, cheap-to-source supplement (not a substitute) to real footage for this classifier.

**Caveat — does not generalize to court-keypoint detection**: this broadcast/game equivalence holds for whole-scene sport classification specifically, because the signal it relies on (framing, composition, chrome) survives the game-engine render. It should **not** be assumed to hold for the court-keypoint model (`vision/court-calibration`): rendering, lighting, and line/texture fidelity differ enough between game engines and real broadcast video that a keypoint model trained only on game screenshots risks not transferring to real footage. Any future keypoint-model training-data decision should be justified independently rather than reusing this rationale.

Classification runs once per selected capture source (not per frame) and the result — sport + confidence — is cached in that source's `SourceProfile`, consistent with the "detect once per source, reuse" pattern already used for calibration. A confidence threshold gates auto-acceptance; below it, the sport is reported as "unknown" rather than guessed. The user can always override the result from the UI (see `app/dual-view-shell`), which also persists to the `SourceProfile` and takes precedence over the automatic result.

**Alternative considered**: running a stock ImageNet-pretrained model zero-shot on each frame, as first proposed. Rejected for the domain-shift reason above — the training/inference split convention (train out-of-repo, ship `.onnx`) already established for the court keypoint model applies here too, so adopting a properly fine-tuned classifier costs no extra architecture, only a small labeled dataset.

### Sport-keyed court geometry registry
`NBA.Vision` defines a `CourtGeometryDefinition` (named landmarks + real-world coordinates + rendered minimap diagram reference) and a registry mapping `SportType → CourtGeometryDefinition`. Calibration (automatic and manual) and homography computation are written against "the current source's selected geometry definition," never against a hardcoded basketball constant. Only a `Basketball` entry is populated in this change.

**Why**: this is the concrete mechanism that makes "add a new sport later" mean "add a registry entry + its own keypoint model," not "touch the calibration/homography code" — directly answering the user's question about multi-sport support without scope-creeping this change into building soccer/tennis detection.

**Alternative considered**: keep court geometry as a hardcoded basketball constant in this change and refactor to a registry when a second sport is actually proposed. Rejected per the user's explicit choice — refactoring the calibration/homography code after the fact (and after `SourceProfile` schemas, UI bindings, etc. already assume one sport) is more disruptive than designing the seam in now while the surface is small.

### Calibration: homography via OpenCvSharp4, source-and-sport-scoped, persisted as a "source profile"
`NBA.Vision` computes homography with `Cv2.FindHomography` from matched image-space/court-space point pairs — using the landmark set from the current source's selected sport's `CourtGeometryDefinition` — from either the keypoint model's output or the manual-click fallback. The result is wrapped in a `SourceProfile` (source identity key → sport classification + homography + metadata) persisted to local disk (e.g., a JSON file keyed by a stable source identifier such as window title + resolution).

**This `SourceProfile` concept is deliberately generic**, not calibration-specific: it is a per-source key/value bag of "things we figured out about this source and don't want to redo every time we see it again." Calibration homography and sport classification are the entries populated in this change. The user separately confirmed the intended future use (score/state OCR ROI, one per source, discovered once via manual framing or a lightweight HUD-region detector, then reused) — this design does not implement that, but the `SourceProfile` storage format leaves room for additional named entries so Phase 4 can add a ROI entry without changing the storage mechanism.

**Alternative considered**: recomputing/re-prompting calibration every time a source is selected, no persistence. Rejected — directly contradicts the "reuse saved calibration" requirement in the `vision/court-calibration` spec and would make the manual-fallback path annoying enough to discourage testing.

### UI: two independent view models driven by one shared pipeline result stream, stacked by default
`NBA.App` runs a single capture→inference→projection pipeline producing a per-frame result object (frame + detections + projected points). The raw overlay view and minimap view are separate Avalonia `UserControl`s, each subscribing to that same result stream and rendering independently; visibility is a per-view boolean, not a pipeline on/off switch — the pipeline always runs so toggling a view is instant and doesn't require a "reconnect." The main window docks the raw view above the minimap view by default (per the reference layout the user provided: source frame on top, derived court diagram below), using a simple vertical `Grid`/`DockPanel` split rather than a floating/tabbed arrangement.

**Why**: satisfies the "both views run concurrently, each independently toggleable" requirement without duplicating pipeline work per view. The stacked order matches how the two views relate causally (minimap is *derived from* the raw view), which is also the layout the user asked for directly.

### Raw view overlay: generic annotation model, not detection-type-specific
The overlay drawn on the raw view is built from a generic `OverlayAnnotation` list (`Shape` [point/box/polyline], optional `Label`, `Style`) rather than a hardcoded "keypoints + boxes" pair. Whatever a given phase's pipeline stage produces (court keypoints now; player boxes, jersey number labels, ball position in later phases) gets mapped to this shared shape before reaching the view. The raw view only ever renders `OverlayAnnotation`s — it has no knowledge of what produced them.

**Why**: the user's reference layout shows player boxes and jersey-number labels overlaid on the raw view, which this change does not yet produce (no player detection until Phase 2). Building the raw view against a generic annotation model now means Phase 2/3 add a mapping step (detection → `OverlayAnnotation`), not a view rewrite.

**Alternative considered**: hardcode the raw view to draw "keypoints" for this change and rework it when player detection lands. Rejected — the rework would touch the one piece of UI every later phase depends on; cheaper to generalize once, now, while the view is simple.

### Minimap info panel: reserved region, placeholder content
The minimap view's layout includes a fixed status/info panel region (per the reference layout's bottom-panel score/info area) bound to a simple `IReadOnlyDictionary<string, string>`-shaped `StatusInfo` snapshot. In this change nothing populates it, so it renders a placeholder (e.g., "Score: not detected"). The binding point exists so Phase 4 (score/state OCR) connects a real data source later without touching the minimap view's layout.

**Why**: the user wants the panel present in the layout now, but implementing it is explicitly out of scope until score/state detection exists (see proposal.md's Out of Scope note and the score-ROI discussion recorded for Phase 4). Reserving the region avoids a layout change when that data becomes available.

**Note for Phase 4 design** (not implemented in this change, recorded here so it isn't lost): the user pointed out that scoreboard updates can lag behind on-court action because broadcasts cut away/transition before a score change is reflected — the info panel's data source should treat score as an eventually-consistent value decoupled from live play state, not something expected to update in lockstep with detected player/ball motion.

## Risks / Trade-offs

- **No trained court keypoint model exists yet** → the automatic calibration path (`vision/court-calibration`'s keypoint-based requirement) can't be exercised end-to-end until a model is trained out-of-repo. Mitigation: the manual calibration fallback is a first-class requirement in this change specifically so the pipeline (capture → calibrate → project → minimap) is provable without waiting on model training.
- **Windows-only capture blocks any future cross-platform ambition** → Mitigation: isolated to `NBA.Capture` behind `IFrameSource`; if a macOS/Linux capture backend is ever needed, only that project needs a new implementation. **Partially realized**: a macOS backend (`MacFrameSource`/`MacCaptureSourceEnumerator`) now exists, verified on this machine including through the real Avalonia app; Linux remains unaddressed.
- **DirectML behavior varies across GPU vendors/drivers** → Mitigation: CPU fallback is a hard requirement, not an optimization, so the app remains usable (if slower) on any machine.
- **Source identity key for `SourceProfile` (window title, etc.) is not perfectly stable** (titles can change, e.g., a YouTube tab title changes with the video) → Mitigation: use a composite key (process/app name + resolution) rather than raw title where possible, and always let the user explicitly confirm/overwrite a reused profile rather than silently trusting a fuzzy match.
- **No labeled training data or trained weights exist yet for the sport classifier** (mirrors the court-keypoint model risk above) → Mitigation: same pattern — manual sport override from the UI is a first-class requirement, so the pipeline is usable before the classifier is trained/accurate.
- **A confidently-wrong sport classification would select the wrong geometry and produce a nonsensical homography** → Mitigation: confidence threshold gates auto-acceptance (below it, report "unknown" rather than guess), and the sport indicator is always visible with a one-click manual override.

## Open Questions

- Exact composite key strategy for `SourceProfile` identity (window title vs. process name vs. user-assigned nickname) — can be refined during implementation without affecting the spec-level behavior (persistence and reuse are already required; the key format is an implementation detail).
