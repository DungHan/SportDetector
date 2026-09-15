# models/

Exported ONNX model files consumed by `NBA.Inference`. Models are **not trained in this repository** — training happens out-of-repo in Python/Ultralytics (or equivalent); only the exported `.onnx` artifact is committed here. See `design.md` in `openspec/changes/init-nba-vision-platform/` for the training/inference split rationale.

## Naming convention

`<capability>.<sport>.onnx`, for example:

- `court-keypoints.basketball.onnx` — court keypoint detection model consumed by `vision/court-calibration`.
- `sport-classifier.onnx` — the sport classification model consumed by `vision/sport-classification` (not sport-specific by definition, so no `<sport>` segment).
- `player-detection.onnx` — the player-detection model consumed by `vision/player-detection` (not sport-specific by definition — a person detector works the same regardless of sport — so no `<sport>` segment).

A model file is not required for the application to run: every model-backed capability has a documented degraded/manual path when its model file is absent (e.g., manual court calibration, "unknown" sport classification) — see the relevant `specs/*/spec.md` for the exact fallback behavior.

## `sport-classifier.onnx`

Currently a CLIP ViT-B/32 **vision-encoder-only** export (via Hugging Face `optimum`), consumed by `ClipZeroShotSportClassifier` (`src/NBA.Vision/ClipZeroShotSportClassifier.cs`) per design.md's "Sport classification: CLIP/SigLIP zero-shot" decision — not a fine-tuned classifier. Verified signature (`python -c "import onnx; onnx.load(...)"`, not yet run through `ClipZeroShotSportClassifier` against a real captured frame):

- Input: `pixel_values`, shape `[batch, channels, height, width]` — matches `ClipZeroShotSportClassifier`'s default `inputName`/`inputSize` (224) exactly, no wiring changes needed.
- Output: `image_embeds`, shape `[batch, 512]` — matches the classifier's "take the first output" convention.

The corresponding **text** encoder (`text_model.onnx`, ~250MB) is deliberately **not** in this directory — it is never loaded by `NBA.Inference`/shipped with the app (see design.md: "no text encoder or tokenizer ships or runs in this app"). It lives at `tools/clip-text-encoder/` as an offline-only tool for generating the prompt-embeddings JSON `ClipPromptEmbeddings` reads — see that directory's README.

**Verified**: this file loads and runs successfully through `ClipZeroShotSportClassifier` end-to-end on this machine (throwaway harness, not part of the checked-in test suite) — `pixel_values` in, a 512-dim `image_embeds` out, ~74ms per classification on CPU — confirming the input/output-name assumptions above are correct against the *real* file, not just its inspected metadata.

**Not yet done**: the harness above used random placeholder prompt vectors (proves the plumbing works, proves nothing about classification *accuracy*) — no real prompt-embeddings JSON has been generated from the paired text encoder yet (needs a CLIP tokenizer, which `text_model.onnx` alone doesn't include — see `tools/clip-text-encoder/README.md`), so `ClipZeroShotSportClassifier` is not yet wired into `NBA.App`'s composition root, and real classification accuracy against actual captured frames is unverified.

## `court-keypoints.basketball.onnx`

A real trained model now exists (previously this capability had no model at all — see design.md's risk entry — and `App.axaml.cs` fell back entirely to `NullCourtKeypointDetector`). Trained locally (Apple M2, PyTorch MPS) with Ultralytics, following design.md's out-of-repo Python/Ultralytics training convention:

- **Data**: Roboflow Universe's [`basketball-court-detection-2`](https://universe.roboflow.com/samet-mmrat/basketball-court-detection-2-axedc) (workspace `samet-mmrat`), CC BY 4.0 — 850 real NBA broadcast frames, single class `court`, 33 keypoints/image (`kpt_shape: [33, 3]`). Downloaded via the free-tier Roboflow dataset-export API (a free API key is sufficient for this — downloading *someone else's already-trained weights* is a paid-plan-only feature and was not used; this repo trains its own weights from the raw annotated dataset instead).
- **Architecture/training**: `yolov8n-pose.pt` base, fine-tuned 50 epochs, `imgsz=640`, default Ultralytics hyperparameters. Validation: box `mAP50=0.995`, pose `mAP50=0.952`, pose `mAP50-95=0.761`.
- **Export**: `model.export(format="onnx", opset=13)` — standard Ultralytics YOLOv8-pose export, no `nms=True`/`end2end`.

**Verified signature** (inspected the real exported `onnx.load(...).graph`, not assumed):

- Input: `images`, shape `[1, 3, 640, 640]` (NCHW, RGB, `[0, 1]`) — matches `ImagePreprocessing.ToNchwTensor` / `OnnxCourtKeypointDetector`'s default `inputSize=640` exactly.
- Output: `output0`, shape `[1, 104, 8400]` — per anchor (8400 of them): 4 box values (`cx,cy,w,h`, unused by this detector), 1 already-sigmoid class confidence, then 33 keypoint triples (`x,y` already decoded to `[0,640]` input-pixel space, already-sigmoid visibility) — confirmed by tracing the graph's final `Sigmoid`/`Concat` nodes. No NMS baked in, but since exactly one "court" object is ever expected per frame, `OnnxCourtKeypointDetector` just picks the single highest-confidence anchor instead of running NMS.

**Keypoint-index → real-world-landmark mapping (the risky part)**: the dataset's exported keypoint order has no authoritative name list (Roboflow's COCO export just labels them `"01".."41"` with gaps — a subset of some larger, undocumented canonical numbering; no schema was found despite Roboflow's own blog using this exact dataset). The mapping in `BasketballGeometry.cs` was reverse-engineered from `data.yaml`'s `flip_idx` (mirror-pair structure: indices 0-14 mirror to a symmetric set, 15/16/17 sit on the center line) plus visual inspection of several real annotated broadcast frames. Confidently identified and wired: `CenterCourt` (index 16), the free-throw circle centers (indices 6/26, reusing the existing `FreeThrowLineCenter_*` landmarks — same real-world point), the mid-court/sideline intersections (15/17), and the paint's baseline corners (0/1/27/28). **Not verified**: for each mirrored pair, which physical corner is the lower vs. higher index — if swapped, the whole computed homography mirrors left/right (a consistent, detectable error, not a scrambled one). The true court corners and 3-point arc apex are not in this model's 33-point set at all and remain manual-calibration-only. See `OnnxCourtKeypointDetectorTests` for the postprocessing wiring verification (against a synthetic fixture, not this real file) and `BasketballGeometry.cs`'s doc comment for the full caveat.

**Not yet done**: no real captured frame has been run through this model end-to-end in the app (`App.axaml.cs` will pick it up automatically now that the file exists at this path, but that hasn't been exercised); the keypoint-index sidedness above is unverified; the confidence thresholds (`keypointConfidenceThreshold`/`detectionConfidenceThreshold`, both default `0.5`) are placeholders, not empirically calibrated.

This file is gitignored (like `sport-classifier.onnx`) — regenerate it by re-running the training steps above, or obtain a copy separately.

## `player-detection.onnx`

**Not yet present** — no trained model has been added to this directory yet. `App.axaml.cs` falls back to `NullPlayerDetector` (zero detections every frame) until this file exists.

Expected signature, consumed by `OnnxPlayerDetector` (`src/NBA.Vision/OnnxPlayerDetector.cs`) - **verified against a real exported model** (`yolov8n.onnx`, exported via `yolo export model=yolov8n.pt format=onnx`, the standard Ultralytics COCO-pretrained detector, not basketball-specific):

- Input: `images`, shape `[1, 3, 640, 640]` (NCHW, RGB, values scaled to `[0, 1]`) — matches `ImagePreprocessing.ToNchwTensor`'s output exactly (`OnnxPlayerDetector`'s `inputName`/`inputSize` defaults match this exactly).
- Output: `output0`, shape `[1, 84, 8400]` — 4 box channels (`cx, cy, w, h`, in `[0, 640]` input-pixel space, not normalized) followed by 80 per-class confidence scores (COCO class 0 = "person"), 8400 candidate anchors, no NMS baked in.

This is the **raw Ultralytics export shape** (no `nms=True`/`end2end` variant), confirmed by loading the real file and inspecting `onnx.load(...).graph` — not a guess. `OnnxPlayerDetector` reads only the `personClassId` channel (default 0) and always applies its own non-max suppression in postprocessing, since this raw shape has none. A basketball-specific model (e.g. a YOLO-variant export from [Roboflow's `basketball-players-fy4c2`](https://universe.roboflow.com/roboflow-universe-projects/basketball-players-fy4c2)) can be dropped in later without a code change, as long as it's exported the same way (plain `yolo export ... format=onnx`, no NMS flag).

## `jersey-number.onnx`

**Not yet present** — no trained model has been added to this directory yet. `App.axaml.cs` falls back to `NullJerseyNumberRecognizer` (reports every track "unrecognized" every frame) until this file exists.

Expected signature, consumed by `OnnxJerseyNumberRecognizer` (`src/NBA.JerseyOcr/OnnxJerseyNumberRecognizer.cs`) — per design.md's "closed-set classification, not general sequence-decoding OCR" decision, this is a fixed-class classifier, not a YOLO-style detector:

- Input: `input`, shape `[1, 3, inputSize, inputSize]` (NCHW, RGB, values scaled to `[0, 1]`) — `inputSize` defaults to 64 (`OnnxJerseyNumberRecognizer`'s constructor parameter), fed a single tracked player's cropped bounding box (not the full frame) resized via `ImagePreprocessing.ToNchwTensor`'s crop-rectangle overload.
- Output: `output0`, shape `[1, 101]` — one logit per class: classes `0`-`99` are the jersey number itself (index = the number), class `100` is "no number" (occluded, player facing away, or no jersey visible). No softmax baked in; `OnnxJerseyNumberRecognizer` takes the argmax class and computes its softmax-equivalent confidence itself, reporting "unrecognized" (`Number: null`) whenever the argmax is class 100 or its confidence falls below the configurable threshold (default 0.5).

No model file is shipped in this change (same placeholder posture as `court-keypoints.basketball.onnx` and `player-detection.onnx` above) — training a real jersey-number classifier is out of scope for this change.
