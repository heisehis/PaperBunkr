# Continuity map M6b "Polish" — design

*Status: approved by the user 2026-11-11 (grilling round 4, Q47–Q50, with a visual-companion screen). Source: the cosmetics section of the
[continuity map pitch](2026-11-11-continuity-map-pitch.md) (items 21–24). Builds on [M1](2026-11-11-continuity-map-room-design.md); the
export also covers [M3](2026-11-11-continuity-matrix-rollcall-design.md)'s Matrix. Implemented in the working tree at base HEAD `28e60a1`
(2026-11-12); on-screen review pending.*

## Goal

Make progress visible as the shape of the map, give the continuity ruler some art, animate connections on selection, and let the user save
a map as an image.

## Facts this rests on (verified 2026-11-11)

- **Motion:**
  - `MotionPolicy.AllowsMotion(role)` / `AllowsContinuous(batteryLow)` (`Theming/Motion/MotionPolicy.cs:148, 151`) and
    `MotionTokens.Get(role)`.
  - Looping effects follow `AmbientBackdrop`'s pattern (an infinite `Animation` gated by `AllowsContinuous`, `Controls/AmbientBackdrop.cs:177,
    200-213`).
  - The reader's bookmark glow uses `PbGlowPulseRing` with `.pulsing`.
  - There's no animated path drawing in the app; edges are static `StreamGeometry` (`EventMapEdgeLayer.BuildPath`, replaced by M1's canvas).
- **Event covers:** `StoryEventCoverCache.Get(eventId) → Bitmap?` (`Services/StoryEventCoverCache.cs:22`). `HeroWash` is the light-theme
  veil for text over cover art (opacity 0.7 on Light, 0 otherwise).
- **Image export:**
  - `FilePickerService.PickSaveFileAsync(title, name, extension, label)` (`Services/FilePickerService.cs:105`).
  - The only control-to-PNG code is `InsightsScreen.OnExportPosterClick` (`Views/InsightsScreen.axaml.cs:504-546`): a hidden host,
    Measure/Arrange, `RenderTargetBitmap.Render`, then `Save`. There's no shared helper.
- **No year row on the ruler** (column labels, trunk badges, and the band/connector rows only). The pitch's era tint is dropped (Q48).

## Decisions

| # | Decision |
|---|---|
| Q47 | Edges up to the last-read issue draw solid in the accent; the rest thin and faded. A "You are here" ring on the next unread card pulses slowly, and is still under Reduced motion or with continuous motion off. |
| Q48 | A soft cover wash on continuity ruler event bands. No era tint. |
| Q49 | Selecting a card traces its connections outward, with related cards lighting up in sequence; off under Reduced motion. |
| Q50 | **Save as image…** in the map More menu renders the whole Map or Matrix with a title and a small Paperbunkr mark, stepping down the card look above 16,000 px. It shares a helper extracted from the Insights poster export. |

## Design

### 1. Read path line and "You are here"

**Which edges count as read** (pure, `EventMapReadPath.Classify(layout, readStates) → per-edge Read | Unread`):
- *Relay chain and continuity event-block chains:* an edge is Read when its **target** card is Read, so the solid line runs up to the
  last read issue in reading order.
- *Spine sequence edges* (per lane and trunk): Read when the target is Read.
- *Tie-in edges:* Read when the target (the lane's first card in the segment) is Read.
- *Continuity edges* (promoted Prologue/Epilogue): Read when the target is Read.
- In-progress targets count as Unread.

**Drawing (M1 canvas):**
- Read edges: `PbAccentBrush`, 2 px.
- Unread edges: their normal pen at 35 % alpha, 1 px.
- Selection dimming and the related-set emphasis apply on top, as today.
- A "Progress line" toggle in the toolbar More menu turns this off; it's persisted as `AppSettings.EventMapProgressLine` (default true).

**You are here:**
- **Which card:** the layout's `FirstUnread` card.
- **Drawing:** a ring drawn around it, 3 px outside the card frame, in `PbAccentBrush`.
- **Pulse:** with `MotionPolicy.AllowsContinuous()`, the ring's opacity eases 0.35 → 0.9 → 0.35 over 2.4 s (role Ambient, `PbMotionMoveEase`),
  driven by the canvas's own clock.
  - The clock runs only while the ring is on screen and the window is active. It stops at Dots (where the ring would be noise) and when
    the map isn't visible.
  - Otherwise the ring is drawn static at 0.8.
- **Other views:**
  - Matrix: the cell containing it gets the same ring.
  - List: the row gets a 3 px accent bar plus a "Next up" chip.
- **Screen readers:** its peer name ends ", next to read".

### 2. Band art (continuity ruler)

- **Which bands:** each *event* band in the ruler's band row that's at least 80 px wide on screen.
- **Image:** the event cover from `StoryEventCoverCache.Get`, decoded once to a 48 px-wide thumbnail (off the UI thread) and drawn
  stretched to the band with `BitmapInterpolationMode.HighQuality`. The upscale from 48 px is the blur; there's no blur effect.
- **Opacity:** 0.30 on dark themes, then a `PbChromeBrush` scrim at 0.55 over it so the band label stays readable. On Light themes the
  scrim is 0.75, the `HeroWash` rule.
- **What it's under:** the existing alternating block washes, the M3 progress fill and the connectors stay drawn above it.
- **Events without a cover** keep today's plain band.
- The thumbnails share the M1 cover source's LRU.

### 3. Trace on select

**Graph:**
- On selection, compute the related set's edges by hop distance from the selected card: hop 1 is edges touching it, hop 2 the next, and
  so on, up to the related set M1 already defines.
- The related cards get the hop of their nearest edge.

**Animation** (canvas clock, role Travel):
- Edges at hop *k* draw in from their source end to their target end, revealed along the path length (`PathGeometry` trimmed by the
  progress fraction).
- Each hop starts 60 ms after the previous one. The whole trace is capped at `MotionTokens.Get(Travel)` by compressing the stagger when
  there are many hops.
- Related cards lift from dimmed to full opacity at their hop's start.
- Unrelated cards and edges dim straight away (M1's 0.25 / 25 %).
- A new selection mid-trace restarts it from the new card.
- **Reduced motion / `AllowsMotion(Travel)` false:** everything appears at once, as in M1.

### 4. Save as image

**Shared helper.** `ControlImageExporter.RenderToPngAsync(Control content, Size size, double dpiScale, string path, CancellationToken)` in
`Services/`, extracted from `InsightsScreen.OnExportPosterClick`:
- It adds the control to a host, measures and arranges it, renders it with `RenderTargetBitmap` and saves the PNG.
- It always removes the control in `finally`.
- Insights is moved onto it in the same step, with no behaviour change.

**Map export:**
- **Menu item:** "Save as image…" in the map toolbar More menu, for Map and Matrix modes.
- **What's rendered:** a fresh, off-screen `EventMapCanvas` (or `EventMatrixCanvas`) bound to the same layout, filters, Featuring and
  Colour by. It has no selection, no hover, no You-are-here pulse (the ring is drawn static) and no inspector.
- **Scale:** the current look's scale. If the full map would be wider or taller than 16,000 px, step down through the looks (Covers →
  Standard → Compact → Chips → Dots) to the first one that fits. Dots always fits up to the 2,000-card budget.
- **Frame around it:**
  - a 96 px title band: name (`pbTextHeading` size), stats line, "Saved from Paperbunkr on {date}"
  - a 40 px footer with the Paperbunkr brand mark (the app icon asset) at the right
  - the theme's `PbBgBrush` background, so exports match the user's theme
- **Saving:**
  - `FilePickerService.PickSaveFileAsync("Save map as image", "{name} map", "png", "PNG image")`.
  - Rendering runs as an Activity Center job when it takes over a second, with a progress line and Cancel. The finished alert has
    "Open folder".
  - A failure gives an Activity Center alert with the reason. A partial file is deleted.
- **Memory guard:** a render larger than 16,000 × 16,000 is refused before allocating; the step-down rule avoids reaching it.

## Testing

**Pure:**
- `EventMapReadPath`:
  - relay chain up to the last read
  - spine sequence/tie-in/continuity rules
  - in-progress counts as unread
- Trace hop computation:
  - hop distances
  - stagger compression to the Travel cap
- Export scale step-down:
  - picks the largest fitting look
  - Dots always fits the 2,000-card budget
  - a refusal beyond the cap

**Headless:**
- the progress-line toggle persists
- the You-are-here ring pulses only when `AllowsContinuous` (a fake policy), and is static at Dots and under Reduced
- band art drawn only for covered bands ≥ 80 px
- the trace finishes within the Travel duration, and is instant under Reduced
- `ControlImageExporter`:
  - writes a PNG of the requested size
  - removes the control after a thrown render
- Insights export still works through the helper (its existing test).
- Map export:
  - title band and footer present
  - a cancelled job leaves no file

**UI checks:** the lint set (motion rules: no overshoot easings, tokens only); renders of the progress line, the You-are-here ring and
band art in three themes; one exported PNG of a spine event checked by eye; the review checklist.

## Risks

- **Continuous animation cost.** The pulse clock only runs while the ring is visible, and stops when the window is inactive or the map is
  hidden.
- **Export size.** Capped and stepped down; a very wide continuity at Dots is still a long, thin image. That's acceptable for a poster.
- **Insights refactor.** It moves to the shared helper in the same step, with its existing export test as the guard.

## Implementation notes (2026-11-12)

Built directly from this design. Code: `Services/EventMap/EventMapPolish.cs` (`EventMapReadPath`, `EventMapTrace`,
`EventMapExportScale`), `Controls/EventMap/EventMapCanvas.Polish.cs`, `Services/ControlImageExporter.cs`, `Views/EventMapView.Export.cs`,
`ViewModels/EventMapViewModel.Polish.cs`, migration `20261111231845_AddEventMapProgressLine` (`AppSettings.EventMapProgressLine`, default
true for existing rows; `Down` drops only that column). Where the build differs from the text above:

- **One read rule.** Every edge kind turned out to reduce to "read when its target card is read", so `EventMapReadPath.Classify` is that
  one rule; the per-kind cases in §1 are its consequences.
- **"You are here" in the Matrix** is a still ring on the cell (no pulse there); in the List it's the **Next up** chip plus an accent
  stripe. The map's ring pulses only while it is drawn, the window is active and continuous motion is allowed.
- **Trace timing:** hops start 60 ms apart, compressed to fit 60 % of the Travel duration; each edge then grows over the rest (at least
  60 ms). Cards lift at their hop's start without a fade of their own.
- **Band art** loads a 48 px thumbnail off the UI thread (`EventMapCanvas.BandArtLoader`, replaceable in tests) and keeps it per canvas.
- **Export without a job.** `RenderTargetBitmap` has to run on the UI thread in one call, so there is no progress or Cancel. The result
  is an Activity Center alert (saved: the path; refused or failed: the reason), with no "Open folder" action: `ActivityAlert` only links
  inside the app. A Covers export is only used for maps of 300 cards or fewer (every cover is decoded for the one render); bigger maps
  step down to Standard or smaller.
- **Export covers and band art** decode synchronously on the off-screen canvas (`UseSynchronousCovers`), so the saved image has them.
- **Verified:** `EventMapPolishTests` (10: read path and in-progress, spine next-up, trace hops/stagger/trim, export step-down and
  refusal, trace only with motion, still ring and the ", next to read" peer name, band art for event bands, progress-line toggle,
  exporter size/cleanup/no partial file) and `AddEventMapProgressLineMigrationTests` (3, synthetic database, never the user's) pass;
  Insights' poster export now goes through `ControlImageExporter`. Renders of the progress line on event and continuity maps in three
  themes were checked by eye. Not verified: the pulse and trace on screen, a real saved PNG of a big continuity, band art with real covers.
