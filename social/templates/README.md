# Post card templates

The text that goes out with each card has its own templates: **[captions.md](captions.md)** (one per series; the card
carries the statement, the caption adds what the card can't).

Branded 16:9 cards for X and Telegram, with slots you drop images (or a screen recording) into. One HTML file
(`card.html`, brand kit: charcoal/cream/orange/yellow, Bebas Neue + Source Serif 4, @Ehis_II badge), rendered with the
same Playwright/Chromium as the brag videos.

```bash
node social/templates/render.js social/templates/specs/<name>.json
```

Writes `<out>.png` (2400×1350). A spec with a `clip` also writes `<out>.gif` (for X, keep it under 15 MB) and `<out>.mp4`,
and the PNG becomes a poster frame from the clip. Point the draft's `media:` line at the file.

## Spec

```json
{
  "layout": "cover",                 // clip | cover | versus | take | drop | trio
  "brand": "fan",                    // "paperbunkr" adds the Paperbunkr logo; "fan" is the handle only
  "kicker": "RATE THIS COVER",       // optional; each layout has a default
  "title": "Saga #1",
  "images": { "cover": "https://... or social/media/file.jpg or C:/full/path.png" },
  "out": "social/media/2026-11-05-saga-cover"
}
```

| Layout | Series | Slots (`images`) | Text fields |
| --- | --- | --- | --- |
| `clip` | Build Log, Paperbunkr Tip | `media` (screenshot) or a `clip` | `kicker`, `title` |
| `cover` | Rate the Cover | `cover` (portrait) | `title`, `credit` |
| `versus` | This or That | `a`, `b` (optional; big A/B letters without them) | `a.label`, `b.label` |
| `take` | Hot Take, Format Wars | `bg` (optional, dimmed) | `title`, `subtitle` |
| `drop` | Drop Day | `art` (official art) | `kicker` (e.g. NEW CHAPTER), `title`, `subtitle` |
| `trio` | Starter Pack, Reader's Desk | `item1`, `item2`, `item3` (covers) | `title`, `items[]` of `{tag, name, line}` |

An empty slot renders as a labelled grey box, so a spec can be laid out before the images exist.

`clip`:
```json
"clip": { "src": "C:/Users/DeeDee/Videos/Paperbunkr Clips/C3 - take 2.mp4", "start": 11, "duration": 9,
          "crop": "1366:698:0:22", "fps": 12, "width": 960, "posterAt": 0.5 }
```
`crop` removes the recording's title bar (1366×720 recordings: `1366:698:0:22`; 1092×614: `1090:554:0:18`), or zooms in on
part of the screen (Quick Open used `860:402:116:40`). The clip is cover-scaled into the slot.

## Rules

- **Clip cards reuse the brag edit's caption** for that shot, word for word, so the GIFs match the launch video
  (`brag-output-2026-11-01-094100/work/edits.py`, edit B's per-shot lines: "Guided view, panel by panel.",
  "Jump anywhere with Ctrl+P.", "Manga, right to left.", "Continuity maps, in publication order.", ...).
- **Every image gets alt text**: an `alt:` line in the draft's front matter (Buffer requires it for X images; describe
  what's on the card, including its text).
- Official art only (publisher sites, official promo), credited in the post. No scans of interior pages, no scanlations.
- Paperbunkr cards: `"brand": "paperbunkr"`, never mention ComicRack.
- For X, Buffer needs a public URL: upload the rendered file with `gh release upload media <file>` and use
  `https://github.com/heisehis/PaperBunkr/releases/download/media/<file>`. Telegram takes the local file.
