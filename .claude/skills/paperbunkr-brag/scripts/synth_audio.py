# Soundtrack for the Paperbunkr brag: 120 BPM, A minor, 20.0 s, one beat grid from frame 0.
import numpy as np, wave, os

SR = 44100
DUR = 20.0
N = int(SR * DUR)
BEAT = 0.5
rng = np.random.default_rng(7)

def midi(m): return 440.0 * 2 ** ((m - 69) / 12)
def tvec(d): return np.arange(int(SR * d)) / SR

L = np.zeros(N); R = np.zeros(N)
send = np.zeros(N)          # mono reverb send

def place(sig, t0, gain=1.0, pan=0.0, rev=0.0):
    i = int(round(t0 * SR))
    if i >= N: return
    sig = sig[: N - i] * gain
    gl = np.cos((pan + 1) * np.pi / 4); gr = np.sin((pan + 1) * np.pi / 4)
    L[i:i + len(sig)] += sig * gl * 1.414 / 2 * 1.414
    R[i:i + len(sig)] += sig * gr * 1.414 / 2 * 1.414
    send[i:i + len(sig)] += sig * rev

def lowpass(x, fc):
    a = np.exp(-2 * np.pi * fc / SR); y = np.empty_like(x); s = 0.0
    for n in range(len(x)):
        s = (1 - a) * x[n] + a * s; y[n] = s
    return y

def onepole_lp_fast(x, fc):
    # vectorised-enough one-pole via scipy-free recursion on chunks
    from itertools import accumulate
    a = np.exp(-2 * np.pi * fc / SR)
    out = np.empty_like(x); s = 0.0
    for n, v in enumerate(x):
        s = v + a * (s - v); out[n] = s
    return out

# ---------- instruments ----------
def kick(gain=1.0):
    t = tvec(0.45)
    f = 45 + 95 * np.exp(-t * 28)
    ph = 2 * np.pi * np.cumsum(f) / SR
    env = np.exp(-t * 7.5)
    click = np.exp(-t * 400) * 0.25
    return (np.sin(ph) * env + click * rng.standard_normal(len(t)) * 0.3) * gain

def hat():
    t = tvec(0.07)
    n = rng.standard_normal(len(t))
    n = n - np.concatenate([[0], n[:-1]]) * 0.95          # crude highpass
    return n * np.exp(-t * 60) * 0.18

def pluck(m, d=0.35, bright=1.0):
    t = tvec(d); f = midi(m)
    s = (np.sin(2 * np.pi * f * t) + 0.35 * bright * np.sin(4 * np.pi * f * t) + 0.12 * bright * np.sin(6 * np.pi * f * t))
    return s * np.exp(-t * 9) * np.minimum(1, t * 400)

def bass(m, d=0.24):
    t = tvec(d); f = midi(m)
    s = np.sin(2 * np.pi * f * t) + 0.25 * np.sin(4 * np.pi * f * t)
    env = np.minimum(1, t * 200) * np.exp(-t * 3) * np.minimum(1, (d - t) * 60)
    return s * env

def pad_chord(notes, d, attack=0.6):
    t = tvec(d); s = np.zeros(len(t))
    for m in notes:
        for det in (-0.08, 0.0, 0.07):
            f = midi(m + det)
            # soft saw: a few harmonics
            for k, a in ((1, 1), (2, .4), (3, .18), (4, .08)):
                s += a * np.sin(2 * np.pi * f * k * t + rng.uniform(0, 6.28))
    env = np.minimum(1, t / attack) * np.minimum(1, (d - t) / 0.35)
    return s * env / (len(notes) * 3)

def whoosh(d, gain=1.0):
    # filtered noise that rises and lands on the beat (ends at t0 + d)
    t = tvec(d)
    n = rng.standard_normal(len(t))
    k = t / d
    env = (k ** 2.2) * np.minimum(1, (d - t) * 40)
    # sweep a one-pole lowpass upward
    out = np.empty_like(n); s = 0.0
    fc = 300 + 5200 * k ** 2
    a = np.exp(-2 * np.pi * fc / SR)
    for i in range(len(n)):
        s = n[i] + a[i] * (s - n[i]); out[i] = s
    return out * env * 0.5 * gain

def boom(m=33, gain=1.0, d=1.6):
    t = tvec(d); f = midi(m)
    s = np.sin(2 * np.pi * f * t) * np.exp(-t * 2.2) + 0.5 * np.sin(2 * np.pi * f * 2 * t) * np.exp(-t * 4)
    n = rng.standard_normal(len(t)) * np.exp(-t * 18) * 0.25
    return (s + n) * gain

def click():
    t = tvec(0.04)
    return (np.sin(2 * np.pi * 2400 * t) * 0.5 + rng.standard_normal(len(t)) * .3) * np.exp(-t * 180)

# ---------- harmony ----------
CH = {  # pad voicings and bass roots
    'Am': ([57, 60, 64, 69], 45), 'F': ([57, 60, 65, 69], 41),
    'C': ([55, 60, 64, 67], 48), 'G': ([55, 59, 62, 67], 43),
}
prog = ['Am', 'F', 'C', 'G', 'Am', 'F', 'C', 'G', 'Am', 'Am']
ARP = {'Am': [69, 72, 76, 72], 'F': [69, 72, 77, 72], 'C': [67, 72, 76, 72], 'G': [67, 71, 74, 71]}

# pad: every bar, the last one rings to the end
for b, name in enumerate(prog):
    notes, _ = CH[name]
    d = 2.0 if b < 9 else 2.0
    g = 0.16 if b > 0 else 0.2
    place(pad_chord(notes, d + 0.3, attack=0.9 if b == 0 else 0.25), b * 2.0, g, 0, rev=0.25)

# kick: every beat from 2.0 up to 17.5, then the final hit at 18.0
for i in range(int(2.0 / BEAT), int(18.0 / BEAT)):
    place(kick(), i * BEAT, 0.55)
# hats on the off-beats, 6.0 - 17.75
for i in range(int(6.0 / BEAT), int(18.0 / BEAT)):
    place(hat(), i * BEAT + 0.25, 0.9, pan=0.25)
# bass in 8ths from 3.0 to 18.0
for i in range(int(3.0 / 0.25), int(18.0 / 0.25)):
    t0 = i * 0.25; _, root = CH[prog[int(t0 // 2)]]
    m = root + (12 if i % 4 == 3 else 0)
    place(bass(m), t0, 0.32)
# arp in 8ths from 3.0 to 18.0, quiet, a little left, with reverb
for i in range(int(3.0 / 0.25), int(18.0 / 0.25)):
    t0 = i * 0.25; seq = ARP[prog[int(t0 // 2)]]
    place(pluck(seq[i % 4], 0.3, 0.6), t0, 0.09, pan=-0.3, rev=0.35)

# ---------- sound effects (all on the grid, pitched to A minor) ----------
place(boom(33, 0.9), 0.0, 0.55, rev=0.3)                          # opening hit
for k, m in enumerate([57, 60, 64, 69]):                           # rows slam in
    place(kick(0.6), k * 0.25, 0.35); place(pluck(m, 0.25), k * 0.25, 0.10, pan=(-.4 + .27 * k), rev=.3)
place(pluck(69, 0.6, 1.2), 1.0, 0.14, rev=0.5)                      # "Your comics."
place(kick(0.8), 1.0, 0.45)
place(whoosh(1.0, 1.0), 1.0, 0.3, rev=0.3)                        # build into 2.0
for k in range(4): place(hat(), 1.0 + k * .25, 0.6 + .2 * k, pan=.2)
place(boom(33, 1.0, 2.0), 2.0, 0.75, rev=0.35)                      # "Your machine."
for t1, d, g in [(3.0, .8, 1.0), (4.5, .6, .8), (6.0, .6, .9), (9.5, .6, .9), (11.0, .4, .6), (13.0, .5, .7), (16.0, .7, .9)]:
    place(whoosh(d, g), t1 - d, 0.22, pan=0, rev=0.3)
for t1 in (13.25, 13.5):                                            # extra cards
    place(whoosh(.35, .6), t1 - .35, 0.16, pan=(.3 if t1 > 13.3 else -.3), rev=.3)
place(pluck(81, 0.5), 3.0, 0.08, rev=0.6)                           # logo sparkle
for t1, m in [(7.0, 76), (8.0, 79), (9.0, 76)]:                     # guided-view steps
    place(pluck(m, 0.4, 1.0), t1, 0.11, pan=.2, rev=.45)
place(click(), 10.5, 0.30, pan=-.2)                                 # cursor click
for i in range(24):                                                 # covers pop in, quiet ticks
    t1 = 11.0 + i * .033
    if i % 3 == 0: place(pluck(84 + (i // 3) % 3 * 3, 0.12, .4), t1, 0.025, pan=(-.5 + i / 24), rev=.2)
for k, m in enumerate([69, 72, 76, 81]):                            # outro facts
    place(pluck(m, 0.45, 1.0), 16.5 + k * .25, 0.11, pan=(-.3 + .2 * k), rev=.45)
place(boom(33, 1.0, 2.0), 18.0, 0.7, rev=0.45)                      # final hit
place(kick(), 18.0, 0.55)

# ---------- reverb (decaying noise IR), mix, master ----------
ir_t = tvec(1.4)
irL = rng.standard_normal(len(ir_t)) * np.exp(-ir_t * 4.2)
irR = rng.standard_normal(len(ir_t)) * np.exp(-ir_t * 4.2)
def conv(x, ir):
    n = len(x) + len(ir); f = 1 << (n - 1).bit_length()
    return np.fft.irfft(np.fft.rfft(x, f) * np.fft.rfft(ir, f), f)[:len(x)]
wetL = conv(send, irL); wetR = conv(send, irR)
wet_gain = 0.18 / (np.sqrt(np.sum(irL ** 2)))
L += wetL * wet_gain; R += wetR * wet_gain

# fade the tail into the end
fade = np.ones(N); fl = int(SR * 1.2); fade[-fl:] = np.linspace(1, 0, fl) ** 1.5
L *= fade; R *= fade
mix = np.stack([L, R], 1)
mix /= np.max(np.abs(mix)) + 1e-9
mix = np.tanh(mix * 1.4) / np.tanh(1.4) * 0.89            # gentle saturation, about -1 dBFS peak
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'music.wav')
with wave.open(out, 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((mix * 32767).astype('<i2').tobytes())
print('wrote', out)
