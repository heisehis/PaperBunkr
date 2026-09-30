# Home screen: cosmetics (pitch spec A)

Spec A of the Home screen pitch (Roadmap, "Home screen pitch", 2026-09-28). It covers cosmetics C1–C10 plus three effects
borrowed from the landing site (scroll-reveal, hero tilt, card hover/shadow). Spec B
(`2026-09-28-home-improvements-design.md`) covers improvements I1–I5 and owns the section model both specs build on. One
plan builds both: B's section model lands first, and A's visuals sit on top of it.

The decisions came from a grilling session on 2026-09-28 (four rounds, with the visual companion). Mockups are in
`.superpowers/brainstorm/1000-1790624627/content/` (`home-today-annotated.html`, `round2-visuals.html`,
`round3-visuals.html`). The Home dashboard is a deliberate ComicRack CE deviation (CE has no home screen), so nothing
here needed a CE-parity lookup.

## Facts this rests on (verified 2026-09-28)

- **C1 and C7 were already built, with defects.**
  - The spotlight hero already draws the blurred spotlight cover (`DetailHero` `Backdrop`).
  - The masthead already tints toward the spotlight cover (`SpotlightAccentToBrushConverter`, 0.42 opacity).
  - Rotation already has a crossfade and a Ken Burns step (`HomeScreen.axaml.cs`).
  - The defects:
    - The accent is a one-pixel average (`SpotlightAccentSampler`), which is usually muddy brown or grey.
    - The muted scrim is a hard-coded `#59000000`.
    - The "crossfade" dips the card to 5% opacity, swaps, and fades back, so the page shows through.
    - The Ken Burns step runs on the 8-cover wall, not the spotlight. It ping-pongs between two transforms over 8 s while
      the spotlight rotates every 7 s, so it never settles. It fires on accent-colour change, not spotlight change.
    - The rotation timer keeps ticking while Home is off screen.
- **C5 was half built.** Recently Added's badge already reads "New" for 7 days (`SeriesCardSample.RecentAddBadgeLabel`), in
  place of the issue count.
- **C4.** `PosterTile`'s inset progress bar exists, but its track uses `CornerRadius="999"`, which breaks the squircle rule.
- **C9 was built and removed.** `DetailHero` parallax shipped (chrome-content-motion-polish item 2) and was removed on
  2026-09-08. The backdrop image exactly filled its clipped frame, so translating it exposed an empty edge. The removal note
  prescribes rendering the backdrop oversized.
- **C6.** The Collections shelf shows one cover (`CollectionResolver.GetCoverHint`). `ReadingListCoverMosaic.PickCoverKeys`
  exists. Its rule: 1–2 covers stay single, 3 repeat the first to fill 2×2, 4+ use the first four.
- **Themes.** A theme is light when `ThemeDefinition.Mode == "Light"`. `ThemeService.ApplyThemeResources` writes every `Pb*`
  resource and raises `ThemeApplied`. The modes are `ThemeAutoMode.Off`, `FollowSystem` and `Scheduled`.
- **DetailHero is always light-on-dark.** Its badges and synopsis use hard-coded light hex by design ("always sits on a dark
  photographic backdrop").
- **Avalonia 12.1.1** ships `Rotate3DTransform`, `BlurEffect` and `DropShadowEffect` (checked in `Avalonia.Base.dll`).
  - A keyframe `Animation` that sets `RenderTransform` throws "No animator registered". Transform motion must use
    `Transitions`, or `Animation.RunAsync` passed the Visual (memory note on keyframe `RenderTransform`).
  - `backdrop-filter`-style live blur of content behind a control has no equivalent. Pre-rendered blur is the workaround.

## C1 — spotlight colour and scrim (polish and fix)

- **Vibrant accent.** `SpotlightAccentSampler.Sample` switches from a one-pixel average to a dominant vibrant colour:
  1. Downscale to 16×16.
  2. Bucket the pixels into 12 hue bins.
  3. Weight each pixel by `saturation × value`, skipping pixels with `s < 0.2` or `v < 0.15`.
  4. Return the heaviest bin's weighted mean colour.
  5. If fewer than 10% of pixels qualify (a near-grey cover), fall back to the old average.
  
  It stays pure and static.
- **Scrim token.** `DetailHero`'s muted scrim becomes `{DynamicResource PbHeroScrimColor}`. `ThemeService.ApplyThemeResources`
  writes it from the theme mode: `#59000000` for dark themes (unchanged), `#1F000000` for light themes. The hero stays a
  photographic, light-on-dark surface in every theme, because its text is light by design. Light themes only lose the extra
  Home-only darkening, so it no longer reads as a slab. `App.axaml` gets the dark default.
- The cover wall stays in dark themes. In light themes it's replaced by the sky (C2), and the spotlight tint layer is hidden
  there, so the sky is the masthead's only colour source.

## C2 — greeting and light-theme sky

- **Greeting.** `DayPhase.For(DateTime local)` is pure and returns `Morning` (05:00–11:59), `Afternoon` (12:00–16:59),
  `Evening` (17:00–20:59) or `Night` (21:00–04:59). The greetings are "Good morning", "Good afternoon", "Good evening" and
  "Late-night reading?". The greeting replaces "Your Library" in every theme. The subtitle and search box are unchanged.
- **Sky.** When the active theme is light (`ThemeService.IsLightTheme`, set in `ApplyThemeResources`), the masthead shows a
  vertical sky gradient in place of the cover wall and the spotlight tint:
  - It runs from the phase's top colour to its bottom colour, then into `PbSurface0Color` over the last 25%.
  - The eight colours are theme-independent resources in `App.axaml`: `PbSky{Morning,Afternoon,Evening,Night}{Top,Bottom}Color`.
    Values: morning `#BFE0FF → #FFE7C9`, afternoon `#8FC3F2 → #E3F1FF`, evening `#FFB38A → #F7D9E6`, night `#4A5B8F → #C9CFE6`.
  - At night the greeting and subtitle use light text (`#FFFFFF`, `#E6E9F5`), because the night sky is dark at the top.
- **Updating.** The phase is recomputed on every Home load. `HomeScreenViewModel` arms a one-shot `DispatcherTimer` for the
  next phase boundary, and re-arms it on each tick and each load. `ThemeApplied` refreshes the light/dark decision live.
- The sky shows for any light theme, whatever the auto mode (Off, FollowSystem or Scheduled). The user's ask was about
  FollowSystem users. The rule is "light theme", and FollowSystem-with-a-light-OS is covered by it.

## C3 — section heading icons

The shared section heading gets an icon before its title (`fi:SymbolIcon`, `PbIconSizeSm`, `PbAccentTextBrush`). Each section
ViewModel supplies its `Symbol`:

| Section | Icon |
|---|---|
| Spotlight | `Sparkle` |
| Needs Attention | `Flag` |
| Continue Reading | `BookOpen` |
| Recently Added | `New` |
| Collections | `Stack` |
| Because You Read | `Lightbulb` |
| Reading List | `TextBulletListLtr` |

A symbol missing from the FluentIcons version in use falls back to a close glyph, recorded in the implementation notes.

## C4 — progress bar

- The `PosterTile` progress track and fill use `{StaticResource PbRadiusChip}`.
- The fill animates from 0 to its value once, when the tile is first shown with `EntranceAnimation` armed (the same one-shot
  trigger as the stagger). It uses a `ScaleTransform` X on the fill, driven by `DoubleTransition`/`TransformOperationsTransition`
  on `PbMotionStandard`, never `Width`.
- Reduced Motion shows the final value at once.

## C5 — "NEW" ribbon

- A diagonal ribbon across the top-right corner of the `PosterTile` cover, clipped by the cover's own `ClipToBounds`:
  45° rotated `Border`, accent fill, `PbAccentText`-contrast text "NEW", 8.5px bold. New `PosterTile.ShowNewRibbon` (bool).
- Recently Added sets it from `SeriesCardSample.IsRecentlyAdded` (newest issue added ≤ 7 days ago). Its gold badge goes back to
  `IssueCountLabel`, and `RecentAddBadgeLabel` is removed.
- Only the Recently Added section sets it.

## C6 — Collections collage

`HomeCollectionCard` gains `MosaicCoverKeys`, filled from `ReadingListCoverMosaic.PickCoverKeys` over the collection's member
cover keys. This keeps the Reading Lists rule (1–2 single, 3 fill by repeating the first, 4+ first four), which is a
deliberate deviation from the grilled "4+ distinct, else single". One rule for both surfaces beat two rules. The keys are
gathered in the background snapshot pass. `PosterTile` gains `MosaicSources` (a list of 4 `IImage?`): when set, it renders a
2×2 `UniformGrid` in place of the single cover.

## C7 — spotlight rotation (polish and fix)

- **Real crossfade.** The hero region holds two stacked `DetailHero`s (layers A and B), each with its own
  `HomeSpotlightHeaderSource`, where the source is pinned to one pick rather than reading "current".
  - On rotation, the hidden layer gets the new pick and fades in over the visible one on `PbMotionStandard`, and the layers
    swap roles.
  - The outgoing layer stays at full opacity underneath until the incoming one is opaque, then drops to 0 instantly.
  - Hit-testing only goes to the top layer.
- **Zoom.** Each time a layer becomes the incoming one, its `DetailHero` `Backdrop` runs one `Animation.RunAsync` from
  scale 1.0 to 1.06 over 6 s (`CubicEaseOut`), finishing before the 7 s rotation. It's passed the `Image`, not the transform.
  It's cancelled when the layer goes out. The masthead cover wall no longer moves, apart from C9 parallax.
- **Timer.** `HomeScreenViewModel.PauseSpotlightRotation`/`ResumeSpotlightRotation` are also called on `HomeScreen`
  `DetachedFromVisualTree`/`AttachedToVisualTree` and when `IsVisible` flips, alongside the existing hover pause. A hover-pause
  must not be undone by a re-attach resume: the two reasons are tracked separately, and the timer runs only when neither is
  active.
- **Reduced Motion.** The crossfade is instant (token), and there's no zoom.

## C8 — Because-You-Read lead card

Each Because-You-Read row renders the seed series first:
- A 0.75-scale `PosterTile` (cover only) with a 2px `PbAccentBrush` outline and the caption "you read".
- Then an `ArrowRight` icon, then the recommendation cards.
- Clicking the lead card opens the seed series' Detail.
- The heading keeps "Because you read {name}".
- `BecauseYouReadRow` gains `SeedSeries` (`SeriesCardSample`).
- The lead card never shows spec B's "Not interested" ✕.

## C9 — parallax, done properly

- **`Controls/ParallaxBackdrop`.** A code-only `Control` (no `.axaml`, so no AVLN2000 exposure) with a `Source` (`IImage?`)
  property. It renders the image `UniformToFill` into a rect 25% taller than its bounds (12.5% overscan above and below).
  - On attach it finds its nearest ancestor `ScrollViewer` and listens to `ScrollChanged`. It shifts the image by
    `clamp(-offsetY × 0.35, ±overscan)`, where `offsetY` is how far the control has scrolled past the viewport top, using a
    `TranslateTransform` on its own render.
  - It detaches its handler on detach.
  - Reduced Motion (read live through `MotionTokens.IsReducedMotion()`) pins the shift to 0.
  - The pure `ParallaxMath.Offset(scrolledPast, height, factor, overscanFraction)` is unit-tested.
- **Used by:**
  - the Home masthead cover wall (dark themes);
  - `DetailHero`'s backdrop, which replaces its `Image` with a `ParallaxBackdrop`. That restores parallax on the comic, manga
    and book Detail screens and applies it to both Home hero layers. The C7 zoom then targets the `ParallaxBackdrop`, which
    carries the scale via its own `RenderTransform`.
- The `CosmeticThumbnailSettings.HeroBackdrop` opacity toggle keeps working on the new control.

## C10 — seasonal flourish

- `AppSettings.HomeSeasonalFlourish` (bool, default false; the column lands in spec B's migration). The toggle lives on spec
  B's Preferences › Appearance › Home tab.
- The pure `SeasonalCalendar.ActiveOn(DateOnly)` returns `null` or a `Season`, in this priority order:

  | Season | Window | Icon | Tint |
  |---|---|---|---|
  | `FreeComicBookDay` | the first Saturday of May | `Gift` | `#E0B341` |
  | `Halloween` | Oct 24–31 | `WeatherMoon` | `#F28C28` |
  | `WinterHolidays` | Dec 15–31 | `Snowflake` | `#7FC8F8` |
  | `NewYear` | Jan 1–3 | `Star` | `#C9A8FF` |

- While a season is active and the setting is on, the icon sits after the PAPERBUNKR wordmark and the greeting line takes the
  tint colour. The colours are `PbSeason*Color` resources in `App.axaml`.
- It's recomputed on each Home load.

## Scroll-reveal (from the landing site)

- **`Controls/ScrollReveal.Enabled`.** An attached bool. On attach, the element starts at `Opacity 0` and `translateY(12px)`.
  - It subscribes to `EffectiveViewportChanged`. The first time the element intersects the viewport (≥ 8% visible), it sets
    `Opacity 1` and `RenderTransform none` through transitions (`PbMotionStandard × 2`, `PbMotionEase`), then unsubscribes.
  - Elements already visible when first measured reveal immediately, with no animation, so the first paint isn't delayed on
    top of the card stagger.
  - Reduced Motion: no initial offset or opacity, so nothing to reveal.
- **Used by** each Home section container. The website equivalent is `site/src/components/Reveal.astro` +
  `global.css:123-126`.

## Hero tilt (from the landing site)

- **`Controls/HeroTilt.Enabled`.** An attached bool on `DetailHero`'s cover `Border`, enabled only by Home (new
  `DetailHero.TiltCover` property). It sets `Rotate3DTransform(AngleY -10°, AngleX 3°, Depth 900)` plus a `TransformOperationsTransition`, and goes to 0° on `:pointerover` and back on leave. The cover gets a static deeper `BoxShadow`.
- Reduced Motion: no tilt.
- **Gate.** The plan's first task renders a cover headless with the transform and checks the pixels actually change (the
  trapezoid edge moves) and nothing throws. If it fails, the tilt is dropped (flat cover plus shadow) and the result is recorded
  here in the implementation notes.

## Card hover and shadow (from the landing site)

- `PosterTile`'s `.posterCover` gets a deeper static shadow (`0 14 34 0 #66000000`, similar to the site's `0 20px 50px`).
- On `:pointerover` it scales the cover to 1.02 through `TransformOperationsTransition` on `PbMotionFast`. The shadow is never
  animated (poster glow-ring race).
- Reduced Motion: the token makes the scale instant.
- `PosterTile` is only consumed by Home (and a DEBUG showcase), so nothing else changes.

## Testing

- Pure units:
  - `SpotlightAccentSampler` (a saturated patch wins over a grey majority; a grey cover falls back to the average)
  - `DayPhase` (04:59, 05:00, 11:59, 12:00, 16:59, 17:00, 20:59, 21:00, midnight)
  - `SeasonalCalendar` (Free Comic Book Day for 2026–2030, window edges, the New Year wrap, priority)
  - `ParallaxMath` (clamp, zero at rest, factor)
- ThemeService: `PbHeroScrimColor` and `IsLightTheme` follow the mode.
- Motion at rest under Reduced Motion: `ParallaxBackdrop` shift is 0 at any offset, `ScrollReveal` leaves opacity 1 and no
  transform, `HeroTilt` sets no transform.
- Headless view tests:
  - The ribbon shows only on Recently Added cards added within 7 days.
  - The mosaic grid appears when `MosaicSources` is set.
  - The lead card is the first item of a Because-You-Read row.
  - The greeting text matches the phase.
  - A light theme shows the sky layer and hides the cover wall and tint.
- The hero-tilt render gate (see above).

## Implementation notes

Built 2026-09-28 in the shared working tree (uncommitted). Build and test runs used a separate `-c HomePitch` configuration, so
they never touched the Debug outputs another session was testing against.

- **Tilt gate passed.** `HeroTiltRenderGateTests` renders a cover through real Skia with and without
  `Rotate3DTransform(3°, -10°, depth 900)`: the flat one has equal left/right edge heights, the tilted one doesn't. The tilt is
  kept (`Controls/HeroTilt.cs`, on the hero cover through `DetailHero.TiltCover`, Home only).
- **C1.** `SpotlightAccentSampler.Sample` now downsamples to 16×16 and calls the pure `PickVibrant`. `PbHeroScrimColor` is derived
  from the theme's `Mode` inside `ThemeService.ApplyThemeResources`, not a new `theme.json` field. `PbThemeIsLight` is written
  alongside it, and `ThemeService.IsLightThemeActive` reads it.
- **C2.** Greeting and subtitle colours are ViewModel-computed brushes (`GreetingBrush`, `MastheadSubtitleBrush`), re-evaluated on
  `ThemeApplied`, on the phase timer and on every load. They're resolved from the `PbText*`/`PbSkyNight*` resources.
- **C3.** Icons used: `Sparkle`, `Flag`, `BookOpen`, `New`, `Collections` (for Collections, instead of `Stack`), `Lightbulb` and
  `TextBulletList` (`TextBulletListLtr` doesn't exist in FluentIcons 2.1.337). Adding an icon meant adding a heading to
  Spotlight, which had none, so the hero now sits under a "Spotlight" heading. Because You Read has no section-level heading:
  each row keeps its own "Because you read X", now with the icon.
- **C4.** The fill is a `ScaleTransform` X with its own `DoubleTransition` (3 × `PbMotionStandard`, `CubicEaseOut`), started on
  each tile's first attach. `PosterTile` also now ignores right-clicks for its open command, so the new context menu can open
  without also opening the series.
- **C5.** The ribbon is **top-left**, because `PosterTile`'s badge was already top-right. The mockups had it the other way round.
- **C6.** The existing `ReadingListCoverMosaic` rule is used as-is (3 covers fill by repeating the first), as recorded above.
- **C7.** The two layers live in `Views/Home/SpotlightSection.axaml`. `HomeScreen.axaml.cs` finds them by name, puts the incoming
  one on top at full opacity (its style transition fades it in), and drops the outgoing one after `PbMotionStandard`. The zoom is
  `Animation.RunAsync` on the incoming `DetailHero.BackdropControl`, and the cover-wall Ken Burns step was removed.
  `HomeScreenViewModel.SetOnScreen` runs on attach, detach and `IsVisible` changes, tracked separately from the hover pause.
- **C8.** The lead card is a `PosterTile` in a `LayoutTransformControl` (0.75×) with the new `Outlined` flag.
- **C9.** `Controls/ParallaxBackdrop.cs` is used by the masthead cover wall (dark themes) and `DetailHero`, which puts parallax back
  on the three Detail screens. The headless test app has no theme, so its `ScrollViewer` can't scroll; the drift is tested through
  an `internal ApplyScroll(scrolledPast)` seam.
- **Card hover/shadow.** Home cards **already** scaled to 1.02 on hover (`Border.posterTile.shelfCard`). Only the deeper resting
  shadow is new.
- **"Not interested" ✕** (spec B) sits **top-left**, 26×26, visible on hover or keyboard focus, for the same badge reason as the
  ribbon.
- **Review checklist.** Overlays drawn on cover art (ribbon, ✕, book marker, progress track) use fixed dark/light hex, the same
  convention as the existing `posterScrim` and `DetailHero` badges ("always on art, never theme-reactive"). Every other colour
  is a resource.
- **Fixes after the first on-screen look (2026-09-29).**
  - The tilt at −10°/depth 900 was real but only a few pixels on a 142px cover. It's now the landing site's −14° Y / 4° X at
    depth 400, which is the same depth-to-width ratio as the site's.
  - The ribbon text was accent-on-accent; it's now `PbBadgeTextBrush`, the colour primary buttons use on the accent fill.
  - The wordmark turns light (`PbSkyNightTextBrush`) on the light-theme night sky.
- **Verification.**
  - Data.Tests: 2026/2026.
  - App.Tests: every class touching changed code (Home, Detail, PosterTile, Preferences, ThemeService, MainViewModel, Insights,
    Collection, Spotlight, SeriesCardSample, IssueTileGlyph, WelcomeTour, Cosmetic) passed 866/866. The full App.Tests run was
    stopped at 31 minutes (quiet output, so no per-test progress was visible) and wasn't repeated.
  - A scratch headless harness rendered the real Home screen with the real App styles in dark/evening/seasonal, light/morning,
    light/night and hover states.
