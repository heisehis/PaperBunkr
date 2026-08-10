# Reader Polish — Continuous/Webtoon Scroll, Chrome/Overlays, Magnifier, Image Adjustment, Background/Margins

*Date: 2026-08-10. Second slice of the Beta backlog's "Reader polish" item (the first was
docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-design.md — fit modes/zoom/
rotation, shipped). This slice bundles five backlog items into one pass, at the user's direction:
continuous/webtoon scroll (the highest-risk, most novel piece — no CE precedent), fullscreen/
minimal-chrome + on-screen overlays, magnifier, live image adjustment, and background/margins.
Explicitly excluded: double-page spread layout + split-page navigation, page-transition animations,
touch gestures beyond what's shipped, remappable shortcuts, auto-scroll — separate sub-projects.*

## 1. Scope and a deliberate architecture deviation

Ships:
- `ReadingMode.VerticalContinuous` and `ReadingMode.HorizontalContinuous` go live (currently dormant
  — the label/toggle exists, nothing renders differently today; both fall through to the paged
  renderer regardless of mode).
- Fullscreen + minimal-chrome (one toggle, matching CE) + status/scrubber overlays.
- Cursor-following magnifier.
- Live brightness/contrast/saturation/gamma adjustment.
- Background mode (Auto/Color) + page margin.

**Architecture deviation, made explicitly and at the user's direction, not by default:**
docs/onboarding.md §8 originally specified *two* rendering mechanisms for a documented reason —
`ICustomDrawOperation` for paged mode (discrete, UI-thread-synced redraws on page-turn) and
`CompositionCustomVisualHandler` for continuous mode (render-thread-independent, needed for smooth
scroll during background decode). This pass unifies both onto `CompositionCustomVisualHandler`
instead, discarding that render-thread-isolation benefit for paged mode in exchange for one
maintained rendering codepath. This was raised and confirmed twice during design (see the
conversation this spec came from) — it is a real, acknowledged trade of "paged mode's shipped,
tested render path stays untouched" for "one codepath end to end." §4 covers the mechanics; this
note exists so a future reader of onboarding.md §8 (which still describes the original two-mechanism
split) understands why the shipped code disagrees with it. **onboarding.md §8 gets rewritten
alongside this implementation to describe the unified pipeline**, not left to silently drift.

**Blast radius note:** `PageCanvas` (the control being rewritten) is shared by the comic Reader
*and* the Novels PDF reader (`PdfPageReaderScreen`), which binds the same control for its own
paged single-page display and has no fit-mode/rotation/continuous-mode concept of its own. The
unified control must keep behaving identically for that caller when continuous-mode/magnifier/
adjustment properties are left unbound — same "unused properties default to today's exact
behavior" discipline the fit-mode pass already established for this same control.

**CE-verification note:** every behavior below was checked against `_reference/ComicRackCE` before
being scoped: `ComicRack.Engine/Display/IComicDisplayConfig.cs` (the full settings surface —
background/margin/magnifier/adjustment/overlay fields), `ComicRack.Engine.Display.Forms/
ComicDisplayControl.cs` (magnifier draw logic and field defaults, `InfoOverlays` usage, fullscreen→
`MinimalGui` coupling), `cYo.Common/Drawing/BitmapAdjustment.cs` + `BitmapAdjustmentConverter`'s
`ImageProcessing.CreateColorMatrix`/`ApplyAdjustment` (the actual adjustment formula),
`ComicRack/Config/DisplayWorkspace.cs` (background/margin defaults), `ComicRack/Dialogs/
PreferencesDialog.Designer.cs` (adjustment slider ranges), and `ComicRack/MainForm.cs` (fullscreen
toggle wiring). Continuous scroll itself has **no CE precedent** — confirmed absent, not guessed —
so §3's architecture is Paperbunkr's own design, grounded in the already-researched mitigation plan
in onboarding.md §8 (Avalonia issue #18498's native-bitmap memory growth) rather than in CE source.

## 2. Layout model vs. render layer

Per onboarding.md §8's existing (and retained) principle, these stay separate layers:

- **Layout model**: given `ReadingMode` + current position (page index for paged mode, scroll
  offset for continuous), computes the ordered list of pages that are visible or near-visible, and
  each one's target rect. Paged mode: always exactly one page, full-bounds rect. Continuous mode:
  every page from the virtualization window (§3), stacked along the scroll axis (Y for
  `VerticalContinuous`, X for `HorizontalContinuous`), each page's cross-axis size fit-to-viewport-
  width/height, main-axis size derived from its own aspect ratio (pages are *not* uniform height/
  width — a tall splash page takes more scroll-axis space than a short one, matching real webtoon
  reader behavior).
- **Render layer**: decode/dispose/draw, agnostic to what produced the page list. Adding a future
  reading mode is a layout-model change, not a render-layer change — unchanged from §8's original
  framing.

## 3. Decode/virtualization service

Replaces `PageImageDecoder` (today: synchronous, UI-thread, single dictionary cache, fixed ±1
window, always full native resolution — fine for one page at a time, not for a scrolling stack of
them).

- **Two-tier bitmaps.** Display tier: every visible/near-visible page decoded and downsampled to
  viewport width — used for both paged and continuous mode, ~95% of real usage including full
  webtoon scroll (never holding native resolution during scroll). Detail tier: on-demand high-res
  crop, decoded only once zoom exceeds what the display tier supports, discarded the moment zoom
  settles back down. Matches onboarding.md §8's original two-tier design unchanged.
- **Virtualization window**: current position ±2 pages kept decoded (matching the constant
  `PageImageDecoder.TrimCache` already uses for paged mode, now shared). Anything outside is
  disposed *immediately*, not left for the GC — the documented mitigation for Avalonia issue #18498
  (Skia native bitmap memory isn't GC-tracked; `Bitmap.Dispose()` is the only thing that frees it
  promptly).
- **Background decode**: `System.Threading.Channels`, one bounded channel per priority tier (near-
  viewport = high priority, prefetch-ahead = low priority) — the threading primitive already
  resolved in docs/open_items_resolved.md, now actually built. Decode runs off the UI thread;
  results marshal back via the same channel-consumer pattern.
- **GPU resource cache**: `SkiaOptions.MaxGpuResourceSizeBytes` raised at Avalonia startup
  (`AppBuilder` config) from the ~28MB default to 384MB (middle of onboarding.md §8's suggested
  256–512MB range) — comic/webtoon pages routinely exceed the default trivially. Periodic
  `SKGraphics.PurgeResourceCache()` (on a timer, e.g. every 30s while the Reader is open) as a
  backstop.
- **Memory-bound test**: an integration-style test that opens a long synthetic webtoon issue (50+
  tall pages) and scrolls through it programmatically, asserting the live decoded-bitmap count
  never exceeds the virtualization window size — the concrete, checkable form of "treat decoded-
  bitmap count as a hard-bounded resource" from onboarding.md §8.

## 4. Rendering — unified `CompositionCustomVisualHandler`

`PageCanvas` (Control) hosts a `CompositionCustomVisualHandler`-backed visual instead of overriding
`Render`/using `context.Custom`. The handler owns:
- Drawing the current layout-model page list (§2) each compose pass, applying zoom/pan/rotation/
  fit-scale exactly as `ReaderPageDrawOperation` does today (that math — `ZoomPanMath.
  ComputeBaseScale`/`ClampPan`/rotation composition — is unchanged and reused, only the drawing
  entry point moves).
- Applying the live image-adjustment color filter (§7) as a paint-level `SKColorFilter`, not a
  pixel-level bitmap mutation — cheap enough to run every compose pass without redecoding.
- Drawing the magnifier overlay (§6) as a final pass on top, when visible.

Input handling (pointer/wheel/keyboard) stays on `PageCanvas` itself via Avalonia's normal routed
events — `CompositionCustomVisualHandler` only replaces the *drawing* mechanism, not input, so
`OnPointerPressed`/`OnPointerWheelChanged`/`OnKeyDown` etc. keep their current shape, extended for
scroll-axis input (§5). Zoom/pan transform is applied at composition/present time, decoupled from
decode, per onboarding.md §8's original framing — render a display-tier bitmap somewhat larger than
strictly needed, pan/zoom cheaply via transform, only request a fresh detail-tier decode once
interaction settles (debounced).

**Exact Composition-API mechanics** (visual creation, compose-pass scheduling, how a
`CompositionCustomVisualHandler` receives its draw data from the UI-thread `PageCanvas` across the
thread boundary) are resolved while writing the code, not pinned here — same precedent as the
rotation transform API in the prior spec.

## 5. Scroll input and zoom in continuous mode

- Plain wheel/touch-drag/two-finger scroll becomes document scroll along the reading axis (not
  page-turn — there's nothing to turn). Arrow keys scroll by a step; Page-Up/Down or Home/End jump
  further, matching common scroll-reader convention.
- **No fit-mode picker in continuous mode** — base scale always fills viewport width (vertical
  mode) or height (horizontal mode), the only coherent base for a stacked flow. The fit-mode
  toolbar control hides when the active `ReadingMode` is one of the continuous modes.
- **Zoom is free and unclamped upward from that base**, layered on top exactly like paged mode's
  existing zoom: the existing ctrl+wheel/pinch gesture, *plus* a new toolbar zoom slider (not just
  the existing −/%/+ presets) so continuous mode has a direct, non-gestural way to zoom, per the
  user's explicit ask. Reuses `ZoomPanMath` unchanged — same clamp range, same anchor-to-cursor
  behavior — no new math.
- Click-drag pan works the same as paged mode once zoomed past the point where the page overflows
  the viewport in the cross-axis (`ZoomPanMath.HasOverflow`, already exists).

## 6. Position tracking and persistence

- **"Current page" in continuous mode** = whichever page's midpoint is nearest the viewport center,
  recomputed on every scroll-position change (cheap — the layout model already knows each visible
  page's rect). This is genuinely equivalent to CE-less `PageCount`/current-index tracking the user
  asked for directly — no schema change, reuses `Issue.LastPageRead` exactly as paged mode already
  does, throttled to avoid a `SaveChanges` per scroll-frame (same debounce shape as existing
  autosave-on-navigate code).
- `PageLabel` ("PAGE X / Y") stays driven off that same nearest-page value, so it keeps updating
  live during scroll with zero new UI — it's already always-visible in the toolbar.
- Bookmarks / in-book search hits / thumbnail-rail clicks: in paged mode these currently do a page-
  index jump; in continuous mode they instead scroll the target page's top edge into view (smooth-
  scroll, not instant jump — matches the "continuous" feel).

## 7. Fullscreen + minimal-chrome + overlays

- **One toggle**, not two independent controls — CE's `FullScreen` setter directly drives
  `MinimalGui`, confirmed from source; there's no CE precedent for a windowed "hide chrome without
  fullscreen" mode, so this doesn't invent one. **F and F11, not CE's F-key-and-double-click
  pair**: double-click on the page is already bound to zoom-toggle in the shipped gestures spec
  (`PageCanvas.OnPointerPressed`, `e.ClickCount == 2` → `ToggleZoom`) — reusing it for fullscreen
  would silently break that existing, tested behavior. F11 is added alongside CE's F key since it's
  the standard OS/desktop-app fullscreen convention Windows users expect, independent of CE parity.
  Named deviation from CE, driven by a real conflict with already-shipped Paperbunkr behavior plus a
  platform-convention addition, not CE's own design.
- Entering fullscreen: OS-level fullscreen (Avalonia `WindowState`/`SystemDecorations`) + the
  Reader's toolbar and thumbnail rail collapse.
- **Overlays are a separate layer from chrome** — they still render in fullscreen/minimal-chrome
  mode, matching CE's `InfoOverlays` being independent of `MinimalGui`:
  - **Status text**: the existing `PageLabel`, now also rendered as an on-canvas overlay (not just
    toolbar text) so it's visible when the toolbar is collapsed.
  - **Scrubber**: a horizontal page-browser strip (thumbnail-sized tiles, click or drag to jump/
    scroll to a page) — the closest Paperbunkr equivalent to CE's `InfoOverlays.PageBrowser`,
    reusing the same thumbnail bitmaps `PageImageDecoder.GetThumbnail`/its §3 successor already
    produces for the existing (non-fullscreen) thumbnail rail.
  - Both auto-hide after a few seconds of cursor idle while in fullscreen, reappearing on mouse
    move — matching CE's `AutoHideCursor`-driven UX pattern, not idle guessing.
- **Named deviations**: CE's `InfoOverlays.PartInfo` (no real "Parts" concept in Paperbunkr issues)
  and `CurrentPageShowsName` (page filename overlay) are skipped — real CE flags, deliberately not
  ported, not oversights.
- Persistence: fullscreen state is session-only (matches CE — not part of `DisplayWorkspace`'s
  persisted fields either). Whether the scrubber overlay is shown-by-default is a new `AppSettings`
  field (§9), since the Preferences Reader tab now exists to hold it.

## 8. Magnifier

- Cursor-following zoomed loupe, shown while right-click-holding (matching CE's
  `panMagnifier`/drag-triggered visibility) — reads whichever bitmap (display or detail tier) is
  already decoded for the page under the cursor, crops/scales a region around the cursor position,
  and draws it as a circular overlay in `PageCanvas`'s final composition pass (§4). No separate
  decode path — it's a view into data the render layer already has.
- Defaults matching CE's own field-initializer defaults (confirmed from `ComicDisplayControl.cs`,
  not guessed): zoom **2.0x**, opacity **1.0** (fully opaque), size **200×200px**. These become new
  `AppSettings` fields (Reader tab), not fixed constants — unlike the original fit-mode pass, a
  Preferences Reader tab now exists (shipped after that spec was written), so there's no reason to
  ship a magic constant only to migrate it later.
- **Named deviation**: one frame style only (a simple circular border), not CE's `MagnifierStyle.
  Glass`/`Simple` pair — porting CE's actual glass-bevel bitmap assets isn't warranted for a polish
  pass. `AutoMagnifier`/`AutoHideMagnifier` (CE's hover-without-clicking auto-show behavior) also
  skipped — the explicit right-click-hold trigger is simpler and sufficient.

## 9. Live image adjustment

- **Brightness / Contrast / Saturation / Gamma**, each −100..100 matching CE's exact
  `PreferencesDialog` trackbar range (confirmed from `.Designer.cs`), sliders in a small panel
  toggled from the reader toolbar (same "extend existing chrome" pattern as the fit-mode/zoom
  controls, not a new full-screen panel).
- **Formula**: ported from `ImageProcessing.CreateColorMatrix`/`ApplyAdjustment` — contrast/
  brightness expressed as a scale+offset matrix, saturation as a separate matrix, both composed
  (CE's `CreateColorScaleMatrix(...) * CreateColorSaturationMatrix(...) * CreateColorWhitePointMatrix(...)`,
  minus the whitepoint term per the deviation below), applied via SkiaSharp's
  `SKColorFilter.CreateColorMatrix` at draw time (§4) — a paint-level filter, not a pixel buffer
  mutation, so it updates live as sliders move with no redecode. Gamma is applied as a second pass
  (CE does the same — gamma is a LUT remap, not part of the matrix); the exact Skia-side gamma
  mechanism (a second color filter vs. a runtime shader) is resolved while coding.
- **Persistence**: additive global + per-book, mirroring CE's own `BitmapAdjustment.Add(BaseColorAdjustment,
  Comic.ColorAdjustment)` exactly. Global defaults live on `AppSettings` (four new fields, default
  0 = no adjustment, matching CE's `BitmapAdjustment.Empty`). Per-book override: four new nullable
  `float` columns on `Issue` (`BrightnessOverride`/`ContrastOverride`/`SaturationOverride`/
  `GammaOverride`), same nullable-override shape as `PageFitModeOverride`. Effective value =
  `global + (perBookOverride ?? 0)` per channel, additive like CE.
- **Named deviations**: Sharpen (real convolution-kernel cost per adjustment change — a genuine
  perf concern for a live slider, unlike the cheap color-matrix path above), AutoContrast
  (per-page histogram black/white-point scan), and WhitePoint tint — all real `BitmapAdjustment`
  fields, all skipped as heavier than a polish pass warrants.

## 10. Background and margins

- `ImageBackgroundMode`: **Auto** and **Color** only (CE's actual default is `Color`, confirmed
  from `DisplayWorkspace.cs`) — `Texture` (image-tile background) skipped as a named deviation,
  needs bundled texture assets or a file-picker surface bigger than this pass. "Auto" here means
  Paperbunkr's existing theme-driven canvas background (today's unstated default); "Color" lets the
  user pick a solid `BackColor`, CE default `WhiteSmoke` (confirmed, not guessed).
- `PageMargin` (bool, CE default `false`) + `PageMarginPercentWidth` (float, CE default `0.05`) —
  ported as-is, shrinks the effective render scale so the page doesn't touch the canvas edges,
  applied in the same fit-scale computation `ReaderPageDrawOperation`'s successor already does.
- **Global-only** — no per-Issue override. Background/margin is a personal viewing preference, not
  a per-book concern (unlike fit-mode, where page dimensions genuinely vary issue-to-issue). New
  `AppSettings` fields: `ImageBackgroundMode`, `BackgroundColor` (hex string), `PageMarginEnabled`,
  `PageMarginPercentWidth`.

## 11. Schema summary (one EF migration)

- `Issue`: `BrightnessOverride`, `ContrastOverride`, `SaturationOverride`, `GammaOverride` (all
  `float?`).
- `AppSettings`: `MagnifierZoom` (double, default 2.0), `MagnifierOpacity` (double, default 1.0),
  `MagnifierSizePixels` (int, default 200), `DefaultBrightness`/`DefaultContrast`/
  `DefaultSaturation`/`DefaultGamma` (double, default 0), `ImageBackgroundMode` (enum, default
  `Color`), `BackgroundColor` (string, default `"WhiteSmoke"`), `PageMarginEnabled` (bool, default
  false), `PageMarginPercentWidth` (double, default 0.05), `ShowScrubberOverlay` (bool, default
  true — the scrubber is the primary continuous-mode navigation aid, on by default unlike CE's
  opt-in `InfoOverlays.None` default, since CE has toolbar-based page nav as a fallback that
  Paperbunkr's fullscreen mode deliberately hides).

## 12. UI summary

- Reader toolbar: fit-mode picker hides in continuous modes (§5); zoom control gains a slider
  alongside its existing −/%/+ presets; new magnifier toggle button; new "Adjust" button opening
  the brightness/contrast/saturation/gamma panel (§9).
- Fullscreen toggle: F and F11, per §7's deviation from CE's double-click binding; no new visible
  control strictly needed (CE treats this as a keyboard action, not menu-hunted), though a toolbar
  icon is reasonable to include for discoverability.
- New on-canvas overlay layer: status text + scrubber strip (§7), magnifier loupe (§8).
- Preferences → Reader tab gains: magnifier zoom/opacity/size, default adjustment values,
  background mode/color, page margin toggle/width, scrubber-overlay-shown-by-default.

## 13. Testing

- `ZoomPanMath`: unchanged, already covers fit/zoom/pan math reused here as-is.
- New pure-function tests: continuous-mode layout model (given a set of page aspect ratios + scroll
  offset, correct visible-page list and each one's rect, both axes); nearest-page-to-viewport-center
  calculation; the ported color-matrix formula (spot-check against CE's known outputs for a few
  brightness/contrast/saturation combinations).
- Decode/virtualization service: the memory-bound test from §3 (decoded-bitmap count never exceeds
  the window during a long synthetic scroll); background-channel priority ordering (near-viewport
  requests complete before far-prefetch ones under contention).
- `ReaderScreenViewModel`: per-book adjustment override write/read-back (mirrors the existing
  `PageFitModeOverride` test shape); nearest-page write to `LastPageRead` during simulated
  continuous scroll, throttled correctly.
- New EF migration: standard round-trip test, existing rows come back with all new columns at
  their defaults.
- **Manual-only, same standing caveat as every prior reader spec**: actual on-screen continuous
  scroll smoothness/memory behavior over a real long webtoon file, magnifier rendering, live
  adjustment slider feel, and fullscreen/overlay auto-hide timing all need eyes-on verification —
  no unattended desktop GUI automation available for this project. Given this pass's size, manual
  verification should specifically include watching memory (Task Manager or a profiler) across an
  extended scroll session, not just confirming pages render.

## 14. Explicitly not in this pass

Double-page spread layout + split-page navigation (already partially designed in
docs/open_items_resolved.md §4, but a distinct layout-model change, not bundled here), page-
transition animations (CE's actual animation config was already retired as a GDI+-era artifact per
docs/open_items_resolved.md §5 — revisiting this needs a fresh design, not a port), touch gestures
beyond what's shipped, remappable shortcuts (real CE subsystem — `KeyboardShortcutEditor`/
`KeySequence` — but app-wide input plumbing, not reader-canvas rendering), and auto-scroll (CE's
`AutoScrollMode` is `Off/Pan/Drag` — mouse-driven auto-pan, not a timed auto-advance reader mode;
needs its own confirmation of what Paperbunkr should actually do here before scoping).
