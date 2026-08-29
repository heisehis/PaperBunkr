# Home Screen Redesign

**Date:** 2026-08-28
**Status:** Design approved, plan pending.
**Sub-project 3 of 7** in the full UI rework (see
[2026-08-24-design-language-foundation-design.md](2026-08-24-design-language-foundation-design.md)
for the phase breakdown). Supersedes the visual direction in
[2026-08-24-home-screen-design.md](2026-08-24-home-screen-design.md), which shipped but was left
mid-iteration.

## Background

The Home screen (`HomeScreen.axaml` / `HomeScreenViewModel`) is functionally complete and committed:
a masthead (wordmark/title/subtitle/search/refresh), a rotating spotlight hero with dot pagination,
and the shelves — Continue Reading, Continue Reading&nbsp;— Books, Recently Added, Because You Read
(up to 3 stacked rows), Try This Reading List. All pick/sort logic lives in `HomeFeedResolver` /
`RecommendationResolver`; the view only renders it.

The user generated a v0.dev/Vercel mockup of the intended look and the shipped Avalonia screen reads
as noticeably less polished beside it. This redesign closes that gap. It is **visual/layout only** —
no change to what data appears, how it is picked, or how navigation works. The v0 mockup is the
**direction**, adapted per section against Paperbunkr's real data and the shared primitives the
Detail redesign (Phase 5) introduced (`DetailHero`, `PosterRail`, `PosterTile`).

Two things are also fixed in passing because this touches the same file:

- `HomeScreen.axaml:180` — `AutomationProperties.AutomationId="HomeSpotlightHeader"` on a bare
  `<StackPanel>` stopped surfacing in the UIA tree under Avalonia 12.1 / net10, failing
  `HomeScreenTests.AllFiveModules_RenderTheirEmptyStates_OnAFreshLibrary`. The id moves onto a real
  element.
- The masthead and the hero currently share one blurred backdrop bitmap
  (`CurrentSpotlight.BackdropImage`), which the user explicitly dislikes. The redesign gives each its
  own treatment.

## Out of scope

- Any change to `HomeFeedResolver`, `RecommendationResolver`, the module pick/sort logic, the
  navigation callbacks, or the shelf lineup (no shelves added or removed).
- App-wide FluentIcons migration — its own later branch. The masthead Refresh button stays on the
  existing vector `PbIconRefresh`.
- Full redesigns of the three Detail screens. `DetailHero` grows two backward-compatible opt-in
  slots here; whether the Detail screens adopt them is a separate call.

## 1. Structure & layout

Vertical lineup unchanged:

```
masthead
spotlight hero
Continue Reading
Continue Reading — Books        (only when HasBooksLibrary)
Recently Added
Because You Read — {seed}        (up to 3 rows)
Try This Reading List
```

- One `ScrollViewer`; the masthead scrolls away with the page.
- Inner content is capped at **max width ~1100px, centred**. Today the rows run edge-to-edge and
  stretch awkwardly on wide windows.
- A single hairline (`PbBorderBrush`) divider sits between the masthead band and the first shelf.
- Vertical rhythm opens up: ~32px between shelves (from ~28), masthead ~40px top and bottom.

## 2. Masthead — "living cover-wall"

- **Background:** a blurred, heavily darkened collage of up to 8 covers drawn from the user's own
  library, composed by a new `CoverWallRenderer` service (same family as `BackdropBlurRenderer`).
  Built once per Home visit / Refresh. Covers are sampled from the already-loaded Recently Added set,
  with a random-sample fallback when fewer than 8 exist. A radial dark vignette over the collage
  keeps text legible. It reads as "your shelf as atmosphere" and shifts as the library grows. The
  shared spotlight-cover blur is removed entirely.
- **Fallback:** on a library with zero covers, the background is a flat warm-to-black gradient (the
  v0 "ambient gradient" treatment) rather than an empty black band.
- `PAPERBUNKR` wordmark top-left — Bebas Neue, `PbAccentTextBrush`, letter-tracked.
- `⟳ Refresh library` — a labelled button top-right, `PbIconRefresh` + text.
- Centre column: chromatic-split title (see §5), subtitle, then the search box + amber Search
  button. Search behaviour is unchanged — `SearchCommand` jumps to Library with the query applied.

## 3. Spotlight hero

Rebuilt on the shared **`DetailHero`** control, which gains two opt-in, backward-compatible slots:

- **`Synopsis`** (`string?`) — a body line rendered under `MetaLine`. Null hides it, so the three
  Detail screens are unaffected until they choose to set it. On Home it is `Issue.Summary`, or a
  short generated fallback (`"{SeriesName} #{Number} — start reading."`) when `Summary` is blank.
- **`FooterContent`** (`object?` / content slot) — arbitrary content pinned below the hero body.
  Home places its carousel dot pagination here. Detail screens leave it unset.

Other `DetailHero` changes:

- Backdrop is dialled down for this consumer: the blurred spotlight cover at low opacity over
  `PbSurface0`, calmer than the fuller cinematic wash the Detail screens use. Exposed as a
  `BackdropIntensity` (enum: `Full` default / `Muted`) property so the existing callers keep their
  current look.

Home wiring:

- A new `HomeSpotlightHeaderSource : IDetailHeaderSource` adapter wraps the current
  `SpotlightIssueSample` — maps `CoverBrush` / `CoverImage` / `BackdropImage` / `HeaderTitle`
  (`Title`) / `SecondaryTitle` (`SeriesName`) / `MetaLine` (`Meta`) / `Synopsis` / a single
  `DetailHeroAction` ("Read now" → `OpenSpotlightCommand`). `TrackerProgress` is null.
- The carousel (`SpotlightItems` / `SpotlightIndex` / `CurrentSpotlight` / `DispatcherTimer` /
  `SetSpotlightItemCommand` / `OpenSpotlightCommand`) stays entirely in `HomeScreenViewModel`,
  unchanged. The adapter is re-created (or raises `PropertyChanged` on all members) when
  `CurrentSpotlight` changes.
- The `HomeSpotlightHeader` automation id moves from the bare `StackPanel` onto a real child
  element, fixing the Avalonia 12.1 regression.

## 4. Shelf cards — cover-forward

The three series/issue row modules and the Books row move onto the shared **`PosterRail`** control,
which gains a **`Size`** property:

- `Size="Rail"` — today's ~76px Detail-screen card. Existing `PosterRail` consumers get this by
  default, unchanged.
- `Size="Shelf"` — Home's ~132px card.

`Shelf` card style:

- No surface panel, no border. Cover art fills the card with a soft drop shadow
  (`PbElevationShadow`), `PbRadiusSm` corners.
- Progress bar (Continue Reading only) is a thin rounded amber bar floating **inside** the art near
  its bottom edge, not below the card.
- A gold `PbBadge` pill, top-right, over the art (issue number / "N new" / issue count — the
  existing per-module badge strings, unchanged).
- Serif title below the art, plus one muted second line (`PbTextFaintBrush`) — issue number, or
  "Vol. N".
- `PbGlowRing` on hover / keyboard focus (already the `PosterRail` behaviour).
- Partial next-card peek at the right edge of each row for scroll affordance.

`PosterTile` is **not** touched — its other consumers (Library grid, etc.) are unaffected. This adds
a `PosterRail` size only.

Row headers use the chromatic-split heading treatment. "Because you read **{seed}**" keeps the seed
name in `PbAccentTextBrush`.

## 5. Display type — chromatic split

The v0 headings have a slight RGB mis-registration ("misprinted comic"). New styles
`PbTextHeroSplit` and `PbTextHeadingSplit`:

- Rendered as three stacked `TextBlock`s: a red layer offset `-1,0`, a cyan layer offset `+1,0`, the
  real cream text on top. Offsets ~1px, tuned once at implementation against the real Bebas Neue
  glyphs.
- Applied to: the masthead title, the hero title (via `DetailHero`), and every section / row
  heading.
- **Hover jitter:** elements that carry split type *and* are interactive nudge their offsets ~1px
  over `PbMotionFast`. Because the animation is driven by `PbMotionFast`,
  `SkinService.ApplyReducedMotion` already zeroes it — reduced-motion users get static, non-jittering
  headings for free.
- **Risk:** Avalonia has no guaranteed additive / "screen" blend for `TextBlock`. On the near-black
  `PbSurface0` the plain three-layer stack reads correctly without it. If it looks muddy at
  implementation time, the fallback is a single offset colour layer (not three) — decided then, not
  now.

## 6. Try This Reading List & loose ends

- **Try This Reading List** keeps its distinct wide-card shape (it has a synopsis paragraph and a
  tag list `PosterRail` has no slots for). It picks up `PbSurface2Brush` + `PbRadiusLg`, the
  cover-forward thumbnail treatment, tags swapped to the `pbChip` primitive, synopsis kept,
  `Start the list →` in `PbAccentTextBrush`.
- **Empty states:** the current per-shelf copy is kept but restyled to `PbTextFaintBrush`. The
  all-empty fresh-library case (every shelf empty at once) collapses to a single centred friendly
  line plus a "Scan a folder to get started" pointer to Preferences&nbsp;→&nbsp;Libraries, instead
  of six stacked "Nothing yet" strings.
- **`Continue Reading — Books`** gets the same cover-forward `PosterRail` `Shelf` treatment; the
  `HasBooksLibrary` gate is unchanged.

## 7. Code surface

**New:**

- `Services/CoverWallRenderer.cs` — `Render(IReadOnlyList<Bitmap> covers, PixelSize size) -> Bitmap`.
- `ViewModels/HomeSpotlightHeaderSource.cs` — `IDetailHeaderSource` adapter over
  `SpotlightIssueSample` + the spotlight commands.
- `PosterRail` `Size` enum (`Rail` / `Shelf`) + the `Shelf` `DataTemplate` / sizing.
- `DetailHero` `Synopsis`, `FooterContent`, `BackdropIntensity` properties + markup.
- `PbTextHeroSplit` / `PbTextHeadingSplit` styles (+ the hover-jitter selector) in
  `Styles/Typography.axaml`.

**Changed:**

- `Views/HomeScreen.axaml` — most of the file.
- `ViewModels/HomeScreenViewModel.cs` — construct the cover-wall, expose the spotlight adapter. No
  query / pick / navigation changes.
- `Views/DetailHero.axaml` (+ code-behind) — the two new slots + intensity, all backward compatible.
- `Models/HomeScreenViewModel` collaborators only read existing members.

**Unchanged:** `HomeFeedResolver`, `RecommendationResolver`, `SpotlightIssueSample`,
`SeriesCardSample`, `HomeContinueReadingCard`, `HomeBookCard`, all navigation callbacks, the default
launch screen.

## 8. Testing

- `HomeScreenViewModelTests` — must pass **unmodified** (no VM logic changes); run before and after
  to confirm.
- New unit cases:
  - `CoverWallRenderer` — N covers compose to one bitmap of the requested size; handles fewer than N
    available; handles zero (caller falls back to the gradient, renderer returns null / throws a
    documented way).
  - `HomeSpotlightHeaderSource` — every `IDetailHeaderSource` member maps from the underlying
    `SpotlightIssueSample`; raises `PropertyChanged` when the current spotlight changes.
- `HomeScreenTests` (FlaUI/UIA3):
  - Fix `AllFiveModules_RenderTheirEmptyStates_OnAFreshLibrary` — the `HomeSpotlightHeader` id now
    resolves.
  - The hero renders through `DetailHero`; a shelf card renders through `PosterRail`
    (`AutomationId` checks).
  - Existing `SpotlightCard_Click_NavigatesAwayFromHome` /
    `RecentlyAddedCard_Click_NavigatesAwayFromHome` still pass with the new controls.
- Build clean (0 new warnings), full `dotnet test` green, crash-free direct-exe launch — same bar as
  every prior UI-rework phase.
- On-screen visual verification is the standing computer-use gap; this phase leans on the user's own
  screenshots, as the earlier Home iteration did.

## Open questions

None blocking. Two implementation-time calls, both noted above: the exact chromatic offset in px,
and the three-layer-vs-one-layer split fallback if the blend reads muddy.

## Implementation notes (deviations from the sections above, decided while building)

- **Shelf cards stayed on `PosterTile`, restyled in place** — not rerouted through a new `PosterRail`
  `Size` variant. `PosterTile` is consumed only by `HomeScreen.axaml` (and a `#if DEBUG` showcase),
  so restyling its `.posterTile` class + markup to the cover-forward look is the same visual result
  with far less surface and no risk to the Detail "Related" rail. `PosterRail` is untouched.
- **`SplitText` is a code-only `TemplatedControl`** (`Controls/SplitText.cs`) whose implicit
  `ControlTheme` lives in `Styles/Typography.axaml` — three stacked `TextBlock`s (red `-1px`, cyan
  `+1px`, real text on top), jitter to `±2px` on `:pointerover` over `PbMotionFast`. Not a new
  `x:Class` View, so no AVLN2000 concern.
- **`DetailHero` gained `Synopsis` (a default interface member on `IDetailHeaderSource`, so the three
  detail VMs need no change) and a `MutedBackdrop` bool** (simpler than the proposed
  `BackdropIntensity` enum). The `FooterContent` slot was dropped: the carousel dots stay a sibling
  directly below the hero card, which is what the V0 mock shows anyway, and avoids a namescope
  binding hazard.
- **The "all-empty collapses to one friendly line" idea was dropped** — it conflicts with the
  existing `HomeScreenTests.AllFiveModules_RenderTheirEmptyStates_OnAFreshLibrary` contract, which
  requires all five module headers present on a fresh library. Per-shelf empty states are kept,
  already styled `PbTextFaint`.
- **The `HomeSpotlightHeader` automation id moved onto the spotlight empty-state `TextBlock`** (a
  real element, present exactly in the fresh/empty-library case the tests assert on), fixing the
  Avalonia 12.1 bare-`StackPanel` regression.
- Copy kept as shipped ("Your Library", not V0's "Explore the Multiverse") — copy wasn't a redesign
  topic.
