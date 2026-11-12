# Builds both edit timelines from the beat grids, then cuts/crops every clip to the frames it needs.
# usage: py -I edits.py [--extract]
import json, os, sys, subprocess, hashlib, shutil
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = r'C:\Users\DeeDee\Videos\Paperbunkr Clips'
FPS = 30

# crop to the app area: 1366x720 recordings lose the Windows title bar, 1092x614 lose the letterbox
CROP = {1366: 'crop=1366:698:0:22', 1092: 'crop=1090:554:0:18'}
WIDTH = {'A1 - take 2.mp4': 1366, 'A1 - take 1.mp4': 1366, 'B1 - take 3.mp4': 1366, 'C1.mp4': 1366, 'C3 - take 2.mp4': 1366,
         'D1.mp4': 1092, 'D2.mp4': 1092, 'E1.mp4': 1092, 'E2 - take 2.mp4': 1092, 'EPub.mp4': 1092, 'F1.mp4': 1092,
         'Manga reading.mp4': 1092, 'Quick Open.mp4': 1092, 'Wanted.mp4': 1092}
STILL = {  # screenshots (1366x720 with title bar)
    'smart': r'Extra\Screenshot 2026-11-01 084825.png',
    'earth616': r'Extra\Screenshot 2026-11-01 085052.png',
    'insightsComp': r'Extra\Screenshot 2026-11-01 084703.png',
    'th1': r'F2 Screenshots\Screenshot 2026-11-01 082338.png',
    'th2': r'F2 Screenshots\Screenshot 2026-11-01 082424.png',
    'th3': r'F2 Screenshots\Screenshot 2026-11-01 082500.png',
    'th4': r'F2 Screenshots\Screenshot 2026-11-01 082534.png',
    'th5': r'F2 Screenshots\Screenshot 2026-11-01 082650.png',
    'th6': r'F2 Screenshots\Screenshot 2026-11-01 082808.png',
    'th7': r'F2 Screenshots\Screenshot 2026-11-01 082828.png',
}
# extra theme frames grabbed from clips (Home in the purple and gold themes)
GRAB = {'th8': ('B1 - take 3.mp4', 0.4), 'th9': ('Quick Open.mp4', 14.0)}

def grid(name, start_bar):
    g = json.load(open(os.path.join(HERE, 'tracks', name + '_beats.json')))
    beats, down, bars = g['beats'], g['down'], g['bars']
    t0 = bars[start_bar]
    B = lambda k: bars[k] - t0
    Bt = lambda k, beat=0: beats[down + 4 * k + beat] - t0   # beat within bar k
    return B, Bt, t0

def clip(src, ss, speed, t0, t1, **kw): return dict(kind='clip', src=src, ss=ss, speed=speed, t0=t0, t1=t1, **kw)
def still(key, t0, t1, **kw): return dict(kind='still', key=key, t0=t0, t1=t1, **kw)

# ------------------------------------------------------------------ EDIT A: GOZALO (Slowed), hype
def edit_a():
    B, Bt, off = grid('t4', 4)
    S, T = [], []
    S.append(clip('A1 - take 2.mp4', 2, 3.0, 0, B(10), style='wall'))
    T.append(dict(type='hook', text='Your comics.', t0=B(5), t1=B(8)))
    T.append(dict(type='hook2', text='Your machine.', t0=B(6), t1=B(8)))
    T.append(dict(type='logo', t0=B(8), t1=B(10), kick=Bt(8, 1), tag=Bt(8, 2)))
    m1 = [  # (shot, kicker, label)
        (clip('B1 - take 3.mp4', 0.3, 1.0, 0, 0), 'Home', 'Continue reading'),
        (clip('C1.mp4', 1.2, 1.4, 0, 0), 'Read', 'Open any issue'),
        (clip('C1.mp4', 6.0, 5.0, 0, 0), 'Read', 'Comics'),
        (clip('C3 - take 2.mp4', 4.0, 2.2, 0, 0), 'Read', 'Guided view'),
        (clip('Manga reading.mp4', 2.8, 3.5, 0, 0), 'Read', 'Manga, right to left'),
        (clip('EPub.mp4', 52.0, 6.0, 0, 0), 'Read', 'Books too'),
        (clip('D1.mp4', 7.6, 3.0, 0, 0), 'Organise', 'Every series'),
        (clip('D1.mp4', 16.0, 7.0, 0, 0), 'Organise', 'Every issue'),
        (clip('D2.mp4', 9.2, 2.2, 0, 0), 'Organise', 'Reading lists'),
    ]
    for i, (s, k, l) in enumerate(m1):
        s.update(t0=B(10 + i), t1=B(11 + i), style='window', punch=1, flash=0.7 if i == 0 else 0.22)
        S.append(s); T.append(dict(type='label', kick=k, text=l, t0=s['t0'], t1=s['t1']))
    # break + themes
    T.append(dict(type='center', text='Make it', t0=B(19), t1=B(20), at=B(19)))
    T.append(dict(type='center2', text='yours.', t0=Bt(19, 2), t1=B(20), at=Bt(19, 2)))
    flips = [Bt(20, 0), Bt(20, 2), Bt(21, 0), Bt(21, 2), Bt(22, 0), Bt(22, 1), Bt(22, 2), Bt(22, 3)]
    for b in range(4):
        flips += [Bt(23, b), (Bt(23, b) + (Bt(23, b + 1) if b < 3 else B(24))) / 2]
    themes = ['th8', 'th3', 'th1', 'th6', 'th4', 'th2', 'th7', 'th5', 'th9']
    for i, ft in enumerate(flips):
        t1 = flips[i + 1] if i + 1 < len(flips) else B(24)
        S.append(still(themes[i % len(themes)], ft, t1, style='window', punch=1, flash=0.12, kb=0))
    T.append(dict(type='label', kick='Themes', text='Make it yours', t0=B(20), t1=B(24)))
    m2 = [
        (clip('E1.mp4', 0.0, 1.0, 0, 0), 'Discover', 'Insights'),
        (clip('E1.mp4', 14.0, 5.0, 0, 0), 'Discover', 'Reading trends'),
        (clip('E2 - take 2.mp4', 2.0, 9.0, 0, 0), 'Discover', 'Continuity maps'),
        (still('earth616', 0, 0), 'Discover', 'Every era, in order'),
        (clip('Wanted.mp4', 0.0, 2.5, 0, 0), 'Discover', 'New this week'),
        (clip('Quick Open.mp4', 3.3, 5.0, 0, 0), 'Jump anywhere', 'Ctrl + P'),
        (still('smart', 0, 0), 'Organise', 'Smart lists'),
        (clip('EPub.mp4', 0.0, 2.5, 0, 0), 'Books', 'EPUB · PDF · MOBI'),
        (clip('F1.mp4', 2.0, 20.0, 0, 0), 'Themes', 'Dark or light'),
    ]
    for i, (s, k, l) in enumerate(m2):
        s.update(t0=B(24 + i), t1=B(25 + i), style='window', punch=1, flash=0.7 if i == 0 else 0.22)
        if s['kind'] == 'still': s['kb'] = 1
        S.append(s); T.append(dict(type='label', kick=k, text=l, t0=s['t0'], t1=s['t1']))
    T.append(dict(type='center', text='Free.', t0=B(33), t1=B(34), at=B(33)))
    T.append(dict(type='center2', text='Open source.', t0=Bt(33, 2), t1=B(34), at=Bt(33, 2)))
    end = B(39)
    T.append(dict(type='outro', t0=B(34), t1=end, facts=[Bt(35, 0), Bt(35, 1), Bt(35, 2), Bt(35, 3)], cta=B(37)))
    return dict(name='A', track='t4', offset=off, dur=end, fade=1.2, shots=S, texts=T, look='hype')

# ------------------------------------------------------------------ EDIT B: How Far, polished
def edit_b():
    B, Bt, off = grid('t2', 4)
    S, T = [], []
    S.append(clip('A1 - take 2.mp4', 6, 1.6, 0, B(8), style='wall'))
    T.append(dict(type='hook', text='Your comics.', t0=Bt(4, 1), t1=B(6)))
    T.append(dict(type='hook2', text='Your machine.', t0=B(5), t1=B(6)))
    T.append(dict(type='logo', t0=B(6), t1=B(8), kick=Bt(6, 1), tag=Bt(6, 2)))
    sections = [
        ('01 · Read', 'Built for long sessions', 8, [
            (clip('B1 - take 3.mp4', 0.3, 1.0, 0, 0), 'Pick up where you left off.'),
            (clip('C1.mp4', 1.0, 1.1, 0, 0), 'Open an issue.'),
            (clip('C1.mp4', 6.0, 3.2, 0, 0), 'The backdrop takes its colour from the page.'),
            (clip('C3 - take 2.mp4', 4.0, 2.0, 0, 0), 'Guided view, panel by panel.'),
            (clip('Manga reading.mp4', 2.8, 3.0, 0, 0), 'Manga, right to left.'),
            (clip('EPub.mp4', 52.0, 6.0, 0, 0), 'Books too, with page themes.'),
        ]),
        ('02 · Organise', 'A library that stays tidy', 14, [
            (clip('D1.mp4', 7.6, 2.6, 0, 0), 'Every series has a home.'),
            (clip('D1.mp4', 16.0, 6.0, 0, 0), 'Every issue at a glance.'),
            (clip('D2.mp4', 9.2, 2.0, 0, 0), 'Reading lists you can follow.'),
            (clip('D2.mp4', 14.0, 6.0, 0, 0), 'Reading lists you can follow.'),
            (still('smart', 0, 0, kb=1), 'Smart lists that fill themselves.'),
        ]),
        ('03 · Discover', 'Know what to read next', 19, [
            (clip('E1.mp4', 0.0, 1.0, 0, 0), 'Insights on how you read.'),
            (clip('E1.mp4', 14.0, 5.0, 0, 0), 'Insights on how you read.'),
            (clip('E2 - take 2.mp4', 2.0, 8.0, 0, 0), 'Continuity maps, in publication order.'),
            (clip('Wanted.mp4', 0.0, 2.1, 0, 0), "This week's new releases."),
            (clip('Quick Open.mp4', 3.3, 4.6, 0, 0), 'Jump anywhere with Ctrl+P.'),
        ]),
    ]
    for kick, title, bar0, shots in sections:
        for i, (s, line) in enumerate(shots):
            s.update(t0=B(bar0 + i), t1=B(bar0 + i + 1), style='window')
            S.append(s)
            T.append(dict(type='line', text=line, t0=s['t0'], t1=s['t1']))
        T.append(dict(type='caption', kick=kick, text=title, t0=B(bar0), t1=B(bar0 + len(shots))))
    # themes: Preferences clip, then Home in every theme, wiping every two beats
    S.append(clip('F1.mp4', 8.0, 4.0, B(24), B(25), style='window'))
    T.append(dict(type='line', text='Pick a theme in Preferences.', t0=B(24), t1=B(25)))
    themes = ['th8', 'th3', 'th1', 'th6', 'th4', 'th2', 'th7', 'th9']
    for i in range(8):
        bar, beat = 25 + i // 2, (i % 2) * 2
        t0 = Bt(bar, beat); t1 = Bt(bar, beat + 2) if beat == 0 else B(bar + 1)
        S.append(still(themes[i], t0, t1, style='window', wipe=1, kb=0))
    T.append(dict(type='line', text='Every screen follows.', t0=B(25), t1=B(29)))
    T.append(dict(type='caption', kick='04 · Themes', text='Make it yours', t0=B(24), t1=B(29)))
    end = B(32)
    T.append(dict(type='outro', t0=B(29), t1=end, facts=[Bt(29, 2), Bt(29, 3), Bt(30, 0), Bt(30, 1)], cta=Bt(30, 2)))
    return dict(name='B', track='t2', offset=off, dur=end, fade=1.6, shots=S, texts=T, look='polished')

# ------------------------------------------------------------------ extraction
def frames_dir(s):
    key = f"{s['src']}|{s['ss']:.3f}|{s['speed']:.3f}|{s['t1'] - s['t0']:.3f}"
    return 'f_' + hashlib.md5(key.encode()).hexdigest()[:10]

def extract(edits):
    out = os.path.join(HERE, 'frames'); os.makedirs(out, exist_ok=True)
    stills = os.path.join(HERE, 'stills_src'); os.makedirs(stills, exist_ok=True)
    for k, rel in STILL.items():
        dst = os.path.join(stills, k + '.jpg')
        if not os.path.exists(dst):
            subprocess.run(['magick', os.path.join(SRC, rel), '-crop', '1366x698+0+22', '+repage', '-quality', '93', dst], check=True)
    for k, (src, t) in GRAB.items():
        dst = os.path.join(stills, k + '.jpg')
        if not os.path.exists(dst):
            subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-ss', str(t), '-i', os.path.join(SRC, src), '-frames:v', '1',
                            '-vf', CROP[WIDTH[src]], '-q:v', '2', dst], check=True)
    for e in edits:
        for s in e['shots']:
            if s['kind'] != 'clip': continue
            d = frames_dir(s); s['dir'] = 'frames/' + d
            path = os.path.join(out, d)
            dur = s['t1'] - s['t0'] + 0.4
            if os.path.isdir(path) and os.listdir(path):
                s['n'] = len(os.listdir(path)); continue
            os.makedirs(path)
            vf = CROP[WIDTH[s['src']]]   # every source frame; the compositor maps time x speed to a frame
            subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-ss', f"{s['ss']:.3f}", '-i', os.path.join(SRC, s['src']),
                            '-t', f"{dur * s['speed']:.3f}", '-vf', vf, '-q:v', '3', os.path.join(path, '%04d.jpg')], check=True)
            s['n'] = len(os.listdir(path))
            print(f"  {e['name']} {s['src']:<22} ss={s['ss']:<5} x{s['speed']:<4} -> {s['n']} frames")

# vertical framing: horizontal focus (0-1 across the frame) and pan range for each shot
FOCUS = {
    ('B1 - take 3.mp4', 0.3): (.46, .06), ('C1.mp4', 1.2): (.42, .10), ('C1.mp4', 1.0): (.42, .10),
    ('C1.mp4', 6.0): (.50, .02), ('C3 - take 2.mp4', 4.0): (.50, .30), ('Manga reading.mp4', 2.8): (.47, .02),
    ('EPub.mp4', 52.0): (.50, .02), ('EPub.mp4', 0.0): (.34, .10), ('D1.mp4', 7.6): (.40, .12), ('D1.mp4', 16.0): (.42, .16),
    ('D2.mp4', 9.2): (.38, .10), ('D2.mp4', 14.0): (.36, .06), ('E1.mp4', 0.0): (.36, .10), ('E1.mp4', 14.0): (.30, .10),
    ('E2 - take 2.mp4', 2.0): (.58, .14), ('Wanted.mp4', 0.0): (.45, .14), ('Quick Open.mp4', 3.3): (.50, .03),
    ('F1.mp4', 2.0): (.46, .08), ('F1.mp4', 8.0): (.46, .08),
    'earth616': (.62, .12), 'smart': (.50, .14),
}
def apply_focus(e):
    for s in e['shots']:
        k = s['key'] if s['kind'] == 'still' else (s['src'], s['ss'])
        fx, pan = FOCUS.get(k, (.5, .0 if s['kind'] == 'still' else .06))
        if isinstance(k, str) and k.startswith('th'): fx, pan = .52, .0   # themed Home: spotlight
        s['fx'], s['pan'] = fx, pan

if __name__ == '__main__':
    edits = [edit_a(), edit_b()]
    for e in edits: apply_focus(e)
    extract(edits)   # skips anything already cut; always fills in each clip's frame folder
    for e in edits:
        with open(os.path.join(HERE, f"timeline_{e['name']}.js"), 'w') as f:
            f.write('window.TL = ' + json.dumps(e, indent=1) + ';\n')
        print(f"edit {e['name']}: {e['dur']:.2f}s, {len(e['shots'])} shots, track {e['track']} from {e['offset']:.3f}s")
