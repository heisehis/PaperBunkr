# Reader: Page Transition Animations

*Date: 2026-08-13. Closes the last open item from the Reader polish Beta backlog (docs/alpha-todo.md's
"Bonus, ahead of schedule" section; docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-
chrome-overlays-design.md §14 explicitly deferred this, since CE's own transition settings were
GDI+-era artifacts needing a fresh design, not a port — docs/open_items_resolved.md §5).*

## 1. Scope

CE (`_reference/ComicRackCE`) was checked directly rather than assumed, per the standing CE-parity
rule. Its real page-turn transitions live in `ComicDisplayControl.cs`'s `BlendAnimationHandler`
delegates — `FadeInBlending` (crossfade), `ScrollToLeftBlending`/`ScrollToRightBlending`/
`ScrollToTopBlending`/`ScrollToBottomBlending` (directional slide), gated by `BlendWhilePaging`
(default **false**) and throttled so a blend is skipped entirely if the previous one finished less
than 100ms ago (`ShouldPagingBlend = BlendWhilePaging || Machine.Ticks - lastBlend > 100`) — CE
itself deliberately drops the animation during rapid paging rather than stacking or queuing it.
Separately, `EngineConfiguration.PageBow*` is a static thumbnail-rendering decoration (a drawn
"curled corner" bitmap composited onto thumbnail images), never an actual turn animation — confirmed
from source, not assumed, which settles the "is page-curl in scope" question: it never was a
transition to begin with, so there's nothing to port and nothing to redesign. Page-curl is out of
scope here.

This pass ships:
- Two real transition styles — **Slide** (spatial, direction-aware) and **Crossfade** — plus **None**.
- User-configurable via Preferences → Reader (style dropdown + duration slider), off by default.
- Applies only to adjacent-page navigation in paged reading modes (`LeftToRight`/`RightToLeft`) —
  arrow keys, click-zones, touch flick, and the scrubber's ◀/▶ buttons, all of which already funnel
  through `PageCanvas.LeftCommand`/`RightCommand`. Jumping (thumbnail click, any future "go to page")
  snaps instantly — CE's own blenders were paging-specific too, never used for arbitrary jumps.
- Rapid-paging throttle: same principle as CE's `ShouldPagingBlend`, keyed off the now-configurable
  duration instead of a fixed 100ms.

Explicitly not in scope: continuous/webtoon modes (already continuous motion, per the prior spec's
own carve-out — nothing to add), double-page spread (not implemented yet), and the Novels PDF reader
(shares `PageCanvas` but never binds the new properties, same non-pattern every prior reader-only
addition here has followed).

## 2. Data model

Two new `AppSettings` columns, one migration (`AddPageTransitionSettings`):

- `PageTransitionStyle` (new enum `PageTransitionStyle { None, Slide, Crossfade }`, default `None`) —
  not a CE setting directly (CE split style across `Blender` selection + a separate `BlendWhilePaging`
  on/off flag); collapsed into one setting here since "which style" and "on or off" are the same
  question from the user's side. `None` needs `HasSentinel` treatment matching `DefaultPageFitMode`'s
  precedent (docs/superpowers/specs/2026-08-10-preferences-reader-tab-design.md §2) — it's the enum's
  first/CLR-default value, ambiguous with "unset" on insert to `AppSettings`'s single-row table.
- `PageTransitionDurationMs` (`int`, default `250`) — CE parity in spirit (`AnimationDuration` 250-300,
  `BlendDuration` 400 depending on version), not a literal port of either specific field since they
  governed different things there (general display-state animation vs. page-blend specifically) that
  this design collapses into one duration. UI range 100–600ms.

New enum file `src/Paperbunkr.Data/Entities/PageTransitionStyle.cs`, matching `ImageFitMode.cs`'s
shape (plain enum, doc comment pointing back at this spec).

## 3. Mechanism

### 3.1 Trigger heuristic (`PageCanvas`)

Rather than threading a "was this a turn or a jump" signal through `ReaderScreenViewModel` and its
bindings, `PageCanvas` uses what it already knows: adjacent navigation in paged mode *only* ever
happens by this control invoking its own `LeftCommand`/`RightCommand` (`TryExecute` in
`OnPointerReleased`'s flick handler, `OnPointerWheelChanged`'s plain-wheel branch, `OnKeyDown`,
`InvokeTouchZone`/`InvokeZoneCommand`, `OnPinchEnded`'s two-finger-drag). Thumbnail clicks and any
future jump-to-page control call `ReaderScreenViewModel.GoToPage` directly, bypassing these commands
entirely.

`PageCanvas` records which direction (`Left`/`Right`) it just invoked in a private field immediately
before calling `TryExecute`. When the resulting `Page` bitmap swap arrives via `OnPropertyChanged`,
a pending direction means "this is an adjacent turn" — build a transition message using that
direction and clear the field. No pending direction (or the flag was already cleared) means an
instant swap, exactly like today. The field is also cleared whenever `DecoderProperty` changes
(alongside the existing `_knownPageSizes.Clear()`) — so paging off the last page into the next issue
via `NavigateToAdjacentIssue` never animates as if it were a same-book page turn; a full issue load
changing everything underneath is a different event, not a turn.

This gets RTL correctness for free — direction is keyed off which spatial command fired, the same
"Left"/"Right" naming `PageCanvas` already uses everywhere else (docs/superpowers/specs/
2026-08-07-reader-rtl-navigation-design.md §3), not off page-index arithmetic that would need its
own RTL-aware sign-flip.

### 3.2 Animation (`ReaderPageVisualHandler`)

Confirmed via reflection against the actual Avalonia 12.1.1 assembly this project targets (not
assumed): `CompositionCustomVisualHandler` (which `ReaderPageVisualHandler` already extends, shared
with continuous mode's rendering per docs/superpowers/specs/2026-08-10-reader-polish-continuous-
scroll-chrome-overlays-design.md §1/§4) exposes `RegisterForNextAnimationFrameUpdate()`,
`OnAnimationFrameUpdate()`, and a `CompositionNow` clock — a real per-frame hook on the compositor
thread, not the UI thread. This is the mechanism, not a `DispatcherTimer`.

New message type `ReaderPageTransitionData(Rect Bounds, Bitmap? OldBitmap, Bitmap? NewBitmap, bool
HighQuality, double Zoom, double PanOffsetX, double PanOffsetY, ImageFitMode FitMode, bool
FitOnlyIfOversized, int RotationDegrees, PageTransitionStyle Style, TimeSpan Duration,
PageTransitionDirection Direction)`, sent instead of `ReaderPageVisualData` when `PageCanvas`
determines an animated turn applies. `PageTransitionDirection` (`Left`/`Right`) is a small internal
enum private to `Views` (alongside the other internal records in `ReaderPageVisualHandler.cs`) — not
persisted, not the same type as §2's `PageTransitionStyle`, which is the one `AppSettings` column and
Preferences control. Both bitmaps render using the *current* (already-updated)
zoom/pan/fit/rotation state, computed independently against each bitmap's own pixel size — same as
today's single-bitmap path, just run twice. This deliberately does not attempt to preserve each
page's own historical transform (irrelevant in the by-far-common case where fit/zoom/rotation don't
change mid-turn, and not worth the complexity for the rare case where e.g. `ResetZoomOnPageChange`
also fires — CE doesn't do this either, its blenders reuse one `DisplayOutput` framing for both).

`ReaderPageVisualHandler.OnMessage` stamps `CompositionNow` as the transition's start time, stores
the message, and calls `RegisterForNextAnimationFrameUpdate()`. `OnAnimationFrameUpdate` computes
`progress = clamp((CompositionNow - start) / Duration, 0, 1)`, calls `Invalidate()`, and re-registers
for another frame while `progress < 1`. `OnRender` branches on `Style`:

- **Slide**: both bitmaps drawn at their normal fit-computed `destRect`, each additionally offset
  along the main axis by `(1 - progress) * Bounds.Width` (incoming) and `-progress * Bounds.Width`
  (outgoing) for a `Right` turn, mirrored for `Left` — the new page enters from the edge matching the
  spatial direction pressed, the old page exits the opposite edge, both moving together (not a
  cross-fade-while-sliding hybrid).
- **Crossfade**: both bitmaps drawn at their own normal (unshifted) `destRect`, outgoing painted with
  alpha `1 - progress`, incoming with alpha `progress`, via the same `SKPaint`/color-filter-adjacent
  draw path `RenderPaged` already uses for live image adjustment (an `SKPaint` with alpha instead of
  a color filter, same `ISkiaSharpApiLeaseFeature` lease).

Once `progress` reaches 1, the handler drops the transition state and treats the new bitmap as
`_pagedData` going forward — identical to the non-animated path (a resize or zoom change immediately
after a completed turn re-renders normally, no lingering transition state).

### 3.3 Rapid-paging throttle

`PageCanvas` records the wall-clock time it last *started* an animated transition. When a new page
turn's direction is about to be sent as `ReaderPageTransitionData`, it only does so if
`(now - lastTransitionStart).TotalMilliseconds >= PageTransitionDurationMs` — i.e., the previous
animation has already finished (or none is in flight). Otherwise it falls back to the plain,
non-animated `ReaderPageVisualData` push, exactly like a jump. This mirrors CE's own
`Machine.Ticks - lastBlend > 100` throttle in principle (skip rather than queue/interrupt), keyed off
the configurable duration instead of CE's fixed 100ms constant. Holding the next-page key stays snappy
instead of every turn queuing another 200–600ms animation behind the last.

## 4. Preferences wiring

Follows `DefaultPageFitMode`'s established shape exactly (docs/superpowers/specs/
2026-08-10-preferences-reader-tab-design.md §3): a `ComboBox` + `SelectedItem` two-way binding +
changed-hook for `PageTransitionStyle` (`None`/`Slide`/`Crossfade`), plus a plain `[ObservableProperty]`
+ `On*Changed` hook (matching `MouseWheelSpeed`'s numeric-setting shape) for
`PageTransitionDurationMs`, backed by a `Slider` (100–600, step 50). Both live in the Reader tab's
existing "Display" group. `ReaderScreenViewModel.Load` reads both from `AppSettings` into two new
bindable properties; `PageCanvas` gains matching `PageTransitionStyle`/`PageTransitionDurationMs`
styled properties (`ReaderScreen.axaml` binds them same as every other Reader-tab-backed property).
These are read directly by `PageCanvas` at turn-time rather than added to `RenderAffectingProperties`
— changing the setting mid-session doesn't need to force an immediate rerender, only the *next* page
turn needs to see the new value, same non-reactive-read shape `WheelPanStep` already uses.

## 5. Testing

- New pure-function/model tests (wherever the slide/crossfade offset-and-alpha math for a given
  `progress` lives — kept as a small static helper, same shape as `ZoomPanMath`/`ImageAdjustmentMath`,
  so it's testable without a real compositor): offset direction for `Left`/`Right`, alpha values at
  `progress` 0/0.5/1, clamping outside [0,1].
- `PageCanvas`-level: the trigger heuristic (direction recorded before `TryExecute`, cleared on
  `Decoder` change, jump via `GoToPage` doesn't set a pending direction) — testable via Avalonia's
  headless test harness, same pattern this codebase already uses for `PageCanvas` gesture tests.
- Rapid-paging throttle: a synthetic pair of turns closer together than `PageTransitionDurationMs`
  falls back to the instant path; the same pair spaced further apart animates both.
- `PreferencesScreenViewModelTests`: `EnsureLoaded_Populates*FromAppSettings` +
  `Toggling*_PersistsToAppSettings` pairs for both new settings, matching every other Reader-tab
  setting's existing test shape.
- New EF migration: standard round-trip test, existing `AppSettings` row comes back with both new
  columns at their defaults.
- **Manual-only, same standing caveat as every prior reader spec**: actual on-screen animation
  smoothness/timing, RTL direction correctness, and the rapid-paging throttle's real feel all need
  eyes-on verification — no unattended desktop GUI automation available for this project.

## 6. Explicitly not in this pass

Page-curl (never a real CE transition, see §1). Continuous/webtoon-mode transitions (already
continuous motion). Double-page spread interaction (not implemented). Per-direction style overrides
(CE let `Blender` differ by direction in principle; one style setting covers both directions here,
consistent with keeping this a two-control Preferences surface, not four). Interrupting/re-targeting
a still-running animation mid-flight when a new turn arrives faster than the throttle window — it
falls back to an instant swap instead (§3.3), not an attempt to blend three frames together.
