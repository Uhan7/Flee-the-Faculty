"""Find the cleanest continuous speech window in a long recording.

Scores every candidate window on three things a clone cares about: how much of
it is voiced speech rather than silence, how far the speech sits above the room,
and how steady the pitch is. A window that scores well is one speaker talking
without long gaps, which is what the reference should be.
"""
import sys, warnings
import numpy as np, librosa
warnings.filterwarnings("ignore")

path, want = sys.argv[1], float(sys.argv[2])
y, sr = librosa.load(path, sr=24000, mono=True)

hop = 256
rms = librosa.feature.rms(y=y, frame_length=1024, hop_length=hop)[0]
f0 = librosa.yin(y, fmin=60, fmax=400, sr=sr, frame_length=1024, hop_length=hop)
voiced_prob = librosa.feature.zero_crossing_rate(y, frame_length=1024, hop_length=hop)[0]

floor = np.percentile(rms, 10)
speech_gate = max(floor * 3, np.percentile(rms, 55))
frames_per_window = int(want * sr / hop)
step = int(1.0 * sr / hop)

best = None
for start in range(0, max(1, len(rms) - frames_per_window), step):
    w_rms = rms[start:start + frames_per_window]
    w_f0 = f0[start:start + frames_per_window]
    w_zcr = voiced_prob[start:start + frames_per_window]
    if len(w_rms) < frames_per_window:
        break
    voiced = w_rms > speech_gate
    voiced_frac = float(np.mean(voiced))
    if voiced_frac < 0.35:
        continue
    snr = 20 * np.log10(np.percentile(w_rms, 90) / max(floor, 1e-9))
    # low zero-crossing on loud frames means voiced speech rather than hiss
    tonal = float(np.mean(w_zcr[voiced])) if voiced.any() else 1.0
    pitches = w_f0[voiced & np.isfinite(w_f0)]
    steady = float(np.std(pitches)) if len(pitches) > 10 else 999.0
    med_f0 = float(np.median(pitches)) if len(pitches) > 10 else float("nan")
    score = voiced_frac * 2.0 + snr / 20.0 - tonal * 2.0 - min(steady, 120) / 200.0
    if best is None or score > best[0]:
        best = (score, start * hop / sr, voiced_frac, snr, med_f0, tonal)

if best is None:
    print(f"{path}: no window with enough speech at {want}s")
    sys.exit(1)

score, t, vf, snr, f0m, tonal = best
print(f"{path}")
print(f"  best {want:.0f}s window starts at {t:.1f}s into the trimmed file")
print(f"  voiced {vf*100:.0f}%   snr {snr:.1f}dB   median pitch {f0m:.0f}Hz   zcr {tonal:.3f}")
print(f"  START={t:.2f}")
