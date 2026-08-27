# Home Screen

**Status:** Design phase, not yet planned/implemented.
**Sub-project 3 of 7** in the full UI rework (see [Design Language Foundation](2026-08-24-design-language-foundation-design.md) for the full phase breakdown). Phases 1 and 2 are implemented, tested, and verified.

## Background

`HomeScreenViewModel`/[HomeScreen.axaml](../../../src/Paperbunkr.App/Views/HomeScreen.axaml) already has real data wired up (Phase 6a of an earlier metadata-model initiative, "Because You Read") — a quiet search bar, a small "Spotlight" card (`Button.spotlightCard`, bound to `CurrentSpotlight`/`SpotlightItems`, auto-rotating with dot indicators), a separate "Try This Reading List" card (`HasReadingListSpotlight`, its own `spotlightCard`-styled button with synopsis + tags), and three horizontally-scrolling rows (Continue Reading, Recently Added, Because You Read) built from near-identical hand-rolled card markup (`Button.rowCard` + `Border.rowCover` + `Border.countPill`, repeated three times). This phase is a visual/layout pass over that already-functional screen using Phase 1's design language and Phase 1's `PosterTile` primitive (built in Phase 1, unused by any real screen until now) - no ViewModel/data changes.

**Mid-brainstorm correction (applied immediately, not deferred):** direct user testing surfaced that the live app looked bluish rather than matching the intended dark-amber palette, and asked for the base to go darker - "really dark, if not black." Root cause: Avalonia's `FluentTheme` has its own built-in accent-color system (`ColorPaletteResources.Accent`, driving `TextBox` focus borders, `CheckBox`/`ToggleSwitch` states, `ScrollBar` hover, selection highlights) that Phase 1 never overrode, so every stock-templated control still showed Microsoft's default blue regardless of how complete the `Pb*` token set was. Fixed by setting `FluentTheme.Palettes`'s `Dark` `ColorPaletteResources.Accent` to `{DynamicResource PbAccentColor}` in `App.axaml` (confirmed via Avalonia's own source, not guessed). Simultaneously, `Surface0` moved to literal `#000000` and `Bg`/`Surface1`/`Chrome`/`Surface2`/`Surface3` all shifted darker to match. Both changes are retroactive corrections to Phase 1's token values (`SkinTheme.cs`, `theme.json`, `App.axaml`) - this document doesn't re-litigate them, they're already shipped and verified (build clean, tests passing, crash-free launch, user-confirmed via screenshot).

## Scope

**In scope:**
- Restyle the Spotlight card into a larger, contained hero card using Phase 1's hero-gradient tokens and `PbTextHero` (Bebas Neue) - same underlying data/rotation behavior (`CurrentSpotlight`/`SpotlightItems`/dot indicators/`OpenSpotlightCommand`), no ViewModel changes.
- Replace the three rows' hand-rolled card markup with `PosterTile` instances - the first real screen to consume that Phase 1 primitive.
- Continue Reading additionally uses `PosterTile.ShowProgress`/`ProgressFraction` - the first real use of that primitive's progress-bar slot.
- Restyle the "Try This Reading List" card with Phase 1's surface/radius tokens and swap its tag pills to the `pbChip` primitive.
- Convert `Refresh.png` (the only icon this screen uses) to a vector `PbIconRefresh`.

**Out of scope (deferred):**
- Any change to what data appears or how it's computed (recommendation logic, spotlight selection, continue-reading resume position) - purely visual/layout.
- Search bar stays exactly where and how it looks today (a deliberate "keep it quiet" decision, not an oversight).

## Hero card

Search bar unchanged, at the top. Below it, the existing `Button.spotlightCard`/`CurrentSpotlight` binding gets a new visual treatment: a larger contained card (rounded corners, `PbRadiusLg`) with the cover art as a backdrop, `PbHeroGradientStart`/`PbHeroGradientEnd` as a bottom-up vignette overlay (dark vignette direction from Phase 1 - transparent at the art, fading to `PbSurface0`, never tinted), title in `PbTextHero` (Bebas Neue) over the gradient where today's plain `FontWeight="Bold"` TextBlock sits. The auto-rotation, dot indicators (`ItemsControl` over `SpotlightItems`, active-dot bound via `ObjectConverters.Equal` against `CurrentSpotlight`), and `OpenSpotlightCommand` are unchanged - only the container's size and internal styling change.

## Card rows → PosterTile

Continue Reading, Recently Added, and Because You Read each currently repeat the same `Button.rowCard`/`Border.rowCover`/`Border.countPill` block with a different `ItemsSource`. Each becomes an `ItemsControl` templating `PosterTile` instead: `CoverSource` ← the row's existing cover binding, `TitleText` ← series/issue name, `BadgeText` ← the existing issue-count pill's text. Continue Reading's items additionally set `ShowProgress="True"` and bind `ProgressFraction` to the resume position already available on that row's item view models (confirmed present - the row already shows progress information today, just not as a progress bar). Command wiring (`Command`/`CommandParameter` → whatever each row's tap currently navigates to) moves from `Button.rowCard`'s `Command` onto `PosterTile.Command`/`CommandParameter`, which Phase 1 built exactly for this.

## Try This Reading List - stays distinct

This card has a synopsis and a tag list `PosterTile` has no slots for, so it keeps its own layout rather than being force-fit into the poster-tile shape. It picks up `PbSurface2`/`PbRadiusLg` instead of its current ad-hoc styling, and its tag list swaps from the local `Border.tagPill` style to the `pbChip` primitive from Phase 1 (the exact swap already done for `DetailPills.axaml`'s own tags in Phase 1's audit, extended here).

## Icons

`Refresh.png` converts to a vector `PbIconRefresh`, following the same audit-then-convert approach as every icon phase so far (checked against all other consumers of `Refresh.png` across the app before assigning it a canonical single-action mapping in `icon-mapping.md`).

## Testing

No ViewModel logic changes are anticipated, so existing `HomeScreenViewModelTests` should keep passing unmodified - this is verified, not assumed, by running that suite before and after. Verification otherwise matches Phases 1-2: build clean, full test suite green, crash-free direct-exe launch. Live visual/interactive confirmation is the known computer-use gap in this environment - this phase specifically benefits from the user's own direct testing (as already demonstrated during this phase's brainstorming, where real bugs were caught faster by the user's own screenshot/testing than anything achievable from this side).

## Open questions / deferred

- Exact `PosterTile` sizing/spacing within each row (today's rows use a specific card width/height that may not exactly match `PosterTile`'s current 140px default) is an implementation-time detail, not pre-specified here.
- Whether Continue Reading's resume-position data is already expressed as a 0-1 fraction or needs a small conversion to feed `ProgressFraction` is confirmed during implementation by reading the actual row view model, not guessed here.
