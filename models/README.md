# models/

Exported ONNX model files consumed by `NBA.Inference`. Models are **not trained in this repository** — training happens out-of-repo in Python/Ultralytics (or equivalent); only the exported `.onnx` artifact is committed here. See `design.md` in `openspec/changes/init-nba-vision-platform/` for the training/inference split rationale.

## Naming convention

`<capability>.<sport>.onnx`, for example:

- `court-keypoints.basketball.onnx` — court keypoint detection model consumed by `vision/court-calibration`.
- `sport-classifier.onnx` — the sport classification model consumed by `vision/sport-classification` (not sport-specific by definition, so no `<sport>` segment).

A model file is not required for the application to run: every model-backed capability has a documented degraded/manual path when its model file is absent (e.g., manual court calibration, "unknown" sport classification) — see the relevant `specs/*/spec.md` for the exact fallback behavior.

## `sport-classifier.onnx`

Currently a CLIP ViT-B/32 **vision-encoder-only** export (via Hugging Face `optimum`), consumed by `ClipZeroShotSportClassifier` (`src/NBA.Vision/ClipZeroShotSportClassifier.cs`) per design.md's "Sport classification: CLIP/SigLIP zero-shot" decision — not a fine-tuned classifier. Verified signature (`python -c "import onnx; onnx.load(...)"`, not yet run through `ClipZeroShotSportClassifier` against a real captured frame):

- Input: `pixel_values`, shape `[batch, channels, height, width]` — matches `ClipZeroShotSportClassifier`'s default `inputName`/`inputSize` (224) exactly, no wiring changes needed.
- Output: `image_embeds`, shape `[batch, 512]` — matches the classifier's "take the first output" convention.

The corresponding **text** encoder (`text_model.onnx`, ~250MB) is deliberately **not** in this directory — it is never loaded by `NBA.Inference`/shipped with the app (see design.md: "no text encoder or tokenizer ships or runs in this app"). It lives at `tools/clip-text-encoder/` as an offline-only tool for generating the prompt-embeddings JSON `ClipPromptEmbeddings` reads — see that directory's README.

**Verified**: this file loads and runs successfully through `ClipZeroShotSportClassifier` end-to-end on this machine (throwaway harness, not part of the checked-in test suite) — `pixel_values` in, a 512-dim `image_embeds` out, ~74ms per classification on CPU — confirming the input/output-name assumptions above are correct against the *real* file, not just its inspected metadata.

**Not yet done**: the harness above used random placeholder prompt vectors (proves the plumbing works, proves nothing about classification *accuracy*) — no real prompt-embeddings JSON has been generated from the paired text encoder yet (needs a CLIP tokenizer, which `text_model.onnx` alone doesn't include — see `tools/clip-text-encoder/README.md`), so `ClipZeroShotSportClassifier` is not yet wired into `NBA.App`'s composition root, and real classification accuracy against actual captured frames is unverified.
