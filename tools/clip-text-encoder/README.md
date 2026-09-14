# tools/clip-text-encoder/

`text_model.onnx` — the CLIP ViT-B/32 **text** encoder (Hugging Face `optimum` export), paired with
`models/sport-classifier.onnx` (the vision encoder). This is an **offline-only tool, not part of the app**:

- Never loaded by `NBA.Inference` or referenced by any `src/` project — per design.md's "Sport classification:
  CLIP/SigLIP zero-shot" decision, no text encoder or tokenizer ships or runs in `NBA.App`.
- Its only job is to be run **once, on a dev machine**, over the fixed list of sport prompts (e.g.
  `"a basketball game"`, `"a soccer match"`) to produce the prompt-embeddings JSON that
  `ClipPromptEmbeddings.LoadFromJson` (`src/NBA.Vision/ClipPromptEmbeddings.cs`) reads at app startup.

## Verified model signature

```
input:  input_ids  [batch_size, sequence_length]  (int64 token ids)
output: text_embeds [batch_size, 512]
```

`input_ids` means this model alone is **not** enough to go from a plain-text prompt to an embedding — it needs
a CLIP tokenizer (BPE vocab + merges) to turn `"a basketball game"` into token ids first. That tokenizer is
**not included here yet** — it wasn't part of what was downloaded (`vision_model.onnx` + `text_model.onnx`
only). Running this encoder end-to-end still requires either:

- the matching Hugging Face tokenizer files (`vocab.json` + `merges.txt`, or `tokenizer.json`) for whatever
  CLIP checkpoint these weights came from, run through a Python script (e.g. `transformers`' `CLIPTokenizer`
  to produce `input_ids`, then `onnxruntime` to run this model), or
- an equivalent .NET/other-language CLIP BPE tokenizer implementation, if avoiding Python entirely is preferred.

## Not yet done

- No tokenizer has been obtained/wired up.
- No prompt-embeddings JSON has been generated (expected shape — see `ClipPromptEmbeddings`'s doc comment —
  `[{ "sport": "...", "prompt": "...", "embedding": [...] }, ...]`, 512-dim per entry to match this model's
  output and `models/sport-classifier.onnx`'s vision-encoder output).
- Once generated, that JSON should land somewhere `NBA.App`'s composition root can load at startup (not yet
  decided/wired — `models/` is documented as ONNX-files-only, so it likely belongs in `models/` alongside
  `sport-classifier.onnx` under a name like `sport-classifier-prompts.clip.json`, but this hasn't been decided).
