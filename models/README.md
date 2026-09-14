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

## `player-detection.onnx`

**Not yet present** — no trained model has been added to this directory yet (see `design.md`'s risk entry in `openspec/changes/add-player-detection/`). `App.axaml.cs` falls back to `NullPlayerDetector` (zero detections every frame) until this file exists.

Expected/assumed signature, consumed by `OnnxPlayerDetector` (`src/NBA.Vision/OnnxPlayerDetector.cs`) once a real file is added — a YOLO-family object detector, e.g. exported from a model trained on a dataset such as [Roboflow's `basketball-players-fy4c2`](https://universe.roboflow.com/roboflow-universe-projects/basketball-players-fy4c2) (pick a YOLO-based export variant, not the RF-DETR variant, to match this convention without a postprocessing rewrite):

- Input: `input`, shape `[1, 3, height, width]` (NCHW, RGB, values scaled to `[0, 1]`) — matches `ImagePreprocessing.ToNchwTensor`'s output exactly.
- Output: `output`, shape `[1, N, 6]` — `(x1, y1, x2, y2, confidence, classId)` per candidate box, box coordinates normalized to `[0, 1]`.

This is a **placeholder assumption**, not verified against any real exported model — common for YOLO ONNX exports, but should be checked against whatever file is actually added (`python -c "import onnx; onnx.load(...)"` to inspect the real output shape/name before wiring). `OnnxPlayerDetector` applies its own non-max suppression regardless of whether the exported model already performs it, so either an NMS-included or raw-anchor export works without a code change.
