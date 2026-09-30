# Home Spotlight: accordion carousel

Addendum to the 2026-09-28 Home pitch (`2026-09-28-home-cosmetics-design.md` C7 and "Hero tilt"). It replaces the spotlight hero
with an accordion modelled on Simey's "Stripe Sessions style Flex Carousel" (codepen.io/simeydotme/pen/gOBMZMe, listed on
freefrontend.com as "Fluid Flexbox Accordion Carousel"). It was chosen over GreenSock's scroll-driven infinite 3D carousel, which
would fight Home's own vertical scroll and shows only one cover's information at a time. Grilled 2026-09-29 in one round, with live
mockups (`.superpowers/brainstorm/2188-1790638164/content/accordion-round1-full.html`); every recommendation was accepted.

## How the demo works (read from its source)

- The active item has a fixed width (`flex-basis: 450px`). Every other item is `flex: 1 1 10%`, and a hovered one widens to half
  the active width.
- It's a conveyor: prev/next move the first DOM node to the end, so the active slot stays put and the edge items shrink to 5–10px
  slivers before disappearing.
- Inactive images are desaturated under a green-cyan gradient, which slides away (`translateY(100%)`) when an item becomes active.
- The caption slides in from the left with a 50–100ms delay.
- It auto-advances every 3s, stops for good on any interaction, and responds to ←/→ keys.

## Decisions

- **Replaces** the two-layer `DetailHero` crossfade, the backdrop zoom, the cover tilt and the dots on Home. `HeroTilt`,
  `DetailHero.TiltCover`/`BackdropControl` and their tests are deleted. `ParallaxBackdrop` stays for the Detail screens.
- **8 picks** (was 6), same rules: one per series, two new-arrival slots. **Fixed order**, not a conveyor: the open panel expands
  in place.
- **Band 300px tall**, over a blurred copy of the open pick's cover (`SpotlightIssueSample.BackdropImage`, already pre-blurred)
  under a dark wash. The vibrant accent tint on the masthead still follows the open pick.
- **Open panel 400px wide:** the whole cover, uncropped at 200×300, on the left; series, issue title, meta line, a three-line
  synopsis and Read now on the right. The caption slides in (translate plus fade) after the panel opens.
- **Slivers** share the rest of the width, minimum 44px each. They show the cover (centre crop), darkened, in full colour, with no
  text and a tooltip ("Saga #54"). Too narrow for eight means panels drop off the end, always keeping the open panel plus 3 slivers.
- **Interaction:**
  - Hovering a sliver peeks it wider (weight 1.6×).
  - Clicking a sliver, pressing Enter on it or focusing it with the keyboard opens it.
  - Clicking the open panel (or its Read now) opens the reader.
  - ‹ › buttons sit at the right of the "Spotlight" heading; ←/→ move focus to the neighbouring panel, which opens it.
- **Auto-advance** every 7s, paused while hovered or while Home is off screen, as before. It never stops permanently.
- **Reduced Motion:** widths snap and the caption appears without sliding.

## Design (approach 1: a custom panel with animated widths)

- **`Controls/AccordionLayout.Compute(count, openIndex, peekIndex, width, openWidth, minWidth, spacing, peekWeight)`** is pure and
  returns one width per child. The open child gets `min(openWidth, width - reserve)`. The others split the remainder by weight
  (peek = 1.6, else 1). Children that can't get `minWidth` are dropped from the end (width 0), keeping at least three slivers. The
  widths plus spacing always sum to `width`.
- **`Controls/AccordionPanel : Panel`** has `OpenIndex`, `OpenWidth`, `MinItemWidth`, `Spacing` and `PeekWeight`, plus
  `ActivateCommand` (parameter: the child's `DataContext`).
  - On a target change it snapshots the current widths and eases toward the new ones with one 16ms `DispatcherTimer` over about
    450ms (`CubicEaseInOut`). Each frame is a lerp between two vectors that both sum to the width, so the row never jitters.
  - `MeasureOverride` measures each child at its current width; `ArrangeOverride` lays them left to right. A dropped child is
    arranged at zero width and made non-hit-testable.
  - Hover peek comes from the panel's own pointer tracking.
  - Keyboard focus reaching a child, or ←/→ inside the panel, runs `ActivateCommand` for that child.
  - Reduced Motion (`MotionTokens.IsReducedMotion()`) jumps straight to the targets.
- **`SpotlightPanelViewModel`** (`Sample`, observable `IsOpen`, `Tooltip`). `HomeScreenViewModel.SpotlightPanels` mirrors
  `SpotlightItems`, and `IsOpen` follows `SpotlightIndex`. There are three commands:
  - `ActivateSpotlightPanel(panel)`: opens a closed panel, or opens the reader for the open one.
  - `NextSpotlight` / `PreviousSpotlight`: wrap around.

  `SetSpotlightItemCommand`, `OpenSpotlightCommand`, the hover/off-screen pauses and the accent sampling are unchanged.
  `SpotlightLayerA/B`, `IsLayerAActive`, `SpotlightLayerShown`, `SpotlightHeader` and `HomeSpotlightHeaderSource` are removed,
  since nothing uses them.
- **`Views/Home/SpotlightSection.axaml`:** a heading row with the ‹ › buttons, then the 300px band (blurred backdrop, wash, then
  an `ItemsControl` whose items panel is the `AccordionPanel`). Each item is a `Button` (`Classes.open`) with a sliver layer (the
  cropped cover plus a darkening layer) and an open layer (the cover plus info), cross-faded on `PbMotionStandard`.
- **Automation IDs:** `HomeSpotlightCard` moves to the open panel's Read now button, and `HomeSpotlightHeader` stays on the empty
  state, so the UI tests keep their meaning.

## Testing

- **`AccordionLayout`:** sums to the width; the open panel gets its width; peek is wider than rest; drops from the end on narrow
  widths, keeping 3 slivers; count 0 and 1 handled; a narrow window clamps the open width.
- **`AccordionPanel`:** under Reduced Motion the arrange is immediately at target; keyboard focus of a child runs
  `ActivateCommand`.
- **ViewModel:**
  - 8 picks.
  - `IsOpen` follows the index.
  - Activating a closed panel opens it; activating the open one opens the reader.
  - Next/previous wrap.
  - Removed members' tests deleted.
- **Headless harness render** of the band in dark and light themes.

## Implementation notes

Built 2026-09-29, uncommitted.

- **Files.** New: `Controls/AccordionLayout.cs`, `Controls/AccordionPanel.cs`, `ViewModels/Home/SpotlightPanelViewModel.cs`.
  Rewritten: `Views/Home/SpotlightSection.axaml`, the spotlight styles in `Views/Home/HomeStyles.axaml`, and `Views/HomeScreen.axaml.cs`.
- **Removed.**
  - Untracked files created last session: `Controls/HeroTilt.cs`, `HeroTiltRenderGateTests.cs`.
  - Tracked files: `ViewModels/HomeSpotlightHeaderSource.cs` and its tests.
  - `DetailHero.TiltCover`/`BackdropControl` and the `heroCoverTilted` style.
  - The ViewModel's layer members and `_applyingSnapshot`.
  - The `heroCard`/`heroLayer`/`spotlightDot` styles.
- **Open-panel layout.** The open layer is a fixed 400px grid that the widening panel reveals, like the demo's fixed-width image.
  The cover column is 181px, which is 2:3 at the 272px inner height (300px band minus 14px margins). The synopsis wraps up to 6
  lines, because the text column has room for more than the 3 planned.
- **Focus cue.** `Button` has no `BoxShadow`, so the keyboard-focus cue is a 2px accent border. That XAML failed after C# compiled;
  the half-built DLL was deleted before rebuilding, per the CLAUDE.md weave gotcha. A `Border` inside each panel clips it to the
  rounded corners, because a Button only clips to its rectangle.
- **Keyboard.** ←/→ only move focus, and focus opens the panel. An explicit activate as well would have hit the now-open panel and
  opened the reader.
- **Verification.**
  - Home, Detail and MainViewModel App tests: 416/416. That includes the new `AccordionLayout`, `AccordionPanel` and ViewModel
    tests.
  - Headless harness renders in dark and light themes show the open panel, seven slivers, the blurred band and the ‹ › buttons.
  - The user ran the Debug build on 2026-09-29 (screenshots: accordion open panel, sliver tooltip, the rest of Home, the
    Preferences Home tab) and raised no issues.
