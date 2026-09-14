#!/usr/bin/env python3
"""
Runs text_model.onnx (this directory) over several prompt-template variations per sport and writes the
resulting L2-normalized, averaged ("prompt-ensembled") embeddings as JSON, in the shape
ClipPromptEmbeddings.LoadFromJson (src/NBA.Vision/ClipPromptEmbeddings.cs) expects. See design.md's "Sport
classification: CLIP/SigLIP zero-shot" decision - this script is the offline step that decision requires;
nothing here ships with or runs inside NBA.App.

Usage:
    python3 -m venv .venv && .venv/bin/pip install tokenizers onnxruntime numpy
    .venv/bin/python generate_prompt_embeddings.py

Requires tokenizer.json (already in this directory) and text_model.onnx (already in this directory).
Writes models/sport-classifier-prompts.clip.json (repo-root-relative), overwriting any existing file.

Why multiple templates per sport ("prompt ensembling"), not one prompt each (the v1 approach): live testing
with a single short prompt per sport (e.g. just "a basketball game") produced clearly wrong results - a
Swagger UI / code-editor screenshot scored highest on "baseball", and a real, unambiguous NBA broadcast frame
(scoreboard, dunk, crowd) scored only 14.5% on "basketball" against 85.5% on the "unknown" catch-all.
Independently re-running preprocessing in Python with CLIP's official bicubic-resize+center-crop (instead of
the app's nearest-neighbor squash) reproduced the same wrong result, ruling out a resize/preprocessing bug -
this is CLIP zero-shot's well-documented weakness with single terse prompts (the CLIP paper itself reports a
meaningful accuracy gain from averaging several prompt templates per class over one prompt per class).
Averaging multiple natural phrasings per class (and giving the "unknown" catch-all several concrete non-sport
scenarios instead of one vague sentence) is the standard fix, applied here.

To add a sport: add a "sport": [...] entry to PROMPT_TEMPLATES below and rerun. The catch-all "unknown" entry
(SportType.Unknown - see src/NBA.Vision/SportType.cs) is deliberate, not a placeholder: without at least one
non-sport prompt, a single- or all-sport prompt list has nothing for a genuinely unrelated frame to score
higher on, which defeats the confidence-threshold/"unknown" logic in SportClassificationCoordinator.
"""

import json
from pathlib import Path

import numpy as np
import onnxruntime as ort
from tokenizers import Tokenizer

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parent.parent
OUTPUT_PATH = REPO_ROOT / "models" / "sport-classifier-prompts.clip.json"

PROMPT_TEMPLATES = {
    "basketball": [
        "a basketball game",
        "a photo of a basketball game",
        "people playing basketball on a court",
        "a TV broadcast of an NBA basketball game",
        "a basketball video game screenshot",
    ],
    "soccer": [
        "a soccer match",
        "a photo of a soccer match",
        "people playing soccer on a field",
        "a TV broadcast of a soccer game",
        "a soccer video game screenshot",
    ],
    "baseball": [
        "a baseball game",
        "a photo of a baseball game",
        "people playing baseball on a field",
        "a TV broadcast of a baseball game",
        "a baseball video game screenshot",
    ],
    "tennis": [
        "a tennis match",
        "a photo of a tennis match",
        "people playing tennis on a court",
        "a TV broadcast of a tennis match",
        "a tennis video game screenshot",
    ],
    "american_football": [
        "an american football game",
        "a photo of an american football game",
        "people playing american football on a field",
        "a TV broadcast of an NFL football game",
        "an american football video game screenshot",
    ],
    "unknown": [
        "an unrelated photo or screenshot that is not a sports broadcast",
        "a screenshot of a website or web application",
        "a screenshot of a code editor or programming tool",
        "a screenshot of a computer desktop",
        "a photo unrelated to any sport",
        "a web browser showing a page unrelated to sports",
    ],
}

# Real capture sources are often a whole browser window (tabs, address bar, page chrome) around the actual
# video, not a clean full-bleed broadcast frame - added after live testing showed a sport prompt alone
# under-weights this common case. Appended per sport rather than folded into the base lists above so the
# "pure broadcast/game" phrasing stays the majority signal in the average.
_SPORT_NOUN_PHRASES = {
    "basketball": "basketball",
    "soccer": "soccer",
    "baseball": "baseball",
    "tennis": "tennis",
    "american_football": "american football",
}
for _sport, _noun in _SPORT_NOUN_PHRASES.items():
    _article = "an" if _noun[0] in "aeiou" else "a"
    PROMPT_TEMPLATES[_sport].append(f"a web browser window showing {_article} {_noun} video")
    PROMPT_TEMPLATES[_sport].append(f"a YouTube video of {_article} {_noun} game")


def main() -> None:
    tokenizer = Tokenizer.from_file(str(HERE / "tokenizer.json"))
    session = ort.InferenceSession(str(HERE / "text_model.onnx"), providers=["CPUExecutionProvider"])

    entries = []
    for sport, templates in PROMPT_TEMPLATES.items():
        vectors = []
        for prompt in templates:
            ids = np.array([tokenizer.encode(prompt).ids], dtype=np.int64)
            (embedding,) = session.run(None, {"input_ids": ids})
            vector = embedding[0]
            vectors.append(vector / np.linalg.norm(vector))  # normalize each template before averaging

        averaged = np.mean(vectors, axis=0)
        averaged = (averaged / np.linalg.norm(averaged)).astype(np.float32)  # re-normalize the average
        entries.append({
            "sport": sport,
            "prompt": f"[{len(templates)}-template average] " + " | ".join(templates),
            "embedding": averaged.tolist(),
        })
        print(f"{sport!r:20} <- {len(templates)} templates averaged")

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text(json.dumps(entries, indent=2))
    print(f"Wrote {len(entries)} prompt embeddings to {OUTPUT_PATH}")


if __name__ == "__main__":
    main()
