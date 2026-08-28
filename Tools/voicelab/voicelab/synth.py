"""The one place this tool calls Pocket TTS.

Two paths in, one out. A reference recording produces a cloned voice state; a
catalogue name produces a stock one. Both return the same object, so the rest of
the tool does not know or care which it got, and the slot table can be tuned
before the cloning weights are unlocked.

Cloning is gated: accept the terms at https://huggingface.co/kyutai/pocket-tts
and run `uvx hf auth login`. Without that the public weights come down with the
cloning path stripped and `--catalog` is the only mode that works.
"""

from dataclasses import dataclass
from pathlib import Path
import time

import numpy as np

VOICE_CLONING_HINT = """
Voice cloning is gated behind the model's terms. To unlock it:

  1. Open https://huggingface.co/kyutai/pocket-tts and accept the terms.
  2. Run: uvx hf auth login

Until then, run with --catalog to tune the slot table against a stock voice.
""".strip()


@dataclass
class Synth:
    """A loaded Pocket TTS model, plus the timings worth reporting."""

    model: object
    sample_rate: int
    load_seconds: float

    @classmethod
    def load(cls) -> "Synth":
        from pocket_tts import TTSModel

        started = time.time()
        model = TTSModel.load_model()
        return cls(
            model=model,
            sample_rate=model.sample_rate,
            load_seconds=time.time() - started,
        )

    def voice_from_recording(self, wav_path: Path):
        """Clone a voice from a reference recording. Needs the gated weights."""
        try:
            return self.model.get_state_for_audio_prompt(str(wav_path))
        except ValueError as error:
            if "voice cloning" in str(error).lower():
                raise SystemExit(f"{error}\n\n{VOICE_CLONING_HINT}") from error
            raise

    def best_clone(self, wav_path: Path, probe: str, attempts: int = 5):
        """Clone several times and keep the one that matches the source pitch.

        Conditioning is not deterministic. Five clones of the same 22-second
        reference measured 148, 214, 211, 218 and 133Hz against a 214Hz source,
        so two of the five dropped roughly a fifth below the speaker. One clone
        is a coin flip; picking by measured pitch is not.

        Returns the chosen state plus every ratio, so the caller can report how
        wide the spread was rather than hiding it.
        """
        from . import dsp
        import librosa

        source, _ = librosa.load(str(wav_path), sr=24000, mono=True)
        source_f0 = dsp.median_f0(source, 24000)

        best = None
        ratios: list[float] = []
        for _ in range(max(1, attempts)):
            state = self.voice_from_recording(wav_path)
            audio, _elapsed = self.say(state, probe)
            ratio = dsp.median_f0(audio, self.sample_rate) / source_f0
            ratios.append(ratio)
            error = abs(ratio - 1.0)
            if best is None or error < best[0]:
                best = (error, state, ratio)

        return best[1], best[2], ratios, source_f0

    def voice_from_catalogue(self, name: str):
        """Load a stock voice by name. Works with the ungated weights."""
        return self.model.get_state_for_audio_prompt(name)

    def voice_from_state(self, safetensors_path: Path):
        """Reload a voice this tool exported earlier."""
        return self.model.get_state_for_audio_prompt(str(safetensors_path))

    def export(self, voice_state, safetensors_path: Path) -> int:
        from pocket_tts import export_model_state

        safetensors_path.parent.mkdir(parents=True, exist_ok=True)
        export_model_state(voice_state, str(safetensors_path))
        return safetensors_path.stat().st_size

    def say(self, voice_state, text: str) -> tuple[np.ndarray, float]:
        """Generate one line. Returns float32 samples and the seconds it took."""
        started = time.time()
        audio = self.model.generate_audio(voice_state, text)
        elapsed = time.time() - started
        return audio.numpy().astype(np.float32), elapsed

    def say_paced(
        self, voice_state, text: str, pause_ms: int = 0
    ) -> tuple[np.ndarray, float]:
        """Speak sentence by sentence, with a set pause between sentences.

        Pause density is a property of the speaker, not of the engine, and the
        clone copies it faithfully: a reference at 16% silence produced a clone
        at 15%, and one at 37% produced 46%. Two people who breathe differently
        therefore give you two Pupils who breathe differently, which reads as one
        of them rushing.

        Splitting the line and inserting the gap here fixes that without touching
        the clone, and it gives per-Character pacing for free. GDD 8.3 wants V2
        dreamy and V1 fast, and this is the knob that does it.
        """
        import re

        sentences = [s for s in re.split(r"(?<=[.!?])\s+", text.strip()) if s]
        if len(sentences) < 2 or pause_ms <= 0:
            return self.say(voice_state, text)

        gap = np.zeros(int(self.sample_rate * pause_ms / 1000.0), dtype=np.float32)
        pieces: list[np.ndarray] = []
        total = 0.0
        for index, sentence in enumerate(sentences):
            audio, elapsed = self.say(voice_state, sentence)
            total += elapsed
            pieces.append(audio)
            if index < len(sentences) - 1:
                pieces.append(gap)

        return np.concatenate(pieces), total
