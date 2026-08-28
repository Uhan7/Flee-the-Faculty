"""Put both references in the same acoustic before cloning.

Pocket TTS reproduces the room along with the voice, so two references recorded
in different spaces produce two Pupils who sound like they are sitting in
different buildings. Worse, one of them sounds processed and the other does not,
and a listener reads that as a defect in the voice rather than in the recording.

The chain is deliberately the same for every voice. Matching matters more than
any individual step: a shared imperfection reads as a room, and an unshared one
reads as a mistake.

  1. High-pass at 80Hz          removes rumble the model would otherwise learn
  2. Peak normalise to -3dBFS   so level never stands in for character

Dereverberation and spectral gating are implemented below and OFF by default,
because measuring them showed they cost more than they bought. On a 214Hz male
reference the gate took spectral flatness from 0.0215 to 0.1686 and the spectral
centroid from 2326Hz to 4429Hz: it removed some room and added a great deal of
hiss, and the clone learns the hiss just as faithfully.

Room tone in a reference is a recording problem. The reliable fix is a quiet
room and a close mic, not a filter applied afterwards. Turn these on only with
a before-and-after listen, and check `acoustic_profile` both ways.
"""

import numpy as np
import librosa

HIGHPASS_HZ = 80.0
TARGET_PEAK_DBFS = -3.0

# WPE needs enough taps to cover the room tail. Ten taps at a 128-sample hop and
# 24kHz spans about 70ms of reverberation, which covers a normal room.
WPE_TAPS = 22
WPE_DELAY = 2
WPE_ITERATIONS = 5

N_FFT = 512
HOP = 128


def _stft(y: np.ndarray) -> np.ndarray:
    return librosa.stft(y, n_fft=N_FFT, hop_length=HOP)


def _istft(spec: np.ndarray, length: int) -> np.ndarray:
    return librosa.istft(spec, hop_length=HOP, n_fft=N_FFT, length=length)


def dereverberate(y: np.ndarray) -> np.ndarray:
    """Weighted prediction error dereverberation, single channel."""
    from nara_wpe.wpe import wpe

    spec = _stft(y)
    # nara_wpe wants (channels, frequency, frames)
    cleaned = wpe(
        spec[None, ...],
        taps=WPE_TAPS,
        delay=WPE_DELAY,
        iterations=WPE_ITERATIONS,
        statistics_mode="full",
    )
    return _istft(cleaned[0], len(y))


def spectral_gate(y: np.ndarray, reduction_db: float = 22.0) -> np.ndarray:
    """Push the room tone down without touching the speech above it.

    The noise floor is estimated per frequency band from the quietest tenth of
    frames, which is silence in any recording of someone talking. Subtracting a
    fixed amount below that floor opens the gaps back up; it does not sharpen the
    speech, and it is not meant to.
    """
    spec = _stft(y)
    magnitude, phase = np.abs(spec), np.angle(spec)

    floor = np.percentile(magnitude, 10, axis=1, keepdims=True)
    threshold = floor * 2.2
    reduction = 10.0 ** (-abs(reduction_db) / 20.0)

    mask = np.where(magnitude > threshold, 1.0, reduction)
    # Smooth the mask over time so gating does not chatter between frames.
    kernel = np.ones(3) / 3.0
    mask = np.apply_along_axis(lambda m: np.convolve(m, kernel, mode="same"), 1, mask)

    return _istft(magnitude * mask * np.exp(1j * phase), len(y))


def normalise(y: np.ndarray) -> np.ndarray:
    peak = float(np.max(np.abs(y))) if len(y) else 0.0
    if peak <= 0:
        return y
    return y * (10.0 ** (TARGET_PEAK_DBFS / 20.0) / peak)


def clean(
    path,
    sample_rate: int = 24000,
    dereverb: bool = False,
    gate: bool = False,
) -> tuple[np.ndarray, int]:
    """Run the chain. Same steps, same order, every voice.

    The two aggressive steps are opt-in. See the module docstring for what they
    measured when they were on by default.
    """
    y, sr = librosa.load(str(path), sr=sample_rate, mono=True)

    y = _highpass(y, sr)
    if dereverb:
        y = dereverberate(y)
    if gate:
        y = spectral_gate(y)
    y = normalise(y)
    return y.astype(np.float32), sr


def _highpass(y: np.ndarray, sr: int) -> np.ndarray:
    from scipy.signal import butter, sosfilt

    sos = butter(4, HIGHPASS_HZ / (sr / 2.0), btype="highpass", output="sos")
    return sosfilt(sos, y).astype(np.float32)


def acoustic_profile(y: np.ndarray, sr: int) -> dict:
    """The two numbers that say whether two references match.

    Dynamic range is the one that matters. A voice recorded in a live room has
    its gaps filled by the tail, which shows up as a small gap between the loud
    and quiet percentiles. That filled-in quality is what a listener calls echo.
    """
    env = librosa.feature.rms(y=y, frame_length=1024, hop_length=128)[0]
    db = 20.0 * np.log10(env + 1e-9)

    loud = db > np.percentile(db, 75)
    slopes = []
    for i in range(1, len(db) - 30):
        if loud[i] and not loud[i + 1]:
            segment = db[i : i + 25]
            if segment[0] - segment[-1] > 3:
                slopes.append((segment[0] - segment[-1]) / (25 * 128 / sr))

    decay = float(np.median(slopes)) if slopes else float("nan")
    return {
        "decay_db_per_s": decay,
        "rt60_s": 60.0 / decay if decay and decay == decay and decay > 0 else float("nan"),
        "dynamic_range_db": float(np.percentile(db, 90) - np.percentile(db, 25)),
    }
