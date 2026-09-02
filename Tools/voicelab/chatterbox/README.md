# Chatterbox: the clone that teaches

Renders speech in a zero-shot clone of `../samples/girl3.wav`, using
[Chatterbox](https://github.com/resemble-ai/chatterbox) (Resemble AI, MIT).
Chosen by ear on 1 September 2026: the clone passed the maintainer's identity
bar against the recording, and Piper and Pocket TTS did not.

What remains load-bearing here is `corpus.py`. Chatterbox is 30 seconds a line,
so it never ships: instead it reads the sentence bank in `sentences/` once, and
`../piper-distill` fine-tunes a Piper voice on the result. That student is the
girl's shipped voice, named in the service's `slots.py`, speaking both baked
and live lines in the one engine ADR-0013 requires. To improve her, add
sentences, re-run `corpus.py`, retrain, re-export.

`bake.py` is retired. For one day it overwrote the girl's baked clips with raw
Chatterbox renders while her live lines stayed Piper, which split one Character
into two voices; the distillation ended that on 2 September 2026. It still runs
if a raw-clone bake is ever wanted, and its key logic still matches
`VoiceKey.cs`.

First run downloads about 2GB of weights into the Hugging Face cache and
builds `samples/girl3-tight.wav`, the silence-trimmed reference, from the raw
recording if it is missing.

## Adding a voice

Give `REFERENCES` in `bake.py` the voice's name and about ten seconds of one
speaker, recorded close and clean. Silence is cut automatically; density is
what conditions the clone, not length.
