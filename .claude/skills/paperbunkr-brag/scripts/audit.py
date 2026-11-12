# First / middle / last frame actually shown for every clip shot, per edit.
import json, os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
for name in ('A', 'B'):
    s = open(os.path.join(HERE, f'timeline_{name}.js')).read(); tl = json.loads(s[s.index('{'):s.rindex('}') + 1])
    args = ['magick', 'montage']
    for sh in tl['shots']:
        if sh['kind'] != 'clip' or sh.get('style') == 'wall': continue
        d = sh['t1'] - sh['t0']
        for k, dt in (('in', 0), ('mid', d / 2), ('out', d - .02)):
            idx = min(sh['n'] - 1, int(dt * 30 * sh['speed']))
            args += ['-label', f"{sh['t0']:.1f}s {sh['src'][:10]} {k}", os.path.join(HERE, sh['dir'], f'{idx + 1:04d}.jpg')]
    args += ['-tile', '6x', '-geometry', '300x153+2+2', '-pointsize', '13', '-background', '#222', '-fill', 'white', os.path.join(HERE, 'sheets', f'audit_{name}.jpg')]
    subprocess.run(args, check=True); print('audit', name)
