# Home screen pitch — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-28-home-improvements-design.md (B) and 2026-09-28-home-cosmetics-design.md (A)*

The working tree is shared with other live sessions (LibraryScreen was being edited at plan time). Keep edits surgical in
shared files (`MainViewModel.cs`, `AppSettings.cs`, `PaperbunkrDbContext.cs`, `App.axaml`, `ThemeService.cs`,
`PreferencesScreenViewModel.cs`), and re-check `git status` and the newest migration right before generating ours.

## Step 1: hero-tilt render gate
**Files:** Paperbunkr.App.Tests/HeroTiltRenderGateTests.cs (new)
**What:** Render a 142×206 solid-colour `Border` headless, with and without `Rotate3DTransform(-10°, 3°, depth 900)`. Assert
that the rendered pixels differ (the right edge column changes) and nothing throws. Record the verdict in spec A's
implementation notes.
**Depends on:** none. **Verify:** the test itself.

## Step 2: data layer
**Files:**
- `Paperbunkr.Data/Entities/AppSettings.cs`: `HomeSectionOrder`, `HomeHiddenSections`, `HomeSeasonalFlourish`
- `Entities/DismissedRecommendation.cs` (new) and `PaperbunkrDbContext.cs` (DbSet + unique index)
- Migration `AddHomeCustomization` (no-op `Down()` for the columns; drop the table)
- `Metadata/DismissedRecommendations.cs` (new)
- `Metadata/InsightsResolver.cs` (`AttentionSeries.IsStalled`)
- `Metadata/HomeFeedResolver.cs`:
  - `ResumeCandidate`, `GetContinueReadingMixed`
  - `HomeAttention`, `GetTopAttention`
  - `GetSpotlightPicks` per-series cap, new-arrival slots, interleave
**Depends on:** none.
**Verify:** new or extended Data.Tests: `HomeFeedResolverTests`, `InsightsResolverTests`, `DismissedRecommendationsTests`,
and the `AddHomeCustomization` migration test.

## Step 3: pure App helpers
**Files:**
- `Models/HomeSectionKey.cs` + `Models/HomeLayout.cs`
- `Services/DayPhase.cs`
- `Services/SeasonalCalendar.cs`
- `Controls/ParallaxMath.cs`
- `Services/SpotlightAccentSampler.cs` (vibrant colour)
**Depends on:** none.
**Verify:** new unit tests for each.

## Step 4: theme resources
**Files:**
- `Services/ThemeService.cs`: `IsLightTheme`, and `PbHeroScrimColor` written in `ApplyThemeResources`
- `App.axaml`: `PbHeroScrimColor` default, `PbSky*`, `PbSeason*`
**Depends on:** none.
**Verify:** `ThemeServiceTests` additions (scrim and `IsLightTheme` follow the mode).

## Step 5: section model and HomeScreenViewModel
**Files:**
- `ViewModels/Home/*SectionViewModel.cs` (new)
- `Models/HomeResumeCard.cs` (new; replaces `HomeContinueReadingCard` and `HomeBookCard` in the row)
- `Models/HomeCollectionCard.cs` (mosaic keys)
- `Models/BecauseYouReadRow.cs` (seed card)
- `Models/SeriesCardSample.cs` (`IsRecentlyAdded`; drop `RecentAddBadgeLabel`)
- `ViewModels/HomeSpotlightHeaderSource.cs` (pinned-pick mode)
- `ViewModels/HomeScreenViewModel.cs`: layout-aware snapshot, `Sections`, greeting and phase timer, sky/seasonal state,
  dismiss and Undo, attention, rotation pause reasons, layer A/B spotlight sources
**Depends on:** Steps 2, 3 and 4.
**Verify:** `HomeScreenViewModelTests` updated and extended.

## Step 6: effect controls and DetailHero
**Files:**
- `Controls/ParallaxBackdrop.cs`, `Controls/ScrollReveal.cs`, `Controls/HeroTilt.cs` (new)
- `Views/DetailHero.axaml(.cs)`: `ParallaxBackdrop` backdrop, `PbHeroScrimColor`, `TiltCover`
**Depends on:** Step 3 (`ParallaxMath`) and Step 1 (tilt verdict).
**Verify:** Reduced-Motion-at-rest tests, plus `DetailHero` still rendering in the existing Detail tests.

## Step 7: PosterTile
**Files:** `Views/PosterTile.axaml(.cs)`, `Styles/Primitives.axaml`
**What:** squircle bar plus one-shot fill; NEW ribbon (top-left, since the badge already owns top-right); mosaic grid;
deeper static shadow; dismiss ✕; book glyph.
**Depends on:** none.
**Verify:** headless view tests.

## Step 8: Home views
**Files:**
- `Views/HomeScreen.axaml(.cs)`
- `Views/Home/HomeStyles.axaml` and `Views/Home/*Section.axaml` (resource dictionaries, no `x:Class`)
- `Views/HomeRecommendationContextMenuBuilder.cs` (new)
**What:** masthead (greeting, sky/cover wall, parallax, seasonal); `Sections` `ItemsControl`; crossfade layers; zoom via
`RunAsync`; attach/detach timer pause; scroll-reveal.
**Depends on:** Steps 5, 6 and 7.
**Verify:** build with a real weave check (launch or `-v:diag`), and headless Home render tests.

## Step 9: wiring
**Files:** `ViewModels/MainViewModel.cs` (toast host, Insights nav, Preferences-anchor nav passed to Home)
**Depends on:** Step 5.
**Verify:** `MainViewModelTests` still pass.

## Step 10: Preferences › Appearance › Home
**Files:**
- `Views/Preferences/AppearanceSection.axaml` (`appearance.home` block)
- `ViewModels/PreferencesScreenViewModel.cs` (section rows, move/hide/reset, seasonal, hidden recommendations, Unhide)
- `Models/PreferenceIndex.cs`
**Depends on:** Steps 2 and 3.
**Verify:** `PreferencesScreenViewModelTests` additions.

## Step 11: finish
- Full build and both test suites.
- `avalonia-pro-max/review-checklist`.
- Fill both specs' implementation notes.
- Update the Roadmap's Home pitch entry and `paperbunkr-todo.md`.
- Hand over for on-screen checking (no FlaUI without permission).
