---
name: paperbunkr-brag
description: How to make Paperbunkr launch/promo videos (the /brag workflow) - footage handling, beat-synced edits, brand rules, the render pipeline and its gotchas. Use whenever making or re-cutting a Paperbunkr video, trailer, teaser or social clip, including when /brag or /brag-slim runs in this repo.
---

# Paperbunkr brag videos

Captures what worked (and what didn't) in the 2026-10-31 / 11-01 launch-video sessions. `/brag` (via brag-slim on Opus) owns the
general method; this file is what is specific to Paperbunkr. Working scripts are in `scripts/`, brand files in `assets/`.

## Standing decisions from the user

- **Never mention ComicRack** in promo copy or on screen: no "successor to ComicRack", no "ComicRack friendly" fact card, no "imports a CE
  library". The user doesn't want the attention. Fact cards that work: Free & open source (AGPL-3.0, no paid tier), Local-first (no cloud,
  no account, no telemetry), Reads it all (CBZ · CBR · PDF · EPUB · FB2 · MOBI), Updates itself (self-contained Windows installer).
- **No exact library counts in captions.** "1,049 series. One library." read as oddly specific. Exaggerate without a number
  ("Thousands of comics. One library."). Counts can stay where they are part of the rebuilt UI (sidebar, chips, stat cards).
- **Social: X only, `@Ehis_II`.** Every end card has an X badge (the `assets/si_x.svg` path) with the handle, under the GitHub line.
- **Fonts: Bebas Neue (display) + Source Serif 4 (body)**, the app/site fonts. The brand kit specifies Montserrat, and the user overruled it.
- **From the brand kit** (`C:\Users\DeeDee\Downloads\Paperbunkr Brand Kit`): the flat lockups (`stacked`/`horizontal` color editions, in
  `assets/`), the palette (charcoal `#0B0B0B`, cream `#F7F6F2`, orange `#F7931E`, yellow `#F2C94C` for kickers), no effects on the logo
  (no glow, shadow or tilt), proportional scaling only. App screenshots keep their own in-app amber, since they are the real UI.
- **Ask for footage rather than reuse old screenshots.** The `site/src/assets/shots` images go stale fast. The user records clips when asked.
  Give a shot list (see "Asking for footage") and they will deliver.
- **The user supplies music.** Offer two edits on the two best-fitting tracks rather than one.
- **Landscape and element-built vertical both work.** Read the vertical section before attempting 9:16.

## Vertical (9:16): element-built only (approved 2026-11-03)

Attempts 1–2 (2026-11-01) failed or were shelved. Attempt 3 (below) was approved, so start from it.

- **Only use individual on-screen elements, never whole screenshots or full-screen recordings.** Paperbunkr is a 1366×768 landscape app, and
  any reframe of a whole screen into 9:16 looks off. Attempt 1 (a square card panning over full screens) was rejected outright.
- What the user liked in attempt 2 (`scripts/vert.html` + `vert_edits.py`): portrait-native crops (a comic page, the manga page, the EPUB
  text column, a reading-list column, the Quick Open palette, cover rows), built scenes in place of weak crops (cover grids popping on the
  beat from `harvest.py` covers, format chips, keycaps under Ctrl+P, theme cards with accent dots), headline top / visual middle / bottom
  clear for platform UI.
- Next time, go further: lift elements out of the UI and re-lay them natively for portrait instead of showing any rectangle of a screen.
- **2026-11-03 attempt 3 (`work/vmotion.html`, How Far):** fully element-built at 1080×1920 on the same bar grid as the landscape motion
  cut. It uses rebuilt cards, a Detail panel opening out of the clicked card, real pages, a Quick Open palette, stat cards, and themes as
  three crops of each real screenshot (title-and-search header, Spotlight card, blurred sky). Labels sit at y 300–430, inside the
  280–1220 envelope. **Approved: "Yes, it works."** Final: `paperbunkr-motion-vertical-howfar.mp4`.

## Footage facts (the user's recordings)

- Lives in `C:\Users\DeeDee\Videos\Paperbunkr Clips`, named by shot ID (A1 cover scroll, B1 Home, C1 open+page turns, C3 guided view,
  D1 series→Detail→issues, D2 reading lists, E1 Insights, E2 continuity map, F1 theme switching, plus Manga/EPub/Wanted/Quick Open), and
  `F2 Screenshots/` (Home in 7 themes, same scroll position) and `Extra/` (Library, Insights, Reading Lists, Smart Lists, Earth-616 timeline).
- **Two recording sizes:** 1366×720 has a 22 px Windows title bar, so crop `1366:698:0:22`. 1092×614 is letterboxed, so crop `1090:554:0:18`.
  Check with `ffmpeg ... cropdetect` on any new batch.
- All clips are constant 30 fps (verified). The A1 Library poster-view scroll is the best source of sharp covers (`harvest.py`: columns at
  x = 329, 499, 669, 839, 1009, 1179, 140×212, deduped with a 16×16 difference hash).
- **Content to keep out:** the manga clip has a scanlation donations page at 0:00–0:02 and a suggestive page after ~0:16, so use
  `Manga reading.mp4` only from 2.8 s, briefly. Wanted's "next week" is empty, so stop Wanted before ~6 s of source. Quick Open needs ~0.3 s
  before the palette is up. Keep usernames, emails and `C:\Users\...` paths off screen.
- Always audit with `audit.py` (first/middle/last frame actually shown for every shot) before a full render. Contact sheets at 0.5 s steps
  (`sheets.py`) find click moments.

## Pipeline (scripts/)

To reuse: make a fresh `brag-output-<timestamp>/work/`, copy `scripts/*` into it and `assets/*` into `work/assets/`, then run everything
from `work/`. The scripts expect `tracks/`, `frames/`, `stills_src/`, `covers/`, `sheets/` and `stills/` next to them, and hard-code the
clips folder (`SRC` in `edits.py`/`harvest.py`) and the Playwright paths (`render.js`). Edit `edit_a()`/`edit_b()` in `edits.py` for a
new storyboard.

1. **Music:** decode tracks to 22.05 kHz mono WAV → `analyze_tracks.py` (tempo, energy curve, best 60 s window) → spectrogram/waveform
   sheet with a 10 s grid to see intros, drops and breaks → `beats.py <track> <bpm>` (dynamic-programming beat tracker plus downbeat phase,
   writes `tracks/<t>_beats.json` with bars). Pick a start bar on a downbeat; every cut lands on `B(bar)` or `Bt(bar, beat)`.
2. **Timeline:** `edits.py` builds each edit as data (shots with src, ss, speed, t0/t1, labels) from the beat grid, cuts each clip range to
   JPEG frames (crop only, **no** `setpts`), and writes `timeline_<X>.js`. `extract()` always runs and skips ranges already cut.
3. **Compositor:** `comp.html` renders any time `t` as a pure function: an app window with an ambient blur behind, hype (punch-in plus a
   flash on each cut, kicker plus big Bebas label) or polished (staggered slide, caption block under the window, wipes through themes),
   hook over a dimmed cover-wall scroll, stacked-lockup logo, outro with facts, Download button, GitHub and the X badge.
4. **Render:** `node render.js <A|B> stills [v] t1 t2 ...` for checks, `node render.js <A|B> video [v]` for the full render (Playwright,
   page screenshots piped to ffmpeg as MJPEG → x264). Two renders in parallel is fine; each takes about 6 min for 60 s at 1080p.
5. **Audio:** trim the track from the start bar (`-ss <bar time>`), fade out, and normalise to −14 LUFS (`volume` plus `alimiter`).
6. **Mux:** `mux.sh <video> <wav> <poster frame> <name>` bakes a settled hook frame in as frame 0 (the thumbnail) and writes `<name>.mp4`
   plus `.jpg`. Pick a poster frame where the hook text is fully in.
7. **Deliver:** `share-copy.txt` per platform (TikTok/Reels: "link in bio", since caption links aren't clickable; Shorts: title +
   description; X: one post).

`synth_audio.py` is the 20 s original-score generator from the first video, for when no track is supplied.

## Gotchas that cost time

- **Double speed-up bug:** extract frames with crop only. The compositor already maps time × speed to a frame index. Applying `setpts` in
  extraction as well made every shot run at speed² and overshoot into unwanted content (that's how the scanlation and suggestive pages
  got in). Check: frames on disk ≈ (t1 − t0 + 0.4) × speed × 30.
- **Playwright:** the global `@playwright/cli` ships Playwright 1.237 but the cached browser is `chromium-1234`, so launch with
  `executablePath: .../ms-playwright/chromium-1234/chrome-win64/chrome.exe` and `--allow-file-access-from-files`. Use
  `page.screenshot({clip})`, not `element.screenshot()` (the latter times out waiting for "stable" on long renders).
- **Cross-fades between two busy screens look muddy.** Stagger them (old out, then new in) or wipe.
- **Disk:** two 60 s edits = ~1.2 GB of cut frames. Free space was ~27 GB, so delete `work/` after delivery (ask first; the Bash
  safety check blocks globbed `rm` after `cd`).
- `tail -1` fails in this Git Bash, so use `tail -n 1`.

## Asking for footage

Give a checklist file (see `brag-output/shot-list.md` from 2026-10-31): record at native 1366×768, 100 % display scale, app maximized,
taskbar auto-hidden; OBS 60 fps "Indistinguishable"; one action per clip named by shot ID; hold still 1.5 s at the start and end; slow,
straight cursor moves at ~70 % speed; one hero theme except the theme shots; theme screenshots of the same screen at an identical scroll
position; Do Not Disturb; no personal data on screen. Mark shots must or nice.

## Full-motion version (2026-11-03)

`brag-output-motion-2026-11-03-111357/` rebuilds the app as live HTML instead of using recordings (`work/motion.html`, `timing-map.md`;
method in the user-level `motion-graphics` skill). With the user's permission it reads the real library read-only:
`%APPDATA%\Paperbunkr\thumbnails\<issueId>.jpg` (260×400 covers), `paperbunkr.db` via `py` + sqlite3 `mode=ro` from the PowerShell
tool (the Bash sandbox sees a stale copy), and a few pages unzipped from CBZs on D: (read only; never write to D:). Filter out webtoon
strips and letterboxed thumbnails by aspect ratio and row/column variance.

- **Themes must be the real thing.** The user rejected themes rebuilt from `Assets/Skins/*/theme.json` colour tokens because they lack
  the sky gradients, Matrix rain and Newsprint serif type. Use the `F2 Screenshots` captures (crop 1366×698). They show Default,
  Sunset Boulevard · Dawn, Matrix, Tokyo Midnight, Newsprint, Nordic Frost · Light and Arcade (twice); names were matched to the skin files.
- One persistent cursor whose clicks cause each change (card → Detail opening out of the card rect, Continue → page, taps → page
  turns, theme toggle → circle reveal from the click point, rail → Insights).
- Render one video at a time: two parallel renders made a frame screenshot stall. `render.js` now retries a frame up to 3 times.

## Past outputs

- `brag-output/`: the 20 s first cut (old landing-site screenshots, synthesized score).
- `brag-output-2026-11-01-094100/`: the approved 60 s cuts `brag-gozalo-v2.mp4` (hype, GOZALO (Slowed), 139.7 BPM, from 0:07.8) and
  `brag-howfar-v2.mp4` (polished, How Far Visualizer, 112.1 BPM, from 0:09.9), the shelved verticals, `brag-plan.md` and `share-copy.txt`.
  Files without `-v2`/`-vertical` there have the speed bug and the ComicRack line, so don't reuse them.
