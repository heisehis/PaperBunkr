# Harvest sharp, unique cover crops from the Library poster-view scrolls (1366-wide recordings).
import subprocess, os, numpy as np, hashlib
SRC = r'C:\Users\DeeDee\Videos\Paperbunkr Clips'
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'covers'); os.makedirs(OUT, exist_ok=True)
COLS = [329, 499, 669, 839, 1009, 1179]; CW, CH = 140, 212
W, H = 1366, 698

def frames(src, step):
    p = subprocess.run(['ffmpeg', '-loglevel', 'error', '-i', os.path.join(SRC, src), '-vf', f'fps=1/{step},crop={W}:{H}:0:22',
                        '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], capture_output=True, check=True).stdout
    n = len(p) // (W * H * 3)
    return np.frombuffer(p, np.uint8)[: n * W * H * 3].reshape(n, H, W, 3)

seen = []
def dhash(img):
    g = img.mean(2)
    small = g.reshape(8, g.shape[0] // 8, 9, -1)[:, :, :, :].mean((1, 3)) if False else None
    g = g[20:-20, 10:-10]; ys = np.linspace(0, g.shape[0] - 1, 16).astype(int); xs = np.linspace(0, g.shape[1] - 1, 17).astype(int)
    s = g[np.ix_(ys, xs)]
    return (s[:, 1:] > s[:, :-1]).flatten()

count = 0
for src in ('A1 - take 2.mp4', 'A1 - take 1.mp4'):
    for f in frames(src, 0.7):
        g = f[:, 335:463].astype(float).mean((1, 2))           # brightness profile down column 1
        # a cover top: a dark gap above, a long bright run below
        bright = g > 38
        y = 100
        while y < H - CH:
            if bright[y] and not bright[y - 3] and bright[y:y + CH - 6].mean() > .9:
                for x in COLS:
                    c = f[y:y + CH, x:x + CW]
                    if c.mean() < 30 or c.std() < 25: continue
                    h = dhash(c.astype(float))
                    if any((h != s).sum() < 70 for s in seen): continue
                    seen.append(h)
                    # trim the 4px border and the corner badge row is part of the art; keep as is
                    subprocess.run(['magick', '-size', f'{CW}x{CH}', '-depth', '8', 'rgb:-', '-shave', '3x3', '-quality', '94',
                                    os.path.join(OUT, f'cv{count:03d}.jpg')], input=np.ascontiguousarray(c).tobytes(), check=True)
                    count += 1
                y += CH
            y += 1
print('covers', count)
