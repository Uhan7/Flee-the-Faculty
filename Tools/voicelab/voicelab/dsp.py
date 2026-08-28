"""Pitch and rate offsets, and the clip report.

The offsets here are the preview of what the browser does at runtime. Keep the
two implementations on the same two parameters, semitones and rate, so a number
that sounds right in a preview sounds right in the game.

Pitch and rate have to move independently. Plain resampling couples them, which
rules out V2 ("high, slow"), so both sides use a phase vocoder: librosa here,
SoundTouch in the browser.
"""

import numpy as np
import librosa
import soundfile as sf


TARGET_PEAK_DBFS = -3.0


def apply_offsets(
    samples: np.ndarray,
    sample_rate: int,
    semitones: float,
    rate: float,
    gain_db: float = 0.0,
) -> np.ndarray:
    """Shift pitch, level the take, then change speed and apply gain.

    Levelling sits between the shift and the gain on purpose. Two base voices
    rarely arrive at the same loudness, and an accidental level difference reads
    as a deliberate character difference when you compare slots by ear. Normalise
    first so `gain_db` is the only loudness the table controls.
    """
    out = samples.astype(np.float32, copy=True)

    if abs(semitones) > 1e-6:
        out = librosa.effects.pitch_shift(y=out, sr=sample_rate, n_steps=semitones)

    peak = float(np.max(np.abs(out))) if len(out) else 0.0
    if peak > 0:
        out = out * (10.0 ** (TARGET_PEAK_DBFS / 20.0) / peak)

    if abs(rate - 1.0) > 1e-6:
        out = librosa.effects.time_stretch(y=out, rate=rate)

    if abs(gain_db) > 1e-6:
        out = out * (10.0 ** (gain_db / 20.0))

    return np.clip(out, -1.0, 1.0)


def load_excerpt(path, seconds: float | None, sample_rate: int = 24000) -> tuple[np.ndarray, int]:
    """Load the densest stretch of speech of the requested length.

    A reference recording has pauses in it. Picking the loudest continuous window
    rather than the first N seconds keeps the excerpt from opening on silence.
    """
    samples, sr = librosa.load(str(path), sr=sample_rate, mono=True)
    if seconds is None:
        return samples, sr
    want = int(seconds * sr)
    if len(samples) <= want:
        return samples, sr

    hop = 512
    rms = librosa.feature.rms(y=samples, frame_length=1024, hop_length=hop)[0]
    frames = max(1, want // hop)
    windowed = np.convolve(rms, np.ones(frames) / frames, mode="valid")
    start = int(np.argmax(windowed)) * hop
    return samples[start : start + want], sr


def median_f0(samples: np.ndarray, sample_rate: int) -> float:
    """Median voiced pitch, for confirming a slot landed where the table says.

    `pyin` rather than `yin`, and the difference is not cosmetic. `yin` returns a
    number for every frame including silence and breath, and it has no voicing
    flag to filter them out, so a take with long gaps reports the pitch of its
    gaps. It also slips an octave on low voices.

    Both faults pushed the same way on the male reference: `yin` read the boy
    clone at roughly 160Hz where it actually speaks near 240Hz, which asked for a
    lift of more than eight semitones, hit the clamp, and put V4 at 417Hz. The
    boys came out higher than the girls. `pyin` reports a voicing decision per
    frame, and only voiced frames count here.
    """
    if not len(samples):
        return float("nan")

    f0, voiced, _probability = librosa.pyin(
        samples.astype(np.float32), fmin=60, fmax=600, sr=sample_rate
    )
    voiced_f0 = f0[voiced & np.isfinite(f0)]
    return float(np.median(voiced_f0)) if len(voiced_f0) else float("nan")


def write_wav(path, samples: np.ndarray, sample_rate: int) -> None:
    sf.write(str(path), samples, sample_rate, subtype="PCM_16")


def report_clip(path) -> dict:
    """What a reference recording looks like before it is cloned.

    Pocket TTS reproduces the recording, including its faults, so a bad clip
    produces a faithful clone of a bad clip. These are the numbers worth seeing
    before spending a session on the real reads.
    """
    samples, sample_rate = librosa.load(str(path), sr=None, mono=False)

    channels = 1 if samples.ndim == 1 else samples.shape[0]
    mono = samples if samples.ndim == 1 else librosa.to_mono(samples)
    duration = len(mono) / sample_rate

    peak = float(np.max(np.abs(mono))) if len(mono) else 0.0
    peak_dbfs = 20.0 * np.log10(peak) if peak > 0 else -np.inf

    # Voiced frames carry the speech; the quietest decile approximates the room.
    rms = librosa.feature.rms(y=mono, frame_length=1024, hop_length=256)[0]
    speech = float(np.percentile(rms, 90))
    floor = float(np.percentile(rms, 10))
    snr_db = 20.0 * np.log10(speech / floor) if floor > 0 else np.inf

    trimmed, _ = librosa.effects.trim(mono, top_db=30)
    speech_seconds = len(trimmed) / sample_rate

    f0 = librosa.yin(mono, fmin=60, fmax=500, sr=sample_rate)
    voiced = f0[np.isfinite(f0)]
    median_f0 = float(np.median(voiced)) if len(voiced) else float("nan")

    clipped = int(np.sum(np.abs(mono) >= 0.999))

    return {
        "path": str(path),
        "sample_rate": sample_rate,
        "channels": channels,
        "duration_s": duration,
        "speech_s": speech_seconds,
        "peak_dbfs": peak_dbfs,
        "snr_db": snr_db,
        "median_f0_hz": median_f0,
        "clipped_samples": clipped,
    }


def verdicts(report: dict) -> list[tuple[str, str]]:
    """Turn a clip report into pass/warn/fail lines with the reason attached."""
    out: list[tuple[str, str]] = []

    speech = report["speech_s"]
    if speech >= 20.0:
        out.append(("pass", f"{speech:.1f}s of speech, at or above the 20s Kyutai quotes"))
    elif speech >= 12.0:
        out.append(("warn", f"{speech:.1f}s of speech, usable for a test, short for a ship"))
    else:
        out.append(("fail", f"{speech:.1f}s of speech, below the 12s floor for a stable clone"))

    if report["channels"] != 1:
        out.append(("warn", f"{report['channels']} channels, mix to mono before cloning"))

    if report["sample_rate"] < 24000:
        out.append(("fail", f"{report['sample_rate']}Hz, below the model's 24000Hz output"))

    if report["clipped_samples"] > 0:
        out.append(("fail", f"{report['clipped_samples']} clipped samples, re-record quieter"))
    elif report["peak_dbfs"] > -1.0:
        out.append(("warn", f"peak {report['peak_dbfs']:.1f} dBFS, leave headroom near -3"))
    elif report["peak_dbfs"] < -20.0:
        out.append(("warn", f"peak {report['peak_dbfs']:.1f} dBFS, too quiet, the clone will lift noise"))

    if report["snr_db"] < 20.0:
        out.append(("fail", f"{report['snr_db']:.1f} dB signal to noise, the room comes through"))
    elif report["snr_db"] < 30.0:
        out.append(("warn", f"{report['snr_db']:.1f} dB signal to noise, quieter room preferred"))

    return out
