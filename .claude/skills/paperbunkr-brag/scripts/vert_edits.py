# Vertical (1080x1920) scene plans for both edits, on the same beat grids as the landscape cuts.
# Footage only appears as crops that are naturally tall (a comic page, a list, a palette); everything else is built.
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from edits import grid, clip, still, extract
HERE = os.path.dirname(os.path.abspath(__file__))

# crop rects in source pixels: 1366x698 for the 1366 recordings + stills, 1090x554 for the 1092 ones
R = {
    'home':    (240, 0, 640, 698),
    'open':    (383, 0, 600, 698),
    'pages':   (440, 0, 486, 698),
    'guided':  (283, 0, 800, 698),
    'manga':   (340, 10, 415, 510),
    'epub':    (270, 30, 560, 470),
    'detail':  (55, 0, 545, 554),
    'issues':  (60, 30, 540, 524),
    'rlist':   (50, 0, 560, 554),
    'rlist2':  (140, 0, 480, 554),
    'today':   (60, 100, 540, 370),
    'trends':  (60, 40, 540, 514),
    'contin':  (230, 60, 560, 494),
    'era':     (520, 270, 580, 428),
    'wanted':  (128, 130, 442, 395),
    'qo':      (323, 58, 450, 420),
    'smart':   (580, 390, 420, 308),
    'shelf':   (50, 30, 550, 420),
    'prefs':   (250, 40, 470, 514),
    'theme':   (240, 0, 640, 698),
}

def card(src_shot, crop, t0, t1, **kw):
    s = dict(src_shot); s.update(t0=t0, t1=t1, scene='card', crop=R[crop], style='v'); s.update(kw); return s

def edit_a():
    B, Bt, off = grid('t4', 4)
    S = []
    S.append(dict(scene='wall', kind='none', t0=0, t1=B(10)))
    S.append(dict(scene='words', kind='none', t0=0, t1=B(8), lines=[
        dict(text='Your', at=0), dict(text='comics.', at=0),
        dict(text='Your', at=B(6), color='accent'), dict(text='machine.', at=Bt(6, 2), color='accent')], top=560))
    S.append(dict(scene='logo', kind='none', t0=B(8), t1=B(10), tag=Bt(8, 2)))
    m1 = [
        (clip('B1 - take 3.mp4', 0.3, 1.0, 0, 0), 'home', 'Home', 'Continue reading'),
        (clip('C1.mp4', 1.2, 1.4, 0, 0), 'open', 'Read', 'Open any issue'),
        (clip('C1.mp4', 6.0, 5.0, 0, 0), 'pages', 'Read', 'Comics'),
        (clip('C3 - take 2.mp4', 4.0, 2.2, 0, 0), 'guided', 'Read', 'Guided view'),
        (clip('Manga reading.mp4', 2.8, 3.5, 0, 0), 'manga', 'Read', 'Manga, right to left'),
        (clip('EPub.mp4', 52.0, 6.0, 0, 0), 'epub', 'Read', 'Books too'),
        (None, 'coverwall', 'Organise', 'Every series'),
        (clip('D1.mp4', 16.0, 7.0, 0, 0), 'issues', 'Organise', 'Every issue'),
        (clip('D2.mp4', 9.2, 2.2, 0, 0), 'rlist', 'Organise', 'Reading lists'),
    ]
    for i, (sh, crop, k, l) in enumerate(m1):
        t0, t1 = B(10 + i), B(11 + i)
        if sh is None: S.append(dict(scene='covergrid', kind='none', t0=t0, t1=t1, kick=k, text=l, seed=3))
        else: S.append(card(sh, crop, t0, t1, kick=k, text=l, flash=.7 if i == 0 else .2))
    S.append(dict(scene='words', kind='none', t0=B(19), t1=B(20), lines=[dict(text='Make it', at=B(19)), dict(text='yours.', at=Bt(19, 2), color='accent')], top=700))
    flips = [Bt(20, 0), Bt(20, 2), Bt(21, 0), Bt(21, 2), Bt(22, 0), Bt(22, 1), Bt(22, 2), Bt(22, 3)]
    for b in range(4): flips += [Bt(23, b), (Bt(23, b) + (Bt(23, b + 1) if b < 3 else B(24))) / 2]
    S.append(dict(scene='themes', kind='none', t0=B(20), t1=B(24), flips=flips, kick='Themes', text='Make it yours',
                  themes=['th8', 'th3', 'th1', 'th6', 'th4', 'th2', 'th7', 'th5', 'th9']))
    m2 = [
        (clip('E1.mp4', 0.0, 1.0, 0, 0), 'today', 'Discover', 'Insights'),
        (clip('E1.mp4', 14.0, 5.0, 0, 0), 'trends', 'Discover', 'Reading trends'),
        (clip('E2 - take 2.mp4', 2.0, 9.0, 0, 0), 'contin', 'Discover', 'Continuity maps'),
        (still('earth616', 0, 0), 'era', 'Discover', 'Every era, in order'),
        (clip('Wanted.mp4', 0.0, 2.5, 0, 0), 'wanted', 'Discover', 'New this week'),
        (clip('Quick Open.mp4', 3.3, 5.0, 0, 0), 'qo', 'Jump anywhere', 'Ctrl + P'),
        (still('smart', 0, 0), 'smart', 'Organise', 'Smart lists'),
        (None, 'formats', 'Books', 'EPUB · PDF · MOBI'),
        (clip('F1.mp4', 2.0, 20.0, 0, 0), 'prefs', 'Themes', 'Dark or light'),
    ]
    for i, (sh, crop, k, l) in enumerate(m2):
        t0, t1 = B(24 + i), B(25 + i)
        if sh is None: S.append(dict(scene='formats', kind='none', t0=t0, t1=t1, kick=k, text=l))
        else: S.append(card(sh, crop, t0, t1, kick=k, text=l, flash=.7 if i == 0 else .2, keys=(crop == 'qo')))
    S.append(dict(scene='words', kind='none', t0=B(33), t1=B(34), lines=[dict(text='Free.', at=B(33)), dict(text='Open', at=Bt(33, 2), color='accent'), dict(text='source.', at=Bt(33, 2), color='accent')], top=620))
    end = B(39)
    S.append(dict(scene='outro', kind='none', t0=B(34), t1=end, facts=[Bt(35, 0), Bt(35, 1), Bt(35, 2), Bt(35, 3)], cta=B(36), social=B(37)))
    return dict(name='A', look='hype', dur=end, shots=S, offset=off, track='t4')

def edit_b():
    B, Bt, off = grid('t2', 4)
    S = []
    S.append(dict(scene='wall', kind='none', t0=0, t1=B(8)))
    S.append(dict(scene='words', kind='none', t0=0, t1=B(6), lines=[
        dict(text='Your', at=0), dict(text='comics.', at=0),
        dict(text='Your', at=B(5), color='accent'), dict(text='machine.', at=Bt(5, 2), color='accent')], top=560))
    S.append(dict(scene='logo', kind='none', t0=B(6), t1=B(8), tag=Bt(6, 2)))
    sections = [
        ('01 · Read', 'Built for long sessions', 8, [
            (clip('B1 - take 3.mp4', 0.3, 1.0, 0, 0), 'home', 'Pick up where you left off.'),
            (clip('C1.mp4', 1.0, 1.1, 0, 0), 'open', 'Open an issue.'),
            (clip('C1.mp4', 6.0, 3.2, 0, 0), 'pages', 'The backdrop takes its colour from the page.'),
            (clip('C3 - take 2.mp4', 4.0, 2.0, 0, 0), 'guided', 'Guided view, panel by panel.'),
            (clip('Manga reading.mp4', 2.8, 3.0, 0, 0), 'manga', 'Manga, right to left.'),
            (clip('EPub.mp4', 52.0, 6.0, 0, 0), 'epub', 'Books too, with page themes.'),
        ]),
        ('02 · Organise', 'A library that stays tidy', 14, [
            (clip('D1.mp4', 7.6, 2.6, 0, 0), 'detail', 'Every series has a home.'),
            (clip('D1.mp4', 16.0, 6.0, 0, 0), 'issues', 'Every issue at a glance.'),
            (clip('D2.mp4', 9.2, 2.0, 0, 0), 'rlist', 'Reading lists you can follow.'),
            (clip('D2.mp4', 14.0, 6.0, 0, 0), 'rlist2', 'Reading lists you can follow.'),
            (still('smart', 0, 0), 'smart', 'Smart lists that fill themselves.'),
        ]),
        ('03 · Discover', 'Know what to read next', 19, [
            (clip('E1.mp4', 0.0, 1.0, 0, 0), 'today', 'Insights on how you read.'),
            (clip('E1.mp4', 14.0, 5.0, 0, 0), 'trends', 'Insights on how you read.'),
            (clip('E2 - take 2.mp4', 2.0, 8.0, 0, 0), 'contin', 'Continuity maps, in publication order.'),
            (clip('Wanted.mp4', 0.0, 2.1, 0, 0), 'wanted', "This week's new releases."),
            (clip('Quick Open.mp4', 3.3, 4.6, 0, 0), 'qo', 'Jump anywhere with Ctrl+P.'),
        ]),
    ]
    for kick, title, bar0, shots in sections:
        for i, (sh, crop, line) in enumerate(shots):
            S.append(card(sh, crop, B(bar0 + i), B(bar0 + i + 1), cap=kick, title=title, line=line,
                          first=(i == 0), keys=(crop == 'qo')))
    S.append(card(clip('F1.mp4', 8.0, 4.0, 0, 0), 'prefs', B(24), B(25), cap='04 · Themes', title='Make it yours', line='Pick a theme in Preferences.', first=True))
    flips = []
    for i in range(8):
        bar, beat = 25 + i // 2, (i % 2) * 2
        flips.append(Bt(bar, beat))
    S.append(dict(scene='themes', kind='none', t0=B(25), t1=B(29), flips=flips, cap='04 · Themes', title='Make it yours',
                  line='Every screen follows.', themes=['th8', 'th3', 'th1', 'th6', 'th4', 'th2', 'th7', 'th9']))
    end = B(32)
    S.append(dict(scene='outro', kind='none', t0=B(29), t1=end, facts=[Bt(29, 2), Bt(29, 3), Bt(30, 0), Bt(30, 1)], cta=Bt(30, 2), social=B(31)))
    return dict(name='B', look='polished', dur=end, shots=S, offset=off, track='t2')

if __name__ == '__main__':
    edits = [edit_a(), edit_b()]
    extract(edits)   # cuts any new clip ranges, fills in frame folders
    covers = sorted(f for f in os.listdir(os.path.join(HERE, 'covers')) if f.endswith('.jpg'))
    accents = {'th1': '#e07a3a', 'th2': '#2bd96b', 'th3': '#ff5fa2', 'th4': '#f7f6f2', 'th5': '#4a8fe7', 'th6': '#ff6fcf', 'th7': '#ff4f8b', 'th8': '#f7931e', 'th9': '#e0a040'}
    for e in edits:
        e['covers'] = covers; e['accents'] = accents; e['themeCrop'] = R['theme']
        with open(os.path.join(HERE, f"vtimeline_{e['name']}.js"), 'w') as f:
            f.write('window.TL = ' + json.dumps(e, indent=1) + ';\n')
        print(f"vertical {e['name']}: {e['dur']:.2f}s, {len(e['shots'])} scenes")
