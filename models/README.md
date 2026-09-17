# models/

Exported ONNX model files consumed by `NBA.Inference`. Models are **not trained in this repository** — training happens out-of-repo in Python/Ultralytics (or equivalent); only the exported `.onnx` artifact is committed here. See `design.md` in `openspec/changes/init-nba-vision-platform/` for the training/inference split rationale.

## Naming convention

`<sport>_<scene>_<capability>_<resolution>_<architecture>.onnx`, for example:

- `basketball_nba_court-keypoints_1280_yolov8n-pose.onnx` — court keypoint detection model consumed by `vision/court-calibration`, trained on real NBA broadcast frames (`nba` scene), 1280x1280 input, Ultralytics YOLOv8n-pose.
- `basketball_nba_player-detection_1280_yolov8n.onnx` — the player-detection model consumed by `vision/player-detection`, real NBA broadcast frames, 1280x1280 input, Ultralytics YOLOv8n.

`<scene>` describes the visual domain the training data came from, since this genuinely affects transferability (see `init-nba-vision-platform/design.md`'s broadcast-vs-game-rendering caveat for court-keypoint models specifically) — always written explicitly, even when there's currently only one source:

- `nba` — real broadcast footage.
- `2k` — video-game captures (e.g. NBA 2K-style titles).
- `common` — training data mixes both, and the model is expected to generalize across them.

`<sport>` (and therefore `<scene>`, which only makes sense relative to a sport) is omitted entirely for models that are not sport-specific by definition — the model's job is cross-sport, so there is no single scene to name either:

- `sport-classifier_224_clip-vitb32.onnx` — the sport classification model consumed by `vision/sport-classification` (its whole job is to work across sports, so no `<sport>`/`<scene>` segment; `224` is the CLIP vision encoder's input size, `clip-vitb32` its architecture).

**Current status**: the three files actually in this directory today (`court-keypoints.basketball.onnx`, `player-detection.onnx`, `sport-classifier.onnx`) still use the old `<capability>.<sport>.onnx` scheme — they haven't been renamed yet because renaming implies asserting facts (exact scene, exact resolution/architecture) that should be verified against the real file at rename time, not guessed retroactively. Rename each file (and update its entry in `models.json` below) the next time it's actually replaced by a new export, rather than as a one-off bulk rename.

## `models.json`

`App.axaml.cs` no longer hardcodes model filenames or the per-model tunables that are tied to a specific export (input resolution, class list, confidence/IoU thresholds) - it loads them from `models/models.json` via `NBA.App.Services.ModelsConfig`. A missing file, a missing section, or a missing individual field all silently fall back to that field's default (today's previously-hardcoded value, declared on the corresponding record in `ModelsConfig.cs`) - so this file is optional, and can override as little or as much as needed. This means swapping in a retrained/re-exported model - a new resolution, a different class ordering, retuned thresholds, or (once actually renamed per the convention above) a new filename - only means editing this JSON file, not `App.axaml.cs`.

A subset of `courtKeypoints`'s two thresholds (`keypointConfidenceThreshold`, `detectionConfidenceThreshold`) are also live-adjustable from the toolbar in the running app (see `KeypointDetectionSettingsViewModel`) - `models.json` only sets their *starting* value for a session, not a hard ceiling.

A model file is not required for the application to run: every model-backed capability has a documented degraded/manual path when its model file is absent (e.g., manual court calibration, "unknown" sport classification) — see the relevant `specs/*/spec.md` for the exact fallback behavior.

## `sport-classifier.onnx`

Currently a CLIP ViT-B/32 **vision-encoder-only** export (via Hugging Face `optimum`), consumed by `ClipZeroShotSportClassifier` (`src/NBA.Vision/ClipZeroShotSportClassifier.cs`) per design.md's "Sport classification: CLIP/SigLIP zero-shot" decision — not a fine-tuned classifier. Verified signature (`python -c "import onnx; onnx.load(...)"`, not yet run through `ClipZeroShotSportClassifier` against a real captured frame):

- Input: `pixel_values`, shape `[batch, channels, height, width]` — matches `ClipZeroShotSportClassifier`'s default `inputName`/`inputSize` (224) exactly, no wiring changes needed.
- Output: `image_embeds`, shape `[batch, 512]` — matches the classifier's "take the first output" convention.

The corresponding **text** encoder (`text_model.onnx`, ~250MB) is deliberately **not** in this directory — it is never loaded by `NBA.Inference`/shipped with the app (see design.md: "no text encoder or tokenizer ships or runs in this app"). It lives at `tools/clip-text-encoder/` as an offline-only tool for generating the prompt-embeddings JSON `ClipPromptEmbeddings` reads — see that directory's README.

**Verified**: this file loads and runs successfully through `ClipZeroShotSportClassifier` end-to-end on this machine (throwaway harness, not part of the checked-in test suite) — `pixel_values` in, a 512-dim `image_embeds` out, ~74ms per classification on CPU — confirming the input/output-name assumptions above are correct against the *real* file, not just its inspected metadata.

**Not yet done**: the harness above used random placeholder prompt vectors (proves the plumbing works, proves nothing about classification *accuracy*) — no real prompt-embeddings JSON has been generated from the paired text encoder yet (needs a CLIP tokenizer, which `text_model.onnx` alone doesn't include — see `tools/clip-text-encoder/README.md`), so `ClipZeroShotSportClassifier` is not yet wired into `NBA.App`'s composition root, and real classification accuracy against actual captured frames is unverified.

## `court-keypoints.basketball.onnx`

A real trained model now exists (previously this capability had no model at all — see design.md's risk entry — and `App.axaml.cs` fell back entirely to `NullCourtKeypointDetector`). Trained locally with Ultralytics, following design.md's out-of-repo Python/Ultralytics training convention.

**Active model is currently the 33-keypoint "lweda retrain" below**, promoted 2026-09-16, superseding the "previous version" (`samet-mmrat` dataset) that was active before it — that older file is kept as `court-keypoints.basketball.onnx.bak-33kpt` for context on the `BasketballGeometry.cs` mapping rationale. A separate 34-keypoint retrain was also attempted 2026-09-16 (see "Rejected 34-keypoint attempt" below) but failed end-to-end verification and was never promoted.

**Active — 33-keypoint "lweda retrain"** (2026-09-16, `yolov8n-pose.pt` base, promoted to `court-keypoints.basketball.onnx`):

- **Data**: Roboflow Universe's [`basketball-court-detection-2-lweda`](https://universe.roboflow.com/dh-yang/basketball-court-detection-2-lweda/dataset/1) (workspace `dh-yang`), CC BY 4.0, single class `court`, 33 keypoints/image (`kpt_shape: [33, 3]`) — same 33-point schema as the previous active model below, from a different Roboflow project under the same annotation convention.
- **Export**: Ultralytics `yolo export format=onnx opset=13` equivalent (embedded ONNX metadata: `Ultralytics YOLOv8n-pose model trained on data.yaml`, Ultralytics version `8.4.153`, exported `2026-09-16T23:43:08+08:00`, `task=pose`, `end2end=False`).
- **Verified signature** (inspected the real exported `onnx.load(...).graph`, not assumed): input `images` `[1, 3, 640, 640]`; output `output0` `[1, 104, 8400]` — byte-for-byte the same shape as the previous active model (4 box + 1 class-confidence + 33×3 keypoint channels), so `OnnxCourtKeypointDetector` needs no changes.
- **Keypoint mapping carries over unchanged**: this dataset's `data.yaml` `flip_idx` has the identical mirror structure already wired in `BasketballGeometry.cs` — index 16 self-maps (`CenterCourt`), 15 and 17 self-map (mid-court/sideline intersections), 6↔26 mirror (free-throw circle centers), and 0↔27 / 1↔28 mirror (paint baseline corners) — so the existing reverse-engineered landmark mapping (see "Previous version" below for the full caveat) applies as-is; it was not independently re-verified against this dataset's actual images.
- **Not yet done**: no real captured frame has been run through this file end-to-end via the real C# pipeline (`OnnxCourtKeypointDetector`) — only the ONNX graph's input/output/metadata were inspected directly. Per-keypoint confidence behavior against real broadcast frames (i.e. whether `keypointConfidenceThreshold=0.5` is appropriate for this retrain) is unverified, same caveat as every prior version here.

**Rejected 34-keypoint attempt** (2026-09-16, kept as `court-keypoints.basketball.onnx.new-34kpt-undertrained`, gitignored, not the active file):

- **Data**: Roboflow Universe's [`basketball-court-hlifr-duehm`](https://universe.roboflow.com/dh-yang/basketball-court-hlifr-duehm/dataset/1) (workspace `dh-yang`), CC BY 4.0 — 70 frames (49 train / 14 valid / 7 test), single class `court`, 34 keypoints/image (`kpt_shape: [34, 3]`). Downloaded via Roboflow's YOLOv8 export. **Caveat**: roughly half the images in this export have out-of-bounds/non-normalized keypoint coordinates (Ultralytics logs "ignoring corrupt image/label" for them) and were skipped by the loader — actual training used only 25 train / 10 valid images.
- **Architecture/training**: `yolov8n-pose.pt` base, fine-tuned 50 epochs, `imgsz=640`, default Ultralytics hyperparameters, CPU (Apple M4). Validation (on the 10-image valid subset — small enough that this number is not very meaningful): box `mAP50=0.995`, pose `mAP50=0.995`, pose `mAP50-95=0.938`.
- **Export**: `yolo export format=onnx opset=13` — standard Ultralytics YOLOv8-pose export, no `nms=True`/`end2end`.
- **Verified signature**: input `images` `[1, 3, 640, 640]`; output `output0` `[1, 107, 8400]` (4 box + 1 confidence + 34×3 keypoint channels) — matches `OnnxCourtKeypointDetector`'s dynamic channel-count handling, no code changes needed there.
- **Verified end-to-end through the real C# pipeline** (`OnnxCourtKeypointDetector`, not just Python): loaded and ran without exceptions against 56 real dataset images (all 7 test + all 49 train images), with a consistently high top-level detection confidence (0.986–0.997 on every image) — the "is a court present" head trained fine.
- **Why it was rejected**: the per-keypoint visibility confidence never exceeds ~0.39–0.45 on *any* keypoint on *any* of the 56 images tested — a flat ceiling well below `OnnxCourtKeypointDetector`'s default `keypointConfidenceThreshold=0.5`. At the shipped default threshold this model returns **zero** detected keypoints on every real frame tested, i.e. as configured it is behaviorally identical to `NullCourtKeypointDetector` — a silent regression, not an improvement. (As a decode sanity check, lowering the threshold to 0.3 does yield 2 keypoints per frame with smoothly camera-pan-consistent pixel positions, confirming the index→pixel decode math itself is correct — this is specifically a confidence-calibration failure, almost certainly from the tiny 25-image effective training set, not a wiring bug.)
- **If revisited**: needs either a much larger/cleaner keypoint-labeled dataset (this export's ~50% corrupt-label rate should be fixed at the source, e.g. re-exporting from Roboflow or filtering/fixing the offending label files before training) or, as a cheaper experiment, lowering `keypointConfidenceThreshold` to ~0.35–0.4 specifically for this model and re-validating — not done here since that threshold would need empirical tuning against real broadcast frames, not just this dataset's own training images. The keypoint-index → landmark mapping in `BasketballGeometry.cs` (reverse-engineered against the *old* 33-keypoint dataset, see below) was also never checked against this dataset's ordering, since the confidence-ceiling issue blocks getting any keypoints out to check in the first place.

**Previous version** (33 keypoints, superseded 2026-09-16 by the "lweda retrain" above, kept as `court-keypoints.basketball.onnx.bak-33kpt` for context on the `BasketballGeometry.cs` mapping rationale):

- **Data**: Roboflow Universe's [`basketball-court-detection-2`](https://universe.roboflow.com/samet-mmrat/basketball-court-detection-2-axedc) (workspace `samet-mmrat`), CC BY 4.0 — 850 real NBA broadcast frames, single class `court`, 33 keypoints/image (`kpt_shape: [33, 3]`). Downloaded via the free-tier Roboflow dataset-export API (a free API key is sufficient for this — downloading *someone else's already-trained weights* is a paid-plan-only feature and was not used; this repo trains its own weights from the raw annotated dataset instead).
- **Architecture/training**: `yolov8n-pose.pt` base, fine-tuned 50 epochs, `imgsz=640`, default Ultralytics hyperparameters (Apple M2, PyTorch MPS). Validation: box `mAP50=0.995`, pose `mAP50=0.952`, pose `mAP50-95=0.761`.
- **Export**: `model.export(format="onnx", opset=13)` — standard Ultralytics YOLOv8-pose export, no `nms=True`/`end2end`.

**Verified signature** (inspected the real exported `onnx.load(...).graph`, not assumed):

- Input: `images`, shape `[1, 3, 640, 640]` (NCHW, RGB, `[0, 1]`) — matches `ImagePreprocessing.ToNchwTensor` / `OnnxCourtKeypointDetector`'s default `inputSize=640` exactly.
- Output: `output0`, shape `[1, 104, 8400]` — per anchor (8400 of them): 4 box values (`cx,cy,w,h`, unused by this detector), 1 already-sigmoid class confidence, then 33 keypoint triples (`x,y` already decoded to `[0,640]` input-pixel space, already-sigmoid visibility) — confirmed by tracing the graph's final `Sigmoid`/`Concat` nodes. No NMS baked in, but since exactly one "court" object is ever expected per frame, `OnnxCourtKeypointDetector` just picks the single highest-confidence anchor instead of running NMS.

**Keypoint-index → real-world-landmark mapping (the risky part)**: the dataset's exported keypoint order has no authoritative name list (Roboflow's COCO export just labels them `"01".."41"` with gaps — a subset of some larger, undocumented canonical numbering; no schema was found despite Roboflow's own blog using this exact dataset). The mapping in `BasketballGeometry.cs` was reverse-engineered from `data.yaml`'s `flip_idx` (mirror-pair structure: indices 0-14 mirror to a symmetric set, 15/16/17 sit on the center line) plus visual inspection of several real annotated broadcast frames. Confidently identified and wired: `CenterCourt` (index 16), the free-throw circle centers (indices 6/26, reusing the existing `FreeThrowLineCenter_*` landmarks — same real-world point), the mid-court/sideline intersections (15/17), and the paint's baseline corners (0/1/27/28). **Not verified**: for each mirrored pair, which physical corner is the lower vs. higher index — if swapped, the whole computed homography mirrors left/right (a consistent, detectable error, not a scrambled one). The true court corners and 3-point arc apex are not in this model's 33-point set at all and remain manual-calibration-only. See `OnnxCourtKeypointDetectorTests` for the postprocessing wiring verification (against a synthetic fixture, not this real file) and `BasketballGeometry.cs`'s doc comment for the full caveat.

**Not yet done**: no real captured frame has been run through the *new* (34-keypoint) model end-to-end in the app; the `BasketballGeometry.cs` landmark mapping is unverified against this dataset (see above); the confidence thresholds (`keypointConfidenceThreshold`/`detectionConfidenceThreshold`, both default `0.5`) are placeholders, not empirically calibrated.

This file is gitignored (like `sport-classifier.onnx`) — regenerate it by re-running the training steps above, or obtain a copy separately.

## `player-detection.onnx`

A real basketball-specific multi-class model now exists (previously this directory only had `yolov8n.onnx`'s generic COCO-pretrained shape to verify against — see "Previous verification" below). Trained/exported outside this repo from a Roboflow `basketball-players-fy4c2`-style labeled dataset (`Ball`, `Hoop`, `Period`, `Player`, `Ref`, `Shot Clock`, `Team Name`, `Team Points`, `Time Remaining`), then exported via `yolo export format=onnx opset=12`.

**Verified signature** (inspected the real file's embedded ONNX metadata directly — `onnx.metadata_props`/the raw protobuf bytes — not assumed from any UI display order or guessed):

- Input: `images`, shape `[1, 3, 1280, 1280]` (NCHW, RGB, values scaled to `[0, 1]`) — note the larger `1280` export size, unlike the generic `yolov8n.onnx` verification below (`640`). `OnnxMultiClassObjectDetector`'s `inputSize` must be passed as `1280` at the `App.axaml.cs` call site for this file; the library's own constructor default (`640`) is left unchanged since it's also relied on by unit tests using a small synthetic fixture.
- Output: `output0`, shape `[1, 13, N]` — 4 box channels (`cx, cy, w, h`, in `[0, 1280]` input-pixel space) followed by 9 per-class confidence scores, no NMS baked in (same raw Ultralytics export shape `OnnxMultiClassObjectDetector` already expects).
- Embedded class-name metadata (`names` key, verified by inspection): `{0: 'Ball', 1: 'Hoop', 2: 'Period', 3: 'Player', 4: 'Ref', 5: 'Shot Clock', 6: 'Team Name', 7: 'Team Points', 8: 'Time Remaining'}`. **`Player` is class index `3`** — this is the index `App.axaml.cs` passes as `OnnxMultiClassObjectDetector`'s player-class-channel constructor parameter, so only actual players are fed into `PlayerDetection`/tracking. **As of `add-multiclass-object-detection`, every one of the other 8 classes (including `Ref`) is decoded from the same inference pass too** — surfaced as `OnCourtObjectDetection`s (see `vision/on-court-object-detection`) rather than discarded. The earlier framing here (`add-team-color-track-gating`'s design.md: "`Ref` is deliberately never read") only ever applied to what *consumes* the `Ref` channel for tracking/team-color purposes, which is still true — `Ref` detections still never reach `PlayerDetection` or the tracker, they're just no longer thrown away unread.
- No separate `data.yaml`/`classes.txt` file was produced alongside this export — the class list above came entirely from the `.onnx` file's own embedded metadata, which is sufficient and was cross-checked against the model's live detection output before adoption.

**Previous verification** (kept for context — describes `yolov8n.onnx`, the stock COCO-pretrained detector this repo verified the raw-export-shape assumption against before a basketball-specific model was available): input `images` `[1, 3, 640, 640]`; output `output0` `[1, 84, 8400]` (4 box channels + 80 COCO per-class scores, COCO class 0 = "person"), no NMS baked in. `OnnxMultiClassObjectDetector` always applies its own non-max suppression in postprocessing, independently per class, since this raw export shape has none — true for both this and the model above.

This file is gitignored (like `sport-classifier.onnx`) — regenerate/re-obtain it separately; it is not checked into the repo.

## `jersey-number.onnx`

**Not yet present** — no trained model has been added to this directory yet. `App.axaml.cs` falls back to `NullJerseyNumberRecognizer` (reports every track "unrecognized" every frame) until this file exists.

Expected signature, consumed by `OnnxJerseyNumberRecognizer` (`src/NBA.JerseyOcr/OnnxJerseyNumberRecognizer.cs`) — per design.md's "closed-set classification, not general sequence-decoding OCR" decision, this is a fixed-class classifier, not a YOLO-style detector:

- Input: `input`, shape `[1, 3, inputSize, inputSize]` (NCHW, RGB, values scaled to `[0, 1]`) — `inputSize` defaults to 64 (`OnnxJerseyNumberRecognizer`'s constructor parameter), fed a single tracked player's cropped bounding box (not the full frame) resized via `ImagePreprocessing.ToNchwTensor`'s crop-rectangle overload.
- Output: `output0`, shape `[1, 101]` — one logit per class: classes `0`-`99` are the jersey number itself (index = the number), class `100` is "no number" (occluded, player facing away, or no jersey visible). No softmax baked in; `OnnxJerseyNumberRecognizer` takes the argmax class and computes its softmax-equivalent confidence itself, reporting "unrecognized" (`Number: null`) whenever the argmax is class 100 or its confidence falls below the configurable threshold (default 0.5).

No model file is shipped in this change (same placeholder posture as `court-keypoints.basketball.onnx` and `player-detection.onnx` above) — training a real jersey-number classifier is out of scope for this change.
