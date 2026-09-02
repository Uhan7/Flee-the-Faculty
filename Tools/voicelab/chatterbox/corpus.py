"""Render the distillation corpus: the cloned girl reading the sentence bank.

The plan this serves: Chatterbox is the voice that passed the maintainer's ear
and is 30 seconds a line; Piper is 30 to 50 times real time on a CPU and is the
engine the service already ships. So Chatterbox reads a thousand sentences once,
here, slowly, and a Piper model is fine-tuned on the result. The student then
speaks with the clone's identity at Piper's speed, and ADR-0013's one-engine
rule comes back: baked and live lines from one Piper again.

The corpus is only as good as its worst take, because the student cannot tell a
take from a habit. Chatterbox occasionally rushes, drags, or garbles a line, so
every render is screened: speaking density in characters per second, and median
pitch against the reference speaker's range. A take that fails is re-rendered
once from a different seed, and a sentence that fails twice is dropped and
logged rather than kept. Arctic filler is expendable; a clean corpus is not.

Renders land in `distill-corpus/wav/<fingerprint>.wav` at 22,050Hz, which is
what the `amy-medium` checkpoint the student starts from was trained at.
Already-rendered sentences are skipped, so the run resumes after interruption,
and `metadata.csv` (LJSpeech style, `id|text`) is rebuilt from whatever is on
disk at the end of every run.

    uv run python corpus.py            # the whole bank, about 5 hours
    uv run python corpus.py --limit 8  # smoke test
"""

from __future__ import annotations

import argparse
import time
import wave
from pathlib import Path

from bake import REFERENCES, fingerprint, load_chatterbox, normalise, tight_reference

HERE = Path(__file__).resolve().parent
SENTENCES = HERE / "sentences"
SAMPLE_RATE = 22050

# A ten-year-old reading a sentence lands in this band. Outside it the take is
# rushed, dragging, or has extra or missing words.
MIN_CHARS_PER_SECOND = 8.0
MAX_CHARS_PER_SECOND = 28.0
# The reference speaker's median is 222Hz. A take outside this band is the
# model wandering off the clone, not her having a loud day.
PITCH_LOW = 150.0
PITCH_HIGH = 330.0


def sentence_bank() -> list[str]:
    """Every sentence file, normalised, deduplicated, in stable order."""
    seen: set[str] = set()
    bank: list[str] = []
    for path in sorted(SENTENCES.glob("*.txt")):
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.lstrip().startswith("#"):
                continue
            text = normalise(line)
            if text and text not in seen:
                seen.add(text)
                bank.append(text)
    return bank


def rejection(text: str, seconds: float, pitch: float) -> str:
    """Why this take cannot go in the corpus, or an empty string."""
    density = len(text) / seconds if seconds else 0.0
    if density < MIN_CHARS_PER_SECOND:
        return f"drags: {density:.1f} chars/s"
    if density > MAX_CHARS_PER_SECOND:
        return f"rushes: {density:.1f} chars/s"
    if pitch == pitch and not PITCH_LOW <= pitch <= PITCH_HIGH:
        return f"off voice: {pitch:.0f}Hz"
    return ""


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", default=str(HERE / "distill-corpus"))
    parser.add_argument("--limit", type=int, default=0, help="Render only the first N sentences.")
    parser.add_argument("--voice", default="girl", choices=sorted(REFERENCES))
    args = parser.parse_args(argv)

    bank = sentence_bank()
    todo_bank = bank[: args.limit] if args.limit else bank

    out_dir = Path(args.out)
    wav_dir = out_dir / "wav"
    wav_dir.mkdir(parents=True, exist_ok=True)
    rejected_log = out_dir / "rejected.log"

    todo = []
    for text in todo_bank:
        destination = wav_dir / f"{fingerprint(text)}.wav"
        if not destination.exists():
            todo.append((text, destination))
    print(f"{len(todo_bank)} sentences, {len(todo_bank) - len(todo)} rendered already, {len(todo)} to go")

    if todo:
        import librosa
        import numpy as np
        import torch
        import torchaudio

        reference = str(tight_reference(args.voice))
        started = time.perf_counter()
        model, device = load_chatterbox()
        print(f"Loaded Chatterbox on {device} in {time.perf_counter() - started:.1f}s\n")

        started = time.perf_counter()
        rendered_seconds = 0.0
        dropped = 0
        for index, (text, destination) in enumerate(todo, start=1):
            for attempt in range(2):
                torch.manual_seed((int(fingerprint(text), 16) + attempt) & 0x7FFFFFFF)
                audio = model.generate(text, audio_prompt_path=reference)
                seconds = audio.shape[-1] / model.sr
                signal = audio.squeeze(0).cpu().numpy()
                f0, _, _ = librosa.pyin(
                    signal, sr=model.sr, fmin=100, fmax=450, frame_length=1024
                )
                pitch = float(np.nanmedian(f0))
                reason = rejection(text, seconds, pitch)
                if not reason:
                    resampled = torchaudio.functional.resample(audio.cpu(), model.sr, SAMPLE_RATE)
                    torchaudio.save(
                        str(destination), resampled, SAMPLE_RATE,
                        encoding="PCM_S", bits_per_sample=16,
                    )
                    rendered_seconds += seconds
                    break
                with rejected_log.open("a", encoding="utf-8") as handle:
                    handle.write(f"take {attempt + 1} {reason}: {text}\n")
            else:
                dropped += 1

            elapsed = time.perf_counter() - started
            remaining = elapsed / index * (len(todo) - index)
            print(
                f"[{index}/{len(todo)}] {rendered_seconds / 60:.1f} min of speech, "
                f"about {remaining / 60:.0f} min left: {text[:48]}",
                flush=True,
            )
        if dropped:
            print(f"\n{dropped} sentences dropped after two bad takes. See {rejected_log}.")

    texts_by_id = {fingerprint(text): text for text in bank}
    rows = []
    total = 0.0
    for path in sorted(wav_dir.glob("*.wav")):
        text = texts_by_id.get(path.stem)
        if text is None:
            print(f"stale clip not in the sentence bank, ignored: {path.name}")
            continue
        with wave.open(str(path)) as handle:
            total += handle.getnframes() / handle.getframerate()
        rows.append(f"{path.stem}|{text}")
    (out_dir / "metadata.csv").write_text("\n".join(rows) + "\n", encoding="utf-8")
    print(f"\nmetadata.csv: {len(rows)} clips, {total / 60:.1f} minutes of speech")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
