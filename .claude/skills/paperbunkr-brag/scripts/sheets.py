# Contact sheets: one frame every STEP seconds per clip, labelled with the timestamp.
import subprocess, os, sys, glob, shutil
SRC = r'C:\Users\DeeDee\Videos\Paperbunkr Clips'
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'sheets')
STEP = float(sys.argv[1]) if len(sys.argv) > 1 else 2.0
only = sys.argv[2:]
clips = sorted(f for f in os.listdir(SRC) if f.lower().endswith('.mp4'))
for c in clips:
    if only and not any(o.lower() in c.lower() for o in only): continue
    tag = c[:-4].replace(' ', '_')
    tmp = os.path.join(OUT, 'tmp_' + tag); os.makedirs(tmp, exist_ok=True)
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', os.path.join(SRC, c), '-vf', f'fps=1/{STEP},scale=420:-2', os.path.join(tmp, 'f_%04d.jpg')], check=True)
    files = sorted(glob.glob(os.path.join(tmp, 'f_*.jpg')))
    args = ['magick', 'montage']
    for i, f in enumerate(files):
        args += ['-label', f'{(i) * STEP + STEP/2:.0f}s' if False else f'{i * STEP:.0f}s', f]
    args += ['-tile', '5x', '-geometry', '+3+3', '-pointsize', '18', '-background', '#222', '-fill', 'white', os.path.join(OUT, f'{tag}.jpg')]
    subprocess.run(args, check=True)
    shutil.rmtree(tmp)
    print('sheet', tag, len(files))
