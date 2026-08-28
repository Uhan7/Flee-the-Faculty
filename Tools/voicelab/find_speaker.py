"""Find the longest window held by ONE speaker, inside a pitch band.

The first version of this picker scored loudness and voicing only, and handed
back 25 seconds of a two-person conversation. A clone built from that averages
the speakers. Pitch consistency is the missing term: a window whose per-second
median jumps between bands has more than one person in it.
"""
import sys, warnings
import numpy as np, librosa
warnings.filterwarnings("ignore")

path, lo_hz, hi_hz, want = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), float(sys.argv[4])
y, sr = librosa.load(path, sr=24000, mono=True)
hop = 512
f0 = librosa.yin(y, fmin=70, fmax=400, sr=sr, frame_length=2048, hop_length=hop)
rms = librosa.feature.rms(y=y, frame_length=2048, hop_length=hop)[0]
n = min(len(f0), len(rms)); f0, rms = f0[:n], rms[:n]

gate = np.percentile(rms, 55)
per_sec = int(sr / hop)
secs = n // per_sec
# one median per second, over loud frames only
sec_f0 = []
for i in range(secs):
    sl = slice(i * per_sec, (i + 1) * per_sec)
    loud = rms[sl] > gate
    vals = f0[sl][loud & np.isfinite(f0[sl])]
    sec_f0.append(np.median(vals) if len(vals) >= 3 else np.nan)
sec_f0 = np.array(sec_f0)

in_band = (sec_f0 >= lo_hz) & (sec_f0 <= hi_hz)
want_secs = int(want)

best = None
for start in range(0, max(1, secs - want_secs)):
    w = slice(start, start + want_secs)
    band = in_band[w]
    vals = sec_f0[w][np.isfinite(sec_f0[w])]
    if len(vals) < want_secs * 0.6:
        continue
    frac = float(np.mean(band))
    spread = float(np.std(vals))
    loudness = float(np.mean(rms[start * per_sec : (start + want_secs) * per_sec] > gate))
    score = frac * 3.0 + loudness - spread / 40.0
    if best is None or score > best[0]:
        best = (score, start, frac, spread, float(np.median(vals)), loudness)

if best is None:
    print("no window found"); sys.exit(1)
score, start, frac, spread, med, loud = best
print(f"{path}")
print(f"  best {want:.0f}s window at {start}s: {frac*100:.0f}% of seconds in {lo_hz:.0f}-{hi_hz:.0f}Hz")
print(f"  median {med:.0f}Hz, per-second spread {spread:.0f}Hz, voiced {loud*100:.0f}%")
print(f"  per-second: " + " ".join("--" if not np.isfinite(v) else f"{v:.0f}" for v in sec_f0[start:start+want_secs]))
print(f"  START={start}")
