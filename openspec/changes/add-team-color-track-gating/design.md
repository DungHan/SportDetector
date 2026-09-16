## Context

See proposal.md - Why/What Changes for motivation. Relevant current state:

- `OnnxPlayerDetector` (`src/NBA.Vision/OnnxPlayerDetector.cs`) reads one class channel (`personClassId`, constructor parameter, currently wired to `0` in `App.axaml.cs`) from a raw Ultralytics-shape YOLO export. It doesn't care which class that is - it just reads whichever channel index it's given. The gap being fixed here is the *wiring*, not the detector's mechanism.
- `models/player-detection.onnx` (untracked, newly added) is confirmed by the user to be a multi-class export (Roboflow `basketball-players-fy4c2`-style: `Ball`, `Period`, `Player`, `Ref`, `Shot Clock`, `Team Points`, `Time Remaining`) - `Player` and `Ref` are separate classes.
- `GreedyIouMatcher.Match` (`src/NBA.Tracking/GreedyIouMatcher.cs`) builds its candidate list purely from IoU above a threshold, then greedily claims pairs highest-IoU-first. There is no hook today for an additional per-pair eligibility check.
- `ByteTrackPlayerTracker`'s private `Track` class (`src/NBA.Tracking/ByteTrackPlayerTracker.cs`) already holds per-track mutable state (`Predictor`, `LastBox`, `LastConfidence`, `LostFrames`) alongside the motion model - this change adds one more piece of per-track state (a running color estimate) to the same place, following the same ownership pattern.
- `throttle-player-detection-cadence` already established that `Update(...)` (not `PredictOnly()`) is the only place real detections - and therefore real appearance information - are ever available; color gating naturally only ever runs inside `Update`.

## Goals / Non-Goals

**Goals:**
- Stop referees/officials from ever being detected (and therefore tracked) as players, by correcting the class-index wiring against the new multi-class model.
- Reduce ID swaps between two nearby, differently-colored players by adding an appearance check that's cheap (no new ML model, no new heavyweight dependency) and fails safe (skips itself rather than guessing, when the signal isn't trustworthy this frame).
- Keep `GreedyIouMatcher` generically reusable - the color-awareness lives in the caller (`ByteTrackPlayerTracker`), not hardcoded into the matcher.

**Non-Goals:**
- Speed/displacement clamping on association - explicitly paused (see proposal.md), a separate future change.
- Perceptual color spaces (HSV/Lab) or histogram-based dominant-color extraction - v1 uses a plain mean over BGR, revisit only if plain-mean proves too lighting-sensitive in practice.
- Any use of team color beyond gating association (e.g. team-side classification, jersey-color-based stats, or rendering it in the UI).
- Determining the exact class index for `Player` as part of this design - that's a task-time verification against the model's bundled label metadata (see tasks.md), not a design-level decision, since it depends on inspecting a specific file rather than on any architectural choice.

## Decisions

**Color sampled as a plain mean over the same upper-body crop concept used by the sibling `add-jersey-number-ocr` change (top ~40% of box height, full width), computed independently here.** No dependency is introduced between the two changes - each computes its own crop rectangle from a `TrackedPlayer`/`PlayerDetection` box using the same simple fraction, since the concept ("torso region is where team color and jersey number both live") is shared but the two features have no other coupling and may be implemented in either order or independently.

**Color computed by plain byte-averaging over the BGRA span already in `OnnxPlayerDetector.Detect(...)`, not via OpenCvSharp.** `NBA.Vision` already depends on `OpenCvSharp4` (used for homography), but a plain mean color needs no image-processing library at all - summing and dividing the crop region's B/G/R byte channels is sufficient and keeps this specific computation dependency-free. `NBA.Tracking` continues to depend on neither OpenCvSharp nor any imaging library - it only ever receives a plain color value (e.g. three `byte`/`float` channel values), matching this change's constraint that appearance extraction stays in `NBA.Vision`, where the raw pixels already live.

**Per-frame team-color grouping via a simple 2-means (not a persistent/global color model).** Lighting, camera angle, and exposure can drift over a broadcast; recomputing two groups fresh each detection frame - rather than maintaining one fixed pair of "team A/B" reference colors for the whole clip - keeps the signal self-calibrating to whatever the current frame actually looks like, at the cost of needing the index-flip-safe comparison below.

**Comparison is "nearest-centroid identity," never a raw cluster index, across frames.** A 2-means run's "group 0"/"group 1" labels are arbitrary and can swap between consecutive frames even if the two real color groups haven't moved. To check whether a candidate detection and a track "agree," both are independently compared against *this frame's* two centroids (by color distance) and the question asked is "are they nearest to the same one of these two centroids?" - never "does the detection's group index equal the track's last-seen group index." This makes the comparison correct regardless of how 2-means happens to number its output this particular frame.

**Track color state is a single running EMA, not a per-track copy of "which team."** A track doesn't need to know *which* team it's on, only "what does this specific player tend to look like" - an exponential moving average updated on every successful match (seeded at spawn) captures that without needing any notion of team identity or team-switching logic. The frame-level 2-means step exists only to produce this frame's two comparison points, not to permanently label any track.

**Degenerate-frame safeguards are hard skips of the veto, not soft/weighted signals.** Fewer than two detections, or two centroids too close together (below a configurable minimum-separation constructor parameter), both fully disable the veto for that frame - association falls back to IoU alone, exactly as it behaves today. A soft/weighted blend (e.g., "trust color less when centroids are close") was considered and rejected as unnecessary complexity: the existing IoU-only behavior is already the fallback this codebase relies on everywhere else a signal is unavailable (e.g., missing model → `Null*` returning empty/no-op, not a degraded guess).

**`GreedyIouMatcher.Match` gains an optional `Func<int,int,bool>? isEligible` parameter (predicted-index, detection-index) -> bool, checked when building the candidate list, defaulting to `null` (always eligible).** This keeps the matcher itself color-agnostic and reusable, existing/future callers and tests that don't pass appearance data are entirely unaffected, and `ByteTrackPlayerTracker` supplies a closure capturing that frame's centroids and each track's/detection's colors.

**EMA smoothing factor and minimum-centroid-separation are constructor parameters on `ByteTrackPlayerTracker`**, with starting defaults chosen the same way every other unvalidated threshold in this codebase is (`OnnxPlayerDetector`'s confidence/IoU thresholds, `maxLostFrames`, `throttle-player-detection-cadence`'s cadence `N`) - exposed now, tuned later against real footage.

## Risks / Trade-offs

- [Plain RGB/BGR mean is lighting-sensitive] → two teams with similar brightness-adjusted hues (e.g. both dark jerseys under stadium lighting) may not separate well; the minimum-centroid-separation safeguard causes the veto to self-disable in that case rather than actively hurting association - acceptable degradation to "no worse than today," not a new failure mode.
- [2-means on a small, noisy per-frame point set can occasionally produce an unstable split] → mitigated by the minimum-separation safeguard and by only ever comparing nearest-centroid identity (never raw index) within the same frame's computation, so instability in *labeling* doesn't matter, only instability in *whether a real 2-way split exists* does.
- [Wrong class-index wiring for `Player` would silently detect nothing or the wrong class] → the "Configured class index does not match the loaded model" requirement (spec delta) makes this fail to zero detections rather than silently reading an unrelated channel; the exact index is a task-time verification against the model's bundled label file, not guessed.
- [A player whose jersey color closely resembles the ball/court/crowd, or extreme lighting outliers] → same class of risk as any appearance-based signal; the veto is additive and fails safe to IoU-only, so this cannot make association worse than the pre-existing IoU-only behavior, only fail to help in that instance.

## Open Questions

- The exact `Player` class index in `models/player-detection.onnx`'s bundled label metadata - a task-time verification step (see tasks.md), not something that changes this design, the specs, or the task breakdown.
