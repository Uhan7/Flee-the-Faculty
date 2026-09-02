"""Re-render the girl's authored lines in the voice cloned from her recording.

This is a second engine for one voice, and that is a decision, not an accident.
ADR-0013 put every rendered line through the service's Piper engine so a baked
line and a live line could never drift apart. The maintainer heard the Piper
girl next to a Chatterbox zero-shot clone of `samples/girl3.wav` on 1 September
2026 and chose the clone: an authored girl line is now baked here, and what she
says live in an Encounter still arrives from `POST /v1/speech` as Piper. Those
are two voices for one Character until live synthesis moves too, so this script
exists to make the better voice shippable, not to close that gap.

Only voices named in REFERENCES are re-rendered. Everything else is skipped and
left to `scripts/bake_lines.py` in the service repository, so the order is:
bake there first, then run this with `--force` to overwrite the girl's clips.

    uv run python bake.py --force

The key logic is ported from that script and checked against the same golden
cases before every run, for the same reason it has them: a drift bakes every
clip correctly under a name the client will never look up, and the only symptom
is a game that has quietly gone back to syllable ticks.
"""

from __future__ import annotations

import argparse
import json
import time
import wave
from pathlib import Path

HERE = Path(__file__).resolve().parent
VOICELAB = HERE.parent
CLIENT = VOICELAB.parent.parent

# The voices this script owns, each with the recording it clones. A voice that
# is not here is not this script's business.
REFERENCES: dict[str, Path] = {
    "girl": VOICELAB / "samples" / "girl3-tight.wav",
}

# The raw recording the tight reference is cut from, when it is missing.
RAW_REFERENCES: dict[str, Path] = {
    "girl": VOICELAB / "samples" / "girl3.wav",
}

FNV_OFFSET_BASIS = 0xCBF29CE484222325
FNV_PRIME = 0x100000001B3
MASK64 = 0xFFFFFFFFFFFFFFFF


def normalise(text: str) -> str:
    """Every run of whitespace becomes one space, and the ends are trimmed."""
    return " ".join((text or "").split())


def fingerprint(normalised_text: str) -> str:
    """FNV-1a over the UTF-8 bytes, 64 bits, lower-case hex. `VoiceKey.cs`."""
    hashed = FNV_OFFSET_BASIS
    for byte in normalised_text.encode("utf-8"):
        hashed ^= byte
        hashed = (hashed * FNV_PRIME) & MASK64
    return f"{hashed:016x}"


def key_for(voice: str, text: str) -> str:
    normalised = normalise(text)
    if not voice or not normalised:
        return ""
    return f"{voice.lower()}_{fingerprint(normalised)}"


# The same four cases `scripts/bake_lines.py` checks, checked here for the same
# reason: the moment this file and `VoiceKey.cs` disagree is the moment to stop.
GOLDEN: tuple[tuple[str, str, str], ...] = (
    ("girl", "Hello there.", "girl_892362dd056bbf55"),
    ("girl", "Plants eat the dirt, right?", "girl_365fe4b4b1fe52f8"),
    ("boy", "  spaced   out\n\nline  ", "boy_5473212866159303"),
    ("girl", "Café naïve résumé — unicode.", "girl_c522c547d1bd00df"),
)


def check_golden() -> None:
    for voice, text, expected in GOLDEN:
        actual = key_for(voice, text)
        if actual != expected:
            raise SystemExit(
                f"This script no longer agrees with VoiceKey.cs: {voice} {text!r} "
                f"gives {actual}, expected {expected}."
            )


def tight_reference(voice: str) -> Path:
    """The clone reference: the recording with its silences cut out.

    About nine dense seconds condition a clone better than twelve loose ones,
    and rebuilding the cut here rather than committing it means the reference
    can always be traced back to the recording it came from.
    """
    reference = REFERENCES[voice]
    if reference.is_file():
        return reference

    raw = RAW_REFERENCES[voice]
    if not raw.is_file():
        raise SystemExit(f"No reference recording at {raw}.")

    import librosa
    import numpy as np
    import soundfile

    audio, rate = librosa.load(str(raw), sr=24000, mono=True)
    gap = np.zeros(int(0.15 * rate), dtype=np.float32)
    voiced = [audio[a:b] for a, b in librosa.effects.split(audio, top_db=30)]
    tight = np.concatenate([part for segment in voiced for part in (segment, gap)])
    soundfile.write(str(reference), tight, rate)
    print(f"cut {reference.name}: {len(tight) / rate:.1f}s of {len(audio) / rate:.1f}s")
    return reference


def load_chatterbox():
    """The model on the fastest device this Mac has.

    The Chatterbox checkpoints were saved on CUDA, so on a Mac every torch.load
    inside from_pretrained needs a map_location. This is the patch the
    Chatterbox README prescribes. `corpus.py` loads through here too.
    """
    import torch

    device = "mps" if torch.backends.mps.is_available() else "cpu"
    map_location = torch.device(device)
    torch_load = torch.load
    torch.load = lambda *a, **k: torch_load(*a, **{"map_location": map_location, **k})

    from chatterbox.tts import ChatterboxTTS

    return ChatterboxTTS.from_pretrained(device=device), device


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lines", default=str(VOICELAB / "lines-to-bake.json"))
    parser.add_argument("--out", default=str(CLIENT / "Assets" / "Audio" / "Voices"))
    parser.add_argument("--force", action="store_true", help="Re-render clips that exist.")
    args = parser.parse_args(argv)

    check_golden()

    line_path = Path(args.lines)
    if not line_path.is_file():
        raise SystemExit(
            f"No line list at {line_path}. In Unity, run "
            f"Flee the Faculty > Voices > Export Dialogue Lines first."
        )
    entries = json.loads(line_path.read_text(encoding="utf-8")).get("lines") or []

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    todo: list[tuple[str, str, Path]] = []
    already = 0
    skipped = 0
    for entry in entries:
        voice, text = entry.get("voice", ""), entry.get("text", "")
        if voice not in REFERENCES:
            skipped += 1
            continue
        key = key_for(voice, text)
        if key != entry.get("key"):
            raise SystemExit(
                f"Key mismatch on {voice} {text[:40]!r}: the client exported "
                f"{entry.get('key')}, this computes {key}. VoiceKey.cs and "
                f"this script have drifted apart."
            )
        destination = out_dir / f"{key}.wav"
        if destination.exists() and not args.force:
            already += 1
            continue
        todo.append((voice, normalise(text), destination))

    print(
        f"{len(entries)} lines: {skipped} not this script's voice, "
        f"{already} already baked, {len(todo)} to render\n"
    )
    if not todo:
        print(f"Nothing to do. Pass --force to re-render into {out_dir}.")
        return 0

    import torch
    import torchaudio

    started = time.perf_counter()
    model, device = load_chatterbox()
    print(f"Loaded Chatterbox on {device} in {time.perf_counter() - started:.1f}s\n")
    print(f"{'voice':<6} {'audio':>7} {'synth':>7}  line")
    print("-" * 72)

    for voice, text, destination in todo:
        # Seeding by the line's own fingerprint makes a re-bake start from the
        # same noise. MPS does not promise bit-identical sampling, so the
        # committed clip, not the seed, is the artifact of record.
        torch.manual_seed(int(fingerprint(text), 16) & 0x7FFFFFFF)
        rendered = time.perf_counter()
        audio = model.generate(text, audio_prompt_path=str(tight_reference(voice)))
        elapsed = time.perf_counter() - rendered
        torchaudio.save(
            str(destination), audio.cpu(), model.sr, encoding="PCM_S", bits_per_sample=16
        )
        with wave.open(str(destination)) as handle:
            seconds = handle.getnframes() / handle.getframerate()
        print(f"{voice:<6} {seconds:>6.2f}s {elapsed:>6.2f}s  {text[:40]}")

    print(f"\n  -> {out_dir}")
    print(
        "\nThe file names did not change, so Unity reimports these in place. "
        "Rebuild Voice Library only if new lines were exported."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
