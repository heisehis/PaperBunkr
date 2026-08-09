# Reader Gestures (Zoom/Pan/Wheel/Touch) + Grid Arrow-Key Navigation

*Date: 2026-08-09. Follow-up to the P5 keyboard-navigation pass
(docs/alpha-roadmap.md) — P5 covered Tab order, Enter/Space/Esc, and the Reader's remappable
page-turn keys, but left two real gaps: Library cards and Detail issue tiles only support
one-dimensional Tab order (no spatial arrow-key movement through the grid), and the Reader has
zero mouse-wheel, trackpad, or touch handling at all beyond the existing two-zone click/keyboard
page-turn. This spec closes both, and pulls forward a minimal zoom/pan feature (previously
Beta-backlog, unbuilt) since trackpad pinch-zoom needs something real to control.*

## 1. Scope decision

Scoped to **paged mode (LTR/RTL) only**. During design, the user's actual day-to-day reading habit
turned out to be Continuous Vertical / Continuous Horizontal (RTL and LTR) / Webtoon — none of
which have any rendering pipeline today. `ReadingMode.VerticalContinuous`/`HorizontalContinuous`
exist only as a display label in `ReaderScreenViewModel.ReadingModeLabel`'s switch statement;
`PageCanvas`/`ReaderPageDrawOperation` always render exactly one letterboxed page regardless of
the series' reading mode. Building continuous/webtoon scroll is a separate, previously-flagged
"highest-risk, genuinely new (not CE parity)" project on its own (onboarding.md §8's
memory-management warning about keeping many decoded pages alive in a scrolling strip) — it needs
its own brainstorm for the rendering pipeline before any gesture/navigation model on top of it
makes sense, including the zoom-at-page-edge behavior (zoom-out-forces-next-page vs.
stays-zoomed-until-manually-reset) the user described from other readers they use. That follow-up
is explicitly out of scope here.

Also explicitly out of scope: CE's fit-mode presets (Original/Fit All/Fit Width/Fit Height/Best
Fit) — zoom here is simple continuous zoom only, no mode picker. Hardware media keys (mentioned
alongside touch gestures in the original CE-feature-inventory audit line) — niche, inconsistent
cross-platform Avalonia support, low value for the effort.

## 2. Grid keyboard navigation (Library cards + Detail issue tiles)

Both `LibraryScreen`'s cover grid and `DetailTabs`' Issues tab use `ItemsControl` + `WrapPanel`,
which wraps based on available window width — there's no fixed column count, so "move down a row"
can't be computed as `index + N`. `GridKeyboardNavigation` (new static class, `Paperbunkr.App.Views`,
alongside `PageCanvas`) derives row membership from the realized item containers' actual `Bounds`
at key-press time:

- **Left/Right** — previous/next item in list order, clamped at the ends.
- **Up/Down** — group realized containers by `Bounds.Y` to find the next/previous row, then pick
  the item in that row whose `Bounds.X` is closest to the current item's — keeps roughly the same
  visual column when moving vertically, matching file-explorer-style grid navigation.
- **Home/End** — jump to first/last item.

The row/column math itself is pure — it operates on `(item, bounds)` pairs, not live controls, so
it's unit-testable without a real Avalonia visual tree. A thin per-screen wrapper extracts bounds
from the real `ItemsControl`'s realized containers and calls into it.

Detail issue tiles already have a `Focusable` `Border` + `KeyDown` handler from P5
(`OnIssueTileKeyDown`, currently Enter/Space only) — it gains arrow-key delegation to
`GridKeyboardNavigation`. Library cards are already real `Button`s (Tab/Enter/Space free from
FluentTheme) but have no code-behind file yet; `LibraryScreen.axaml.cs` gains one, following the
same per-item `KeyDown`-handler pattern.

## 3. Reader zoom & pan

State lives on `ReaderScreenViewModel`: `ZoomLevel` (`double`, default `1.0`, clamped to
`[1.0, 4.0]`) and `PanOffsetX`/`PanOffsetY` (`double`, default `0`) — same VM-owns-state,
`PageCanvas`-just-renders-it shape as `CurrentPage`/`HighQualityPageDisplay`/`PageTurnLeftKey`.
`PageCanvas` gets matching two-way-bindable `StyledProperty`s so gestures write straight back
through the binding; no separate transient-drag-state layer to keep in sync with the VM.

**Trackpad pinch and Ctrl+wheel are the same code path.** Windows' Precision Touchpad driver
translates a two-finger pinch into synthesized `PointerWheelChanged` events with `Ctrl` held — the
same event a physical Ctrl+scroll produces. So zoom input is just: `PointerWheelChanged` with
`KeyModifiers.Control` → adjust `ZoomLevel` (clamped). No separate pinch-gesture handling needed.

**Plain wheel / two-finger scroll (no Ctrl) is zoom-state-dependent:** at `ZoomLevel == 1.0` it
page-turns (down/right = forward, up/left = back, matching existing spatial semantics); once
zoomed in, plain wheel pans instead.

**Click-drag pans only when zoomed.** At `ZoomLevel == 1.0`, `PointerPressed`/click-zone behavior
is exactly as today, unchanged. Once zoomed in, press-and-drag pans instead, clamped so the image
can't be dragged past its own edges (computed from `ZoomLevel` + canvas size + image size). Arrow
keys follow the same rule — page-turn when unzoomed, pan when zoomed (this makes Up/Down
meaningful in the Reader for the first time).

**Zoom toggle:** double-click (`PointerPressed.ClickCount == 2`, checked *before* the existing
single-click zone logic so a double-click's first click doesn't also fire a page-turn) zooms in to
a fixed ~2x centered on the click point when unzoomed, and resets to fit (`ZoomLevel = 1.0`,
`PanOffset = 0,0`) when already zoomed. One gesture serves both directions.

**Resolved gap:** while zoomed, click/wheel/arrows all pan rather than page-turn — the existing
bottom scrubber's ◀/▶ buttons (plain `Button`s bound to `GoLeftCommand`/`GoRightCommand`, already
unaffected by zoom/pan state) remain an always-available page-turn path. No new UI needed.

**Reset on navigation:** `Load()` resets `ZoomLevel`/`PanOffset` to default whenever a new
issue/page loads, so a page turn never lands already zoomed/panned to an arbitrary spot.

## 4. Touch tap-zones

CE's own source has no literal "9-zone" scheme — `CommandKey.TouchTap`/`TouchDoubleTap` exist, but
the actual zone semantics were proposed fresh during the CE-feature-inventory audit ("not
previously named even implicitly"), likely drawing on the Mihon/Komikku-style tap-navigation this
project already takes inspiration from. Semantics designed here, not ported:

- Touch input only (`PointerPressedEventArgs.Pointer.Type == PointerType.Touch`) gets a 3×3 zone
  grid over the canvas; mouse keeps today's 2-zone left/right split unchanged. An unrecognized
  `Pointer.Type` falls back to the existing mouse behavior.
- **Left column** (all 3 rows) → `LeftCommand`. **Right column** → `RightCommand`.
- **Center column** (top/middle/bottom) → reserved, no-op. The natural target is "toggle
  chrome/menu," but Paperbunkr has no minimal-chrome/fullscreen mode yet (separate unbuilt
  Beta item) — no real action to wire to today. Zone geometry doesn't need to change later, just
  what it calls.
- **Double-tap** → same zoom toggle as mouse double-click (§3), for cross-input consistency.
- **Flick/swipe** → a fast horizontal touch-drag maps to the same page-turn as
  wheel/arrow-keys-when-unzoomed.

## Testing

- `GridKeyboardNavigation`: unit tests against synthetic `(item, bounds)` layouts — Left/Right/Up/
  Down/Home/End produce the expected target item across wrapped-row boundaries, including
  ragged last rows (fewer items than a full row).
- `ReaderScreenViewModel`: `ZoomLevel`/`PanOffset` clamping at their bounds; reset to default on
  `Load()`; double-click-to-zoom-in and double-click-to-reset toggle correctly; pan is disabled
  (click-zones behave as today) at `ZoomLevel == 1.0`.
- Manual verification (same no-GUI-automation approach as prior specs): Tab into a Library/Detail
  grid and confirm arrow keys move spatially with wraparound; Ctrl+scroll and a real trackpad
  pinch both zoom; plain scroll page-turns unzoomed and pans zoomed; drag-to-pan clamps at image
  edges; double-click zooms in and back out; the scrubber ◀/▶ still turns pages while zoomed.
