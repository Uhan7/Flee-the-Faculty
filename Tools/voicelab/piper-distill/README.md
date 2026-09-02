# Piper distillation

Fine-tunes a Piper voice on a corpus the Chatterbox clone reads, so the girl
speaks with the clone's identity at Piper's speed. Chatterbox is the voice that
passed the maintainer's ear and costs about 30 seconds a line; Piper is 30 to
50 times real time on the CPU the service already pays for. The teacher reads
a thousand sentences once; the student learns to be her.

If it works by ear, this closes the split `chatterbox/README.md` documents:
the exported model replaces the girl's base voice in the service's `voices/`
and `speech/slots.py`, baked and live lines come from one Piper again, and
ADR-0013's one-engine rule is restored. `chatterbox/bake.py` then retires; the
corpus generator is the part of that directory that remains load-bearing.

Everything runs on this Mac. Training uses MPS with CPU fallback for the few
unsupported ops.

## The whole run

1. `../chatterbox`: `uv run python corpus.py`, about 5 hours, resumable.
2. Here, once: `./setup_env.sh`. It builds the Cython alignment extension the
   piper-tts wheel ships unbuilt, downloads the 807MB `amy-medium` checkpoint,
   and sanitises its 2023 pickle for torch 2.6.
3. `./train.sh`, as long as you can spare. Checkpoints save continuously, the
   best five by validation mel loss plus `last.ckpt`, so stop whenever and
   export whatever exists. Expect roughly a day on MPS for the full 1,000
   epochs; listen along the way rather than waiting it out.
4. `./export.sh lightning_logs/version_N/checkpoints/<best>.ckpt`, then listen
   to it against `../samples/girl3.wav`.
5. If it passes: copy `export/girl-clone.onnx` and `export/girl-clone.onnx.json`
   into the service repository's `voices/`, point the girl's base voice at it
   in `src/flee/speech/slots.py`, and re-measure the slot lift table with
   `scripts/measure_voices.py`. The clone sits near 222Hz against amy's 201Hz,
   so the lift to the child register shrinks by about 1.7 semitones.

## What decides the outcome

The student cannot exceed its corpus. If the exported voice drifts from the
clone, the levers in order: more corpus (add sentence files under
`../chatterbox/sentences/` and re-run `corpus.py`), more epochs, and only then
training knobs. Validation mel loss saturates early on VITS; the adversarial
losses keep improving the sound after it flattens, which is why the schedule is
long and the verdict is your ear, not the curve.
