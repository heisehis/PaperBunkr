# Home Screen & NavRail — Visual v2 (Icons + "Kinetic Rail" + Theme Fix)

**Date:** 2026-09-08
**Status:** Design approved, plan pending.

## Background

Three things, brainstormed together in one session (the third was flagged after the first two were
already designed, and folds cleanly into the masthead work already planned there):

1. **NavRail icon overhaul.** The rail (`MainWindow.axaml:182-276`) currently has Library and Books
   sharing the identical `Symbol="Book"` glyph (no differentiation), and Continuity uses
   `Symbol="Star"` (reads as favorites/rating, not story-continuity — there's no ComicRack CE prior
   art for "Continuity" at all, it's Paperbunkr-original, so this was free to pick on Fluent's own
   semantics). Confirmed directly against the real `FluentIcons.Common.Symbol` enum (grepped the
   installed package DLL, then rendered every candidate through a real headless-Avalonia
   `SymbolIcon` — not guessed from names) that `Symbol.Planet` exists and is a literal ringed-planet
   glyph, and `Symbol.Library` / `Symbol.BookOpen` exist as clean, already-distinct icons.
2. **Home screen "deeper visual v2."** Home already went through one full visual redesign on
   2026-08-28 ([2026-08-28-home-screen-redesign-design.md](2026-08-28-home-screen-redesign-design.md))
   — cover-wall masthead, `DetailHero`-based spotlight, chromatic-split ("misprinted comic")
   headings via the `SplitText` control, cover-forward `PosterTile` shelf cards. This spec pushes
   that further in one coherent pass, chosen (of three mocked directions) specifically to lean into
   the "fluid/reactive" language this project's own
   [2026-08-24-design-language-foundation-design.md](2026-08-24-design-language-foundation-design.md)
   already uses to describe the nav/motion system — i.e. this is a continuation of an existing
   pillar, not a new one.
3. **Masthead isn't actually skin-aware — a real bug, not a preference.** The user reported Home
   "looks off" on the `windows_11` skin. Root-caused, not guessed: `windows_11` is the only one of
   the 5 skins with a light background (`bg`/`surface0` `#F3F3F3`, `text` `#1A1A1A`) — the other 4
   (`default`, `cool_technical`, `vibrant_pop`, `vintage_paperback`) are all dark. The masthead
   backdrop is never actually theme-reactive: `CoverWallRenderer.cs` bakes a fixed near-black scrim +
   vignette straight into the rendered bitmap via raw SkiaSharp `SKColor` literals (`SKColor(8, 8,
   10, 205)` etc. — pixel data, no resource system involved), and the `HomeScreen.axaml:142-146`
   fallback `RadialGradientBrush` (shown when there's no cover-wall yet) hardcodes `#241812`/
   `#0C0B0A`/`#060606` instead of a skin token. On the 4 dark skins this coincidentally blends in. On
   `windows_11` it's a genuine contrast failure, not just a mismatched vibe: "Your Library" renders
   in `PbTextBrush`, which correctly resolves to that skin's `#1A1A1A` (confirmed `PbTextBrush` is
   properly skin-reactive elsewhere — `SkinService.cs` rebuilds real `SolidColorBrush` objects per
   skin switch), sitting on a masthead band that stays near-black regardless of skin. Near-black text
   on a near-forced-black band.

## Out of scope

- Any change to `HomeFeedResolver`, `RecommendationResolver`, the shelf lineup (no shelves added,
  removed, or reordered), or navigation behavior. Content and data logic are untouched — visual/
  motion only, same boundary the 2026-08-28 spec drew.
- Search behavior, the masthead's Refresh action, and `PosterTile` itself (Home's shelf cards stay
  on `PosterTile`, not `PosterRail` — matches the 2026-08-28 spec's own implementation-time
  deviation; no reason to revisit that call here).
- The other 6 rail items (Home, Insights, Smart Lists, Reading Lists, Preferences, Pin) keep their
  current glyphs (`Home`, `DataHistogram`, `Filter`, `Bookmark`, `Settings`, `Pin`) — confirmed with
  the user there's no collision or semantic problem with any of them. They still pick up the shared
  size bump and active-state variant change below, since those are rail-wide, not per-icon.
- The in-flight Stats v2 skin-token additions (`chartBlue`/`chartViolet`, already shipped and merged
  as of this writing) are unrelated and untouched.

## 1. NavRail

**Glyph swaps** (`MainWindow.axaml`):
- Continuity (line 244): `Symbol="Star"` → `Symbol="Planet"`
- Library (line 214): `Symbol="Book"` → `Symbol="Library"`
- Books (line 222): `Symbol="Book"` → `Symbol="BookOpen"`

**Sizing** (`Button.rail fi|SymbolIcon` style, `MainWindow.axaml:57-59`): `FontSize` 20 → **22**,
applies to all 9 rail icons uniformly (nav items + Pin). The app-wide default in
`Styles/Icons.axaml` (15) is untouched — this override is rail-scoped, same as today.

**Active-state variant**: currently `Button.rail.active` (`MainWindow.axaml:48-52`) only changes
`Foreground`/`FontWeight`. Add an `IconVariant` swap to `Filled` on the active item's `SymbolIcon`,
alongside the existing color/weight change. Needs a binding from each button's own `IsX` property
(e.g. `IsLibrary`) to the icon's `IconVariant` — simplest is a per-button
`IconVariant="{Binding IsLibrary, Converter={StaticResource BoolToIconVariantConverter}}"` (new,
small converter: `true → Filled`, `false → Regular`) rather than a style selector, since
`IconVariant` isn't purely a visual-state property Avalonia's style system can flip the way
`Foreground` can via a pseudoclass — it's bound per-item to the same `IsX` boolean the `Classes.active`
binding already uses.

## 2. Home masthead

- **Ambient recolor**: the masthead scrim blends ~30% toward the current spotlight cover's dominant
  tone. Implementation: downsample the spotlight cover's `SKImage` to 1×1 (SkiaSharp-direct, same
  family of approach `CoverWallRenderer`/`BackdropBlurRenderer` already use — no new imaging
  dependency) to get an average color, blend it into the existing scrim gradient rather than
  replacing it, so it can never fully override any of the 5 skins' own palettes. Re-samples whenever
  `CurrentSpotlight` changes.
- **Scoped-down Ken-Burns**: a single slow pan/scale (`RenderTransform`, ~20s, eased) on the
  backdrop, **triggered by spotlight rotation or fresh navigation to Home** — not a perpetual
  infinite loop. (Deliberately scoped down from the original pitch: this project's own
  `avalonia-pro-max/motion` subskill flags `IterationCount="Infinite"` as appropriate only for small
  elements, and calls out "looping decorative animation that pulls eye from content" as a common
  mistake — a full-bleed 210px background running forever behind a reading list is exactly that.
  Triggered-once-per-change keeps the "the masthead visibly reacts to what's featured" feeling
  without the screensaver problem.) Reduced Motion: `RenderTransform="none"`, no pan/scale at all —
  transform-based, so this needs its own explicit check rather than relying on a token zeroing to 0.
- **Wordmark jitter**: the `PAPERBUNKR` `SplitText` instance (`HomeScreen.axaml` masthead,
  `FontSize="15"`) picks up the same red/cyan `:pointerover` jitter the `.hero`/`.heading` instances
  already have. Since the jitter is templated behavior on `SplitText` itself (not gated by the
  `.hero`/`.heading` style classes, which only set size), this should apply with no extra wiring —
  confirm at implementation time that the wordmark isn't set `IsHitTestVisible="False"` or similar,
  which would suppress `:pointerover` from firing.

## 3. Masthead theme compatibility (fixes the Windows 11 contrast bug)

- **`CoverWallRenderer.Render(...)`** gains a `SKColor baseColor` parameter (or equivalent), supplied
  by the caller from the active skin's `surface0`/`bg` token converted to `SKColor`. Replaces the 3
  hardcoded literals (`SKColor(8, 8, 10)` tile-clear, `SKColor(8, 8, 10, 205)` scrim, the
  `SKColor(6, 6, 6, 235)` vignette end-stop) with values derived from `baseColor` — darkened for the
  4 dark skins (visually near-identical to today, so no regression there), correctly light-derived
  for `windows_11`. The blur/tiling/vignette *shape* is unchanged, only the color source.
- **`HomeScreen.axaml:142-146` fallback gradient**: the 3 literal `GradientStop` colors become
  `DynamicResource`-bound to skin tokens (e.g. blending `PbSurface2Color`/`PbSurface3Color` toward
  `PbAccentSoftColor`, tuned at implementation time to keep the same moody-vignette *feel* on dark
  skins while actually tracking `windows_11`'s light palette instead of overriding it).
- **Re-render trigger**: `CoverWallRenderer` must be invoked again whenever the active skin changes,
  not just on Home load/Refresh — `HomeScreenViewModel` needs a subscription to the skin-change
  notification (`SkinService` already raises one for the rest of the app's live-reskin behavior) so
  switching skins while already on Home updates the masthead without requiring a navigate-away/back.
- This shares the exact same call site as the §2 ambient-recolor work (both need the current cover
  data and both write into the same masthead scrim), so implementing them together is intentional,
  not incidental scope creep.

## 4. Spotlight hero

- Auto-rotation (the existing `DispatcherTimer` in `HomeScreenViewModel`) pauses while the pointer is
  over the hero card or any dot, resumes on pointer-leave. No visual changes to `DetailHero` itself.

## 5. Shelf cards

*(Continue Reading, Continue Reading — Books, Recently Added, Collections, Because You Read rows)*

- **Entrance-stagger on Home load/navigation** (one-shot, not scroll-triggered). Reuses the
  `EntranceAnimation` attached-property pattern already specced (but not yet built or implemented
  anywhere) in
  [2026-09-07-chrome-content-motion-polish-design.md](2026-09-07-chrome-content-motion-polish-design.md)
  for the Library grid — that spec explicitly deferred "Home carousels" as its own future v2. Since
  the control doesn't exist in the codebase yet, this spec builds
  `Paperbunkr.App.Controls.EntranceAnimation` (`Enabled` bool, `Index` int attached properties;
  `.entrance` / `.entrance.entered` style pair on `Opacity`/`RenderTransform`, transitions on
  `PbMotionStandard`) as a small shared control rather than waiting on the unrelated Library work —
  whichever of the two lands first, the other reuses it unchanged.
  `HomeScreenViewModel` gets a one-shot `PlayEntranceAnimation` flag (set true on navigation into
  Home, consumed once) mirroring the pattern the Library spec describes.
- **Hover**: widen the existing hand-rolled glow-ring (`Primitives.axaml`/`Typography.axaml`
  `Border.posterCover` box-shadow literal, currently a 2px ring at `#66E0995A`) and add a subtle
  `scale(1.02)`-style pop, both on `PbMotionFast`. `PosterTile`'s existing box-shadow (its own
  literal, not the shared `PbElevationShadow` token) is otherwise untouched — no regression risk to
  its rest-state shadow.

## 6. Reduced Motion

Every new animation gets an explicit, verified-not-assumed off switch:

| Animation | Mechanism | Reduced-Motion handling |
|---|---|---|
| Shelf entrance-stagger | `Transitions` on `PbMotionStandard` | Already zeroed by `SkinService.ApplyReducedMotion` |
| Shelf hover glow/scale-pop | `PbMotionFast` | Same |
| Masthead pan/scale | `RenderTransform`, not token-driven | Explicit `RenderTransform="none"` check — does **not** get zeroed for free |
| Wordmark jitter | `SplitText`'s existing `PbMotionFast` transition | Already Reduced-Motion-safe (inherited, no new work) |
| Hero auto-rotate pause/resume | N/A — not an animation, a state toggle | N/A |

## Code surface

**New:**
- `Controls/EntranceAnimation.cs` — attached properties + `.entrance`/`.entrance.entered` style pair.
- A small average-color helper (SkiaSharp-direct, same file family as `CoverWallRenderer`/
  `BackdropBlurRenderer` in `Services/`) — exact name/shape decided at implementation time.
- `BoolToIconVariantConverter` (or equivalent) for the rail's active-state `Filled` swap.

**Changed:**
- `Views/MainWindow.axaml` — 3 `Symbol=` swaps, rail icon `FontSize` 20→22, active-state
  `IconVariant` binding on all 9 rail buttons.
- `Views/HomeScreen.axaml` — masthead scrim binding, pan/scale `RenderTransform` + trigger, fallback
  `RadialGradientBrush` stops swapped to `DynamicResource`, shelf `EntranceAnimation` attached
  properties on each rail's `ItemsControl`/container, widened hover glow + scale-pop on the shared
  shelf-card style.
- `Services/CoverWallRenderer.cs` — `Render(...)` takes a skin-derived base color instead of 3
  hardcoded `SKColor` literals; shape/blur/vignette math unchanged.
- `ViewModels/HomeScreenViewModel.cs` — `PlayEntranceAnimation` one-shot flag, hero hover
  pause/resume state, spotlight-cover average-color property, subscription to `SkinService`'s
  skin-change notification to re-render the masthead on live skin switch.
- `ViewModels/MainViewModel.cs` — no logic change expected; `IsLibrary`/`IsBooks`/etc. already exist
  and are reused as-is for the new `IconVariant` bindings.

**Unchanged:** `HomeFeedResolver`, `RecommendationResolver`, `PosterTile`, `DetailHero` (structure —
only consumed, not modified), `CoverWallRenderer`'s tiling/blur/vignette *shape* (only its color
source changes), all 5 skins' token *files* (no new tokens needed — the fix consumes existing ones).

## Testing

- Existing `HomeScreenViewModelTests` and `InsightsScreenViewModelTests`-style unit coverage: must
  still pass unmodified where they assert on data/navigation, since this is visual/motion-only.
- New unit coverage: the average-color helper (given a known solid-color test bitmap, returns that
  color; handles a null/missing spotlight cover by leaving the scrim unblended); `PlayEntranceAnimation`
  flips true on navigation and false after being consumed once (guards against virtualization
  replaying the entrance on scroll, the exact failure mode the motion subskill's "Common Mistakes"
  section warns about); `CoverWallRenderer.Render(...)` given each of the 5 skins' actual `bg`/
  `surface0` values produces a masthead whose sampled corner pixel is meaningfully closer to that
  skin's own tone than to the old hardcoded near-black (the concrete regression test for the
  Windows 11 bug — asserted numerically, not just "doesn't crash").
- `EntranceAnimation` and the rail's `IconVariant` binding are visual-state changes without a strong
  unit-test story — verified via the project's standing FlaUI/UIA3 `HomeScreenTests` /
  `MainWindowTests`-style suites (automation-id presence, `IconVariant` property value after a
  simulated `IsLibrary` toggle) plus **on-screen visual verification by the user**, per the
  project's standing computer-use gap (memory: `feedback_no_computer_use`) — this phase leans on the
  user's own screenshots, same as the 2026-08-28 Home spec did. For the theme-compatibility fix
  specifically, the user should check Home under **at least `windows_11` and one dark skin** (the
  bug this section fixes), not just the default skin.
- Build clean (0 new warnings), full `dotnet test` green, crash-free direct-exe launch — same bar as
  every prior UI-rework phase.

## Open questions

None blocking. Three implementation-time calls, all noted above: the exact average-color helper's
shape/location, confirming the wordmark's `SplitText` instance isn't hit-test-suppressed, and tuning
the fallback gradient's exact token blend so dark skins keep their current look while `windows_11`
actually changes.
