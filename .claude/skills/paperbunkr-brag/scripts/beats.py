# Dynamic-programming beat tracker (Ellis 2007) + downbeat phase + section onsets.
# usage: py -I beats.py t4 139.5   -> tracks/t4_beats.json
import numpy as np, wave, sys, os, json
HERE = os.path.dirname(os.path.abspath(__file__))
SR, HOP = 22050, 256
name, bpm0 = sys.argv[1], float(sys.argv[2])

with wave.open(os.path.join(HERE, 'tracks', name + '.wav')) as w:
    x = np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768
n = 2048; win = np.hanning(n).astype(np.float32)
frames = 1 + (len(x) - n) // HOP
idx = np.arange(n)[None, :] + HOP * np.arange(frames)[:, None]
S = np.abs(np.fft.rfft(x[idx] * win, axis=1))
freqs = np.fft.rfftfreq(n, 1 / SR)
logS = np.log1p(S * 10)
flux = np.concatenate([[0], np.maximum(0, np.diff(logS, axis=0)).sum(1)])
flux = flux - np.convolve(flux, np.ones(32) / 32, 'same'); flux = np.maximum(flux, 0); flux /= flux.std() + 1e-9
low = S[:, freqs < 120].sum(1)
fps = SR / HOP
period = 60 / bpm0 * fps
# DP
alpha = 400.0
score = flux.copy(); back = np.full(len(flux), -1)
lo, hi = int(period * 0.8), int(period * 1.25)
for t in range(hi, len(flux)):
    prev = np.arange(t - hi, t - lo)
    pen = -alpha * np.log((t - prev) / period) ** 2
    c = score[prev] + pen
    k = int(np.argmax(c)); score[t] = flux[t] + c[k]; back[t] = prev[k]
t = int(np.argmax(score[-int(period * 2):])) + len(score) - int(period * 2)
beats = []
while t >= 0:
    beats.append(t); t = back[t]
beats = np.array(beats[::-1]) / fps
# downbeat: phase (0-3) whose beats carry the most low-band onset energy
lowflux = np.concatenate([[0], np.maximum(0, np.diff(np.log1p(low)))])
bi = np.clip((beats * fps).astype(int), 0, len(lowflux) - 1)
ph = [lowflux[bi[p::4]].mean() for p in range(4)]
down = int(np.argmax(ph))
# loudness per beat (low band) to locate drops/breaks
lb = np.array([low[bi[i]:bi[i + 1]].mean() if i + 1 < len(bi) else low[bi[i]:].mean() for i in range(len(bi))])
lb = lb / lb.max()
ibi = np.diff(beats)
print(f"{name}: {len(beats)} beats, mean bpm {60/ibi.mean():.2f} (sd {ibi.std()*1000:.1f} ms), first beat {beats[0]:.3f}, downbeat phase {down}")
# print bars with low-band level, to spot drops
bars = beats[down::4]
for k, b in enumerate(bars):
    i = down + 4 * k
    lv = lb[i:i + 4].mean()
    if k % 1 == 0: print(f"bar {k:3d} @ {b:7.3f}s  low {'#' * int(lv * 30)}")
json.dump({'beats': beats.tolist(), 'down': down, 'bars': bars.tolist(), 'lowPerBeat': lb.tolist()},
          open(os.path.join(HERE, 'tracks', name + '_beats.json'), 'w'))
