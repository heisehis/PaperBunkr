# Tempo, beat phase, energy and brightness per track. Writes a summary and a plot per track.
import numpy as np, wave, sys, os, json
HERE = os.path.dirname(os.path.abspath(__file__))
SR = 22050
HOP = 512

def load(p):
    with wave.open(p) as w:
        x = np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(np.float32) / 32768
    return x

def stft_mag(x, n=2048, hop=HOP):
    win = np.hanning(n).astype(np.float32)
    frames = 1 + (len(x) - n) // hop
    idx = np.arange(n)[None, :] + hop * np.arange(frames)[:, None]
    return np.abs(np.fft.rfft(x[idx] * win, axis=1))

def analyze(p):
    x = load(p)
    S = stft_mag(x)
    logS = np.log1p(S * 10)
    flux = np.maximum(0, np.diff(logS, axis=0)).sum(1)
    flux = np.concatenate([[0], flux])
    flux = flux - np.convolve(flux, np.ones(16) / 16, 'same')
    flux = np.maximum(flux, 0)
    fps = SR / HOP
    # tempo via autocorrelation over 70-180 BPM, prefer 85-150
    ac = np.correlate(flux, flux, 'full')[len(flux) - 1:]
    lags = np.arange(len(ac))
    bpms = 60 * fps / np.maximum(lags, 1)
    mask = (bpms > 70) & (bpms < 180)
    w = np.exp(-0.5 * ((np.log2(bpms / 115)) / 0.9) ** 2)
    score = ac * w * mask
    lag = int(np.argmax(score))
    # refine with parabolic interpolation
    if 1 < lag < len(ac) - 1:
        a, b, c = score[lag - 1], score[lag], score[lag + 1]
        lagf = lag + 0.5 * (a - c) / (a - 2 * b + c + 1e-9)
    else:
        lagf = lag
    bpm = 60 * fps / lagf
    period = lagf / fps
    # beat phase: comb over candidate offsets
    t = np.arange(len(flux)) / fps
    best, best_off = -1, 0
    for off in np.linspace(0, period, 80, endpoint=False):
        beats = np.arange(off, t[-1], period)
        v = flux[np.clip((beats * fps).astype(int), 0, len(flux) - 1)].sum()
        if v > best: best, best_off = v, off
    # energy (RMS) and brightness per second
    sec = SR
    nsec = len(x) // sec
    rms = np.array([np.sqrt(np.mean(x[i * sec:(i + 1) * sec] ** 2)) for i in range(nsec)])
    freqs = np.fft.rfftfreq(2048, 1 / SR)
    cent = (S * freqs).sum(1) / (S.sum(1) + 1e-9)
    cent_s = np.array([cent[int(i * fps):int((i + 1) * fps)].mean() for i in range(nsec)])
    low = S[:, freqs < 150].sum(1); lows = np.array([low[int(i * fps):int((i + 1) * fps)].mean() for i in range(nsec)])
    # best 60 s window by mean RMS
    win = 60
    if nsec > win:
        m = np.convolve(rms, np.ones(win) / win, 'valid'); st = int(np.argmax(m))
    else:
        st = 0
    return dict(dur=len(x) / SR, bpm=bpm, beat_offset=best_off, rms=rms.tolist(), cent=cent_s.tolist(),
                low=(lows / lows.max()).tolist(), best60=st, loud_db=20 * np.log10(rms.mean() + 1e-9))

out = {}
for i in range(1, 6):
    r = analyze(os.path.join(HERE, 'tracks', f't{i}.wav'))
    out[f't{i}'] = r
    print(f"t{i}: dur {r['dur']:.1f}s  bpm {r['bpm']:.2f}  offset {r['beat_offset']:.3f}s  best60 @ {r['best60']}s  mean {r['loud_db']:.1f} dBFS")
    rms = np.array(r['rms']); print('   energy/5s:', ' '.join(f"{v:.2f}" for v in rms.reshape(-1)[: len(rms) // 5 * 5].reshape(-1, 5).mean(1) / rms.max()))
    c = np.array(r['cent']); print('   bright/5s:', ' '.join(f"{v/1000:.1f}" for v in c[: len(c) // 5 * 5].reshape(-1, 5).mean(1)))
json.dump(out, open(os.path.join(HERE, 'tracks', 'analysis.json'), 'w'))
