"""Piper, the engine the game ships with.

Piper is a plain ONNX model plus a JSON config. A voice is 60MB at medium
quality, it loads in under half a second, and it synthesises about 58 times
faster than playback on Apple silicon. That speed is why it runs server-side
without the round trip mattering, and why it was built for a Raspberry Pi.

Compare Pocket TTS in `synth.py`. That one clones a voice from 20 seconds of
audio, which is what makes it the right tool for testing your recordings, and
it stores a voice as a transformer KV cache pinned to one checkpoint, which is
what makes it the wrong tool to build a runtime on.
"""

from dataclasses import dataclass
from pathlib import Path
import time

import numpy as np

# Two stock voices, so the slot table can be tuned before your own are trained.
STAND_INS = {"girl": "en_US-amy-medium", "boy": "en_US-lessac-medium"}

DOWNLOAD_HINT = """
No Piper voice found. Download the two stand-ins into ./voices:

  uv run python -m piper.download_voices --download-dir voices \\
      en_US-amy-medium en_US-lessac-medium

Once your own voices are trained, pass them with --girl-model and --boy-model.
""".strip()


def _ensure_espeak_data() -> None:
    """Work around a data-path bug in piper-tts 1.7.0.

    The espeak bridge resolves its data directory to `site-packages` rather than
    to `site-packages/piper/espeak-ng-data`, and it ignores both the
    `espeak_data_dir` argument and `ESPEAK_DATA_PATH`. Without a fix, every call
    fails with "Error processing file .../phontab". Linking the data files where
    the bridge looks for them is the only thing that works.

    Remove this once the upstream path handling is fixed.
    """
    import piper

    package = Path(piper.__file__).parent
    data = package / "espeak-ng-data"
    site_packages = package.parent

    if not data.is_dir() or (site_packages / "phontab").exists():
        return

    for entry in data.iterdir():
        link = site_packages / entry.name
        if not link.exists():
            try:
                link.symlink_to(entry)
            except OSError:
                # A read-only or otherwise unwritable environment. Let the real
                # error surface from the synthesis call rather than guessing.
                return


@dataclass
class PiperEngine:
    """The Piper equivalent of `synth.Synth`. Same three things the CLI needs."""

    sample_rate: int
    load_seconds: float
    _voices: dict

    @classmethod
    def load(cls) -> "PiperEngine":
        _ensure_espeak_data()
        # Nothing global to load: a Piper voice is the model. Sample rate comes
        # from the first voice opened, and every medium voice is 22050Hz.
        return cls(sample_rate=22050, load_seconds=0.0, _voices={})

    def open_voice(self, model_path: Path):
        from piper import PiperVoice

        if not model_path.is_file():
            raise SystemExit(f"No such Piper voice: {model_path}\n\n{DOWNLOAD_HINT}")

        started = time.time()
        voice = PiperVoice.load(str(model_path))
        self.load_seconds += time.time() - started
        self.sample_rate = voice.config.sample_rate
        return voice

    def say(self, voice, text: str) -> tuple[np.ndarray, float]:
        started = time.time()
        chunks = [chunk.audio_float_array for chunk in voice.synthesize(text)]
        elapsed = time.time() - started
        if not chunks:
            return np.zeros(0, dtype=np.float32), elapsed
        return np.concatenate(chunks).astype(np.float32), elapsed


def resolve_model(base: str, explicit: str | None, voices_dir: Path) -> Path:
    """Where a base voice's model file lives: yours if given, otherwise a stand-in."""
    if explicit:
        return Path(explicit)
    return voices_dir / f"{STAND_INS[base]}.onnx"
