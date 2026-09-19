# Library scroll smoothness

Status: draft for review, 2026-09-19. Second of two specs; the first is
[2026-09-19-library-search-perf-design.md](2026-09-19-library-search-perf-design.md) (implemented on branch
`claude/library-search-perf`). This spec builds on that branch: it touches the same view-model and the same
`LibraryScreen` files.

## Problem

The user wants Library scrolling to feel like ComicRack's: no hitches while scrolling (wheel or scrollbar
drag), covers already painted instead of popping in, and eased mouse-wheel motion. The library is ~3,000 comics in
~500 series, so this is not a scale problem. Each realized card is expensive, and work runs on every scroll pixel
that does not need to.

## What ComicRack actually does (looked up in `_reference/ComicRackCE`, not assumed)

- Paint is cache-only: `CoverViewItem.OnDraw` asks for a memory-cached thumbnail and never decodes or touches
  disk (`ThumbnailViewItem.cs:113-142`). A miss draws the tile empty at an estimated size and queues the load; the
  arriving image fades in over 300 ms and repaints only its own rectangle.
- Loading is a **newest-first bounded queue** (`ImagePool.cs:120-150`, `ProcessingQueue.cs:359-408`): 256 entries,
  re-requesting a queued key moves it to the front, the oldest overflow is dropped. One fast thread for
  disk-cache hits, up to four slow threads, all at lowest priority. Nothing is cancelled on scroll; stale
  requests simply fall off the end.
- Memory tier is small (50 MB default, JPEG bytes; decoded bitmaps dropped after 15 s idle) over a 500 MB disk cache.
- There is **no easing or inertia** and no fling mode by default. Smooth means cheap paint plus tile sizes known
  up front. (Its grid is not virtualized at all; Paperbunkr's already is, so that part is not copied.)

Taken from CE: cache-only paint, the queue policy, stable tile size. Not taken: its non-virtual grid, its lack of easing
(the user asked for easing on top).

## What Paperbunkr does today (read from the code, 2026-09-19)

1. **The entrance animation replays on every scroll recycle.** `LibraryScreenViewModel.PlayEntranceAnimation` is set
   to `true` on every rebuild and is never reset to `false` anywhere in Library. Because it is an observable
   property, setting it to `true` again raises nothing, so it stays `true` for the whole session.
   `EntranceAnimation` reads the flag at every container preparation, so every card the panel recycles while scrolling
   gets a `DispatcherTimer`, two class changes, and two transitions (`Opacity`, `RenderTransform`).
2. **The panel invalidates layout on every scroll pixel.** `VirtualizingWrapPanel.OnEffectiveViewportChanged` calls
   `InvalidateMeasure()` whenever the viewport top or bottom moves by 0.5 px. Every scroll pixel then runs
   `MeasureOverride` (which re-runs `RealizeViewportRange` with a LINQ `Keys.Where(...).ToList()` allocation and measures
   every realized container) and `ArrangeOverride`, even though absolute child positions never change while the
   set of realized rows is the same. The realized-range buffer is a fixed 2 rows.
3. **A poster card is ~30 visuals**: `Button` + `Grid`, a `Border` with a blurred `BoxShadow` and `ClipToBounds`, an
   inner `Grid`, three `Image`s (cover, dog-ear with a `Clip` geometry, plugin overlay), a scrim `Border`, a publisher chip
   `Border` + `BrandMark`, an unread `Ellipse`, a rating `Border` pair, a templated `CheckBox`, two `TextBlock`s with
   tooltips, and six `MultiBinding`s (item property AND a view-model setting) that re-evaluate per card. Each recycle
   re-binds all of it. Most of it is invisible for most cards.
4. **Cover decode is unprioritized and unbounded.** `AsyncCoverImage` starts a `Task.Run` decode per cache miss.
   A container that is recycled before its decode finishes still decodes; nothing is dequeued. A fast fling queues
   dozens of stale decodes ahead of the covers actually on screen. Every decode is a full 400 px JPEG (`new Bitmap(path)`).
5. **The cover cache is shared and cannot shrink safely.** `CoverImageCache` holds up to 5,000 decoded bitmaps (≈ 4.8 GB
   worst case at 267×400×4 bytes) and hands the same `Bitmap` instance to Home, Detail, Events and others that bind it
   into long-lived view-models, so eviction cannot dispose (a disposed bitmap crashed the app once; `LruCache` says so).
6. **Small per-recycle costs**: `CoverAspectRatioStore.Report` on every cache hit (dictionary + lock), a
   `PreviewIssue` update on every keyboard focus step, hover handlers that arm tooltips.
7. **Wheel scrolling is stock `ScrollViewer` stepping** (no handler in Library); touch already has native inertia.

## Goals and how they are measured

Metrics (Step 0 measures them before any change; every later step reports them again):

| Metric | Meaning |
|---|---|
| Step cost | Layout + realize time (and CPU raster time in the headless renderer) for one scroll step of one row, p50/p95/max over a scripted scroll |
| Pop-in | Fraction of newly visible cards that have no cover in their first frame, at slow / medium / fast scroll speeds |
| Wasted decodes | Decodes that finished for a card that was already off screen and never used |
| Decode backlog | Maximum queue depth during a fast fling |
| Memory | Working set after a scripted top-to-bottom scroll; grid cover cache bytes |

Acceptance: step cost p95 at least halves versus the Step 0 baseline and no scripted step exceeds the 16 ms frame
budget at p95; pop-in and wasted decodes drop materially at medium and fast speeds; memory does not exceed the ~1.2 GB
the user already verified. Exact thresholds are set from the Step 0 numbers and written into the plan before
implementation, not guessed here. The final acceptance is the user's on-screen check on the real library.

## Non-goals

- List, Details, and Panorama (`VirtualizingVariableWrapPanel`) get the shared pieces for free (cover pipeline, entrance
  fix) but are not tuned in this pass; they are measured afterwards and fixed only if bad.
- Replacing the XAML card with an owner-drawn control (CE's approach): rejected, it loses styling, focus rings,
  accessibility, and the skin/theme system for a win the steps below may not need.
- Changing thumbnail size/format on disk, or the other screens' use of `CoverImageCache`.
- GPU or compositor-level changes.

## Decisions already taken with the user

| Topic | Decision |
|---|---|
| What "smooth" means | All three: hitch-free, no pop-in, wheel easing. Easing goes last, after the pipeline is cheap |
| Modes | Poster and Tiles first |
| Order | Measure first, then fix by measured cost |
| Card look | Lazily build the invisible parts now. Blurred shadow only if measured to matter, then ask again with before/after |
| Memory | Display-size decode, byte-budget cache (~300 MB constant, not a Preference), stays under ~1.2 GB |
| Decode queue | Newest-first, bounded, stale requests dropped, low-priority threads |
| Prefetch | ~2 viewports ahead in the scroll direction, decode only, inside the byte budget |
| Wheel easing | Mouse-wheel notches only; touchpad/precise deltas, scrollbar drag, keyboard pass through; Reduced Motion turns it off; a "Smooth scrolling" toggle, default on |

## Defaults chosen in this draft (confirm or change on review)

| # | Default |
|---|---|
| D1 | The measurement harness is a new dev-only console project (`src/Paperbunkr.ScrollHarness`), not shipped, not part of the installer or the normal test run. The xunit headless bootstrap cannot render reliably in this environment: `MatrixRainOverlayRenderTests` and several `RunJobs()` tests already fail with "different thread owns it" on HEAD, so a render harness needs its own main-thread bootstrap. |
| D2 | The entrance animation becomes one-shot: it plays for a real reload, view-mode change, and sort/group, and **not** for cards recycled by scrolling. (Visible change: scrolling no longer re-fades cards in.) |
| D3 | The grid gets its **own** cover cache (`GridCoverCache`); the shared `CoverImageCache` is left alone for the other screens. |
| D4 | One wheel notch scrolls the same distance Avalonia's stock `ScrollViewer` scrolls today (measured once in the harness and pinned in a test), only animated. No new step size to relearn. |

## Design

### 1. Measurement harness (Step 0)

`Paperbunkr.ScrollHarness`: a console app that bootstraps `AppBuilder.Configure<App>().UseSkia().UseHeadless(...)` on
its own main thread (so dispatcher and compositor ownership are consistent), redirects the database to a temp file
seeded with 3,000 issues / 500 series (and a 10,000 / 1,500 headroom run) plus synthetic thumbnails on disk, mounts
`LibraryScreen` with the real `App.axaml` resources in a headless window, and runs scripted scrolls (slow, medium, fast
fling; down then back up) by setting `ScrollViewer.Offset` and forcing layout and render ticks. It prints the metrics
table above as text. It is the acceptance evidence for every later step and never runs in CI.

### 2. Make the entrance animation one-shot (D2)

- `EntranceAnimation` gets an entrance **window**: when `Enabled` becomes `true` on an `ItemsControl`, a 500 ms window
  opens (per-control state), and `Prepare` animates only containers prepared inside it. A container recycled after the window
  is left untagged and fully visible.
- The view-model pulses the flag (`false` then `true`) on the triggers that should animate (`Full`, `SortGroup`, view-mode
  change) so the property change is real, and leaves it `false` for search/filter/panorama swaps (already the case after the
  search work). This also removes the always-`true` state.
- Reduced Motion behavior is unchanged (delay collapses to zero, transitions already zeroed by the token).

### 3. Cover pipeline

**3.1 `GridCoverCache`.** Separate from `CoverImageCache` (which still serves every other screen unchanged). Keyed by
`(cover stem, width bucket)`, byte-budgeted (≈ 300 MB constant), LRU by bytes, decoded with `Bitmap.DecodeToWidth` at the
display size. Width bucket = card cover width × render scaling rounded up to a 64 px step, clamped to [96, 400], so dragging the
density slider does not re-decode on every step. As with the shared cache, **eviction only drops the reference, it never
disposes**: a bitmap may still be bound by a realized `Image`. Byte accounting is `width × height × 4`. After this,
the grid stops filling the shared cache, so its 5,000-bitmap worst case stops being reachable from Library scrolling.

**3.2 `CoverDecodeQueue`.** Replaces the per-miss `Task.Run`. Newest-first, bounded (256), two priorities
(Visible, then Prefetch nearest-first), re-requesting a queued key moves it to the front, overflow drops the oldest, and a
container recycled before its decode starts **dequeues** its request (CE only drops overflow; Paperbunkr already knows exactly
when a container is recycled). Worker count `min(3, ProcessorCount / 2)` at below-normal priority. An in-flight decode cannot be
cancelled, but its result is cached (scrolling back finds it). Completed decodes are applied on the UI thread at Background
priority, at most a small fixed number per dispatcher tick, so a burst of arrivals cannot itself cause a frame spike.

**3.3 Prefetch.** `VirtualizingWrapPanel` already knows the viewport and the realized range. It reports the range and scroll
direction through an event; a `CoverPrefetcher` maps the next ~2 viewports of items (beyond the realized buffer) to cover
keys through a small `ICoverKeyProvider` interface implemented by `SeriesCardSample` and `IssueListRow`, and enqueues them at
Prefetch priority. Prefetch only decodes into `GridCoverCache`; it never realizes containers. A direction reversal or a
jump (A-Z index, scrollbar drag far away) drops the pending prefetch requests.

**3.4 Cheaper hit path.** `AsyncCoverImage` hit: skip `CoverAspectRatioStore.Report` when the ratio is already persisted.
`AsyncPluginOverlayImage` is only attached when a plugin implements the overlay hook (see §4).

### 4. Card cost (each item gated by the harness delta; kept only if it earns its place)

1. **Settings-driven visibility without per-card `MultiBinding`s.** The publisher chip, unread dot, rating pill, continue button,
   tile titles and selection checkbox are gated by a view-model setting AND an item property. Move the setting half onto the
   grid `ItemsControl` as classes (`showPublisher`, `showUnread`, ...) and let styles reveal the element, leaving a single simple
   item-property binding per card. Zero per-card evaluations when a setting flips.
2. **Build rarely-used parts on demand.** Dog-ear `Image` + `Clip` geometry: created on first hover when the setting is on
   (the hover handler already exists). Plugin overlay `Image`: only in the template variant used when a plugin implements
   `DrawThumbnailOverlay`. Selection `CheckBox`: a light glyph until hover or an active selection.
3. **Blurred `BoxShadow` on the cover.** Only if Step 0 shows it dominates; then the user is asked again with before/after
   frames (design decision Q5 above).
4. **Preview panel on focus.** `OnCardGotFocus` sets `PreviewIssue`/`PreviewSeries` on every keyboard focus step; coalesce to
   the last value per dispatcher tick if the harness shows it matters during arrow-key repeat.

### 5. Panel layout cost

- Do not invalidate layout unless the wanted realized range **changes**. Compute the target first/last index from the
  viewport; if equal to the current range, return without `InvalidateMeasure()`. Children keep their absolute positions
  while the range is unchanged, so no measure or arrange is needed until a row boundary is crossed.
- `RealizeViewportRange` without allocations (reuse a scratch list, no LINQ), `_realizedByIndex` iteration without
  enumerator churn, and `Measure` only for newly realized containers.
- `BufferRows` becomes viewport-relative (`max(2, ceil(0.5 × visible rows))`) so a fast fling has containers ready.
- Recycle-pool size is capped so the pool does not hold hundreds of hidden containers after a resize.

### 6. Wheel easing (last)

`SmoothWheelScroll` attached behavior on the Library `ScrollViewer`s:

- Tunnel `PointerWheelChanged`. Handle only discrete-notch deltas from a mouse wheel (integral, |Δy| ≥ 1); pass
  through fractional/precise deltas (touchpad), horizontal deltas with a modifier, scrollbar drag, keyboard scrolling, and
  touch (native inertia).
- Each notch adds `notches × StepPixels` to a target offset (D4: the stock per-notch distance) clamped to the extent; the
  offset eases toward the target over ~150 ms with a cubic ease-out, driven by `TopLevel.RequestAnimationFrame`, not a
  timer. A new notch retargets from the current offset (no restart from the start), so rapid notches accumulate smoothly.
- Off entirely when Reduced Motion is on or the toggle is off (plain stock behavior).
- **Toggle**: Preferences → Behavior "Smooth scrolling", default on, carried by the same static push-cache pattern as
  `CosmeticThumbnailSettings`. It needs one additive `AppSettings` column with a migration; being additive, an older build
  sharing the per-user dev database keeps working. This is a deliberate deviation from CE, which has no such setting
  (recorded in the deviation notes when implemented).

### 7. Sequencing

Harness (Step 0) → entrance one-shot → panel invalidation → decode queue + `GridCoverCache` → prefetch → measured card trims →
wheel easing + toggle. Easing is last because it drives the same offset changes at frame rate; layering it on a hitchy
pipeline would make every hitch more visible.

## Testing

- Unit: `GridCoverCache` (byte budget, LRU by bytes, bucket selection, never disposes on eviction), `CoverDecodeQueue`
  (newest-first, bound, priority, dequeue on recycle, dedup, re-request moves to front), prefetch range math and
  direction reversal, panel range-equality rule with a fake viewport (no invalidation inside a row, one per row crossing),
  `EntranceAnimation` window (animates inside it, not after it, Reduced Motion), wheel-easing math (target accumulation,
  clamp, notch classification, retarget) as pure functions, and the setting-class visibility styles.
- Existing suites stay green except tests that pin the old always-`true` entrance flag (updated deliberately).
- Harness before/after for every step; results recorded in the plan.
- On-screen check by the user; no UI automation without permission.

## Risks

- **Headless numbers are a proxy.** CPU raster and layout cost are measured; real GPU behavior is not. The user's check is the
  real acceptance test, and the harness is used for relative before/after, not absolute frame times.
- **Display-size decode helps less at high DPI.** At 150 % scaling a 225 px request from a 267 px source saves ~30 % of the
  pixels; at 100 % about two thirds. The bigger wins are the byte budget and the faster scaled decode, but the harness confirms it.
- **Dequeuing on recycle races with a decode already started.** The result is still cached and simply not applied
  (existing generation guard), so the race is harmless.
- **Eased offset vs. virtualization anchors.** Offset changes every frame during easing; the panel range-equality rule (§5)
  is what keeps that cheap, which is why §5 precedes §6.

## Out of scope, logged

- Tuning List/Details/Panorama; owner-drawn card; moving `CoverImageCache` consumers to shared display-size bitmaps;
  nav-in `LoadFromDatabase` cost (still logged from the search spec).
