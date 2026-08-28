"""Render the same preset conversation in both cloned voices, for A/B review.

Every line is spoken by the girl voice and then the boy voice, back to back, so
the only thing that differs between the two takes is the voice. That is the
comparison that decides casting, and it is hard to make from separate files.

Lines are opening lines and Misconceptions from the service's presets. Nothing
here touches a Correction; see the service's CLAUDE.md rule 2 and GDD 18.2.
"""

import warnings

warnings.filterwarnings("ignore")

import subprocess
import sys
from pathlib import Path

import numpy as np
import soundfile as sf

from voicelab.synth import Synth

# presetId -> the lines a Pupil actually says, in the order they say them.
CONVERSATIONS: dict[str, list[str]] = {
    "photosynthesis": [
        "Plants eat the dirt, right? That's what the soil is for.",
        "Plants take their food out of the soil, which is why you water them and keep them in a pot.",
        "Leaves are just green. That's their colour. It isn't for anything.",
    ],
    "gmdas": [
        "The M is before the D, so multiplication always goes first. That's the whole point of the letters.",
        "GMDAS gives the order, so you do all the multiplying first and all the dividing after it.",
        "You add before you subtract. A comes before S, that's just the order of the rule.",
    ],
    "archipelago": [
        "It's only a theory, so it's just a guess. Nobody actually knows how the islands got here.",
        "The islands have just always been there. The ground doesn't go anywhere.",
    ],
}

# Label by the part each voice plays, not by which file it came from. Which
# recording feeds which group changes as casting settles, and a filename that
# says "friend1" while the voice has moved to the boys is worse than no label.
VOICES = [("girl voice", "girl"), ("boy voice", "boy")]
OUT = Path("listen/convo")
SR = 24000


def announce(text: str, path: Path) -> np.ndarray:
    """A spoken label, so the file is followable without watching a filename."""
    aiff = path.with_suffix(".aiff")
    subprocess.run(
        ["say", "-v", "Daniel", "-r", "190", "-o", str(aiff), text],
        check=True,
        capture_output=True,
    )
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", str(aiff),
         "-ac", "1", "-ar", str(SR), str(path)],
        check=True,
    )
    aiff.unlink(missing_ok=True)
    return sf.read(str(path))[0].astype(np.float32)


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    silence = np.zeros(int(0.45 * SR), dtype=np.float32)
    long_gap = np.zeros(int(0.9 * SR), dtype=np.float32)

    synth = Synth.load()
    states = {
        who: synth.voice_from_state(Path(f"out-real/{base}.safetensors"))
        for who, base in VOICES
    }

    for preset, script in CONVERSATIONS.items():
        reel: list[np.ndarray] = []
        reel.append(announce(preset.replace("-", " "), OUT / f"_say_{preset}.wav"))
        reel.append(long_gap)

        for index, text in enumerate(script, start=1):
            print(f"\n{preset} line {index}: {text[:70]}...")
            reel.append(announce(f"Line {index}", OUT / f"_say_line{index}.wav"))
            reel.append(silence)

            for who, _base in VOICES:
                audio, elapsed = synth.say(states[who], text)
                peak = float(np.max(np.abs(audio))) or 1.0
                audio = audio * (0.7 / peak)

                name = OUT / f"{preset}_line{index}_{who.replace(" ", "-")}.wav"
                sf.write(str(name), audio, synth.sample_rate)
                print(f"   {who}: {len(audio) / synth.sample_rate:.1f}s in {elapsed:.2f}s -> {name.name}")

                reel.append(announce(who, OUT / f"_say_{who.replace(" ", "-")}.wav"))
                reel.append(silence)
                reel.append(audio)
                reel.append(silence)

            reel.append(long_gap)

        combined = OUT / f"{preset}-both-voices.wav"
        sf.write(str(combined), np.concatenate(reel), synth.sample_rate)
        print(f"  -> {combined}")

    for stray in OUT.glob("_say_*"):
        stray.unlink()
    return 0


if __name__ == "__main__":
    sys.exit(main())
