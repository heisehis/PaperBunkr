# Home Screen — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-24-home-screen-design.md*

Verified directly against the current codebase before writing this plan (not guessed):
- `PosterTile` (`src/Paperbunkr.App/Views/PosterTile.axaml` + `.axaml.cs`) is a real `UserControl`
  already shipped in Phase 1: `CoverSource` (`IImage?`), `TitleText`, `MetaText`, `BadgeText`
  (null/empty hides the badge slot), `ShowProgress` (bool), `ProgressFraction` (double 0-1),
  `Command`, `CommandParameter`. Root is a `Border` with `Classes="posterTile"` — **not** a
  `Button` — it handles its own `PointerPressed → Command.Execute`.
- `HomeContinueReadingCard` (`src/Paperbunkr.App/Models/HomeContinueReadingCard.cs`) today only has
  `Series`/`ResumeIssueId` — **no 0-1 progress fraction exists yet**. The design doc's own "Open
  questions" flagged this as possibly needed; confirmed needed by reading the model directly.
  `IssueMetadataExtensions.ReadPercentage()` (`src/Paperbunkr.Data/Metadata/IssueMetadataExtensions.cs`)
  returns 0-100, clamped — `PosterTile.ProgressFraction` wants 0-1, so this is a divide-by-100, not
  a re-derivation. `HomeFeedResolver.GetContinueReading` (`src/Paperbunkr.Data/Metadata/HomeFeedResolver.cs`)
  already returns `ContinueReadingCandidate(Series, Issue ResumeIssue)` with `ResumeIssue` fully
  loaded (`PageCount`/`LastPageRead` populated via `Include(s => s.Issues)`), so
  `candidate.ResumeIssue.ReadPercentage()` is callable with no extra query.
- **Real regression found, not anticipated by the design doc:** `HomeScreenTests.cs`
  (`src/Paperbunkr.App.UiTests/HomeScreenTests.cs`)'s `RecentlyAddedCard_Click_NavigatesAwayFromHome`
  locates a row card via `FindFirstDescendant(cf => cf.ByControlType(ControlType.Button))` then
  calls `.AsButton().Invoke()` — this only works because every clickable element in this codebase
  so far has been a real `Button` (confirmed by grepping every `.AsButton()` call site in
  `Paperbunkr.App.UiTests` — no exceptions). `PosterTile`'s root is a `Border`, not a `Button`, and
  its click handling is a raw `PointerPressed` handler, not a UIA `InvokePattern` implementation —
  `ByControlType(ControlType.Button)` would find nothing once Step 4 lands, and even a found `Border`
  can't be `.AsButton()`'d. This is the first screen to expose `PosterTile` to real UI automation
  (Phase 1's showcase view is `#if DEBUG`-only, no UI tests), so the gap was real, not previously
  triggered. Fixed by giving `PosterTile`'s root `Border` a stable `AutomationProperties.AutomationId`
  and switching the test to FlaUI's `AutomationElement.Click(bool)` (confirmed present on the base
  `AutomationElement` class via `FlaUI.Core.xml`'s doc comments, not `Button`-specific — it performs
  a physical click at the element's clickable point, which works regardless of UIA pattern support).
  See Step 4.
- `Icons.axaml`/`icon-mapping.md` pattern confirmed by reading both files directly: hand-computed
  `StreamGeometry` (not pixel-traced), added to `Styles.Resources`, consumed via
  `Path Data="{StaticResource PbIconX}" Classes="pbIcon"`; `icon-mapping.md` has one Markdown table
  per phase under "Converted to vector", plus a "Still raster" inventory list to prune from.
- `App.axaml` already has `PbHeroGradientStartColor` (`#00000000`) / `PbHeroGradientEndColor`
  (`#FF000000`), `PbRadiusLg` (14, `CornerRadius`-typed), `PbMotionFast`/`PbMotionEase`, and the
  mid-brainstorm palette correction (`PbSurface0Color` `#000000`, `FluentTheme` dark-palette accent
  bound to `PbAccentColor`) — all already shipped, nothing to redo.
- `Primitives.axaml`/`Typography.axaml` confirmed: `Border.pbChip` (chip/tag), `TextBlock.pbTextHero`
  (Bebas Neue via `PbDisplayFontFamily`, 42px), `Button.primary` — all real, all usable as-is.
- Build is currently clean (`dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` — 0 warnings, 0
  errors) as of the start of this planning pass.

## Step 1: `HomeContinueReadingCard.ResumeProgressFraction`

**Files:** `src/Paperbunkr.App/Models/HomeContinueReadingCard.cs` (edit), `src/Paperbunkr.App/ViewModels/HomeScreenViewModel.cs` (edit)

**What:** Add `public double ResumeProgressFraction { get; init; }` to `HomeContinueReadingCard`. In
`HomeScreenViewModel.LoadFromDatabase`, when building each card from a `ContinueReadingCandidate`,
set `ResumeProgressFraction = candidate.ResumeIssue.ReadPercentage() / 100.0`. This is the one real
(small) data addition the design doc's "Open questions" section flagged as possibly needed — it's
needed. No other `HomeScreenViewModel` logic changes.

**Depends on:** none

**Verify:** New `HomeScreenViewModelTests` case: seed an issue with `lastPageRead: 30, pageCount: 100`,
assert `vm.ContinueReading[0].ResumeProgressFraction == 0.30`. Existing
`Construct_PopulatesContinueReading_WithSeriesCardAndCorrectResumeIssue` and
`OpenContinueReadingCommand_InvokesGoReaderForIssue_WithTheResumeIssueId` keep passing unmodified
(neither touches the new field).

## Step 2: `PbIconRefresh`

**Files:** `src/Paperbunkr.App/Styles/Icons.axaml` (edit), `src/Paperbunkr.App/Assets/Icons/icon-mapping.md` (edit)

**What:** Grep every `.axaml` for `Refresh.png` first (this screen's `Border.heroIcon` is the only
current consumer per the design doc's own Icons section — confirm, don't assume). Add a
hand-computed `StreamGeometry` named `PbIconRefresh` to `Icons.axaml`'s `Styles.Resources` (a
circular double-arrow glyph on the same 24x24 viewbox as the existing set, stroked-outline style,
matching every other icon added so far — not traced from the PNG). Add a new "Phase 3 (Home
screen)" row/section to `icon-mapping.md`'s "Converted to vector" tables (`Refresh` →
`PbIconRefresh` → `Refresh.png`), and remove `Refresh` from the "Still raster" inventory list.

**Depends on:** none

**Verify:** Visual check once wired into Step 5's hero header markup.

## Step 3: Existing Home tests stay green as a baseline

**Files:** none — verification only, run before Steps 4-6 land

**What:** Run `dotnet test --filter HomeScreenViewModelTests` and note the pre-change baseline
passes, so any later failure introduced by Steps 4-6 is attributable to those steps, not pre-existing
flake. (Folded in as a checkpoint rather than a real step — the actual assertion work is Steps 1 and
4's own `Verify` lines.)

**Depends on:** Step 1
**Verify:** `dotnet test --filter HomeScreenViewModelTests` green.

## Step 4: Card rows → `PosterTile` (Continue Reading, Recently Added, Because You Read)

**Files:** `src/Paperbunkr.App/Views/HomeScreen.axaml` (edit), `src/Paperbunkr.App/Views/PosterTile.axaml` (edit), `src/Paperbunkr.App.UiTests/HomeScreenTests.cs` (edit)

**What:**
- `PosterTile.axaml`: add `AutomationProperties.AutomationId="PosterTileCard"` to the root `Border`
  (`x:Name="Root"`). Shared across every instance app-wide by design — this is the first screen to
  put `PosterTile` in front of UI automation, and Library's own grid (Phase 4) will need the exact
  same hook, so this isn't a Home-specific hack.
- `HomeScreen.axaml`: replace all three rows' `DataTemplate`s (`Button.rowCard`/`Border.rowCover`/
  `Border.countPill`) with `<views:PosterTile>`:
  - **Continue Reading** (`x:DataType="models:HomeContinueReadingCard"`): `CoverSource="{Binding
    Series.CoverIssueId, Converter={x:Static views:CoverImageConverter.Instance}}"`,
    `TitleText="{Binding Series.Name}"`, `BadgeText="{Binding Series.IssueCountLabel}"`,
    `ShowProgress="True"`, `ProgressFraction="{Binding ResumeProgressFraction}"` (Step 1),
    `Command="{Binding $parent[UserControl].((vm:HomeScreenViewModel)DataContext).OpenContinueReadingCommand}"`,
    `CommandParameter="{Binding ResumeIssueId}"`.
  - **Recently Added** and **Because You Read**'s inner per-row `ItemsControl` (both
    `x:DataType="models:SeriesCardSample"`): `CoverSource="{Binding CoverIssueId, Converter=...}"`,
    `TitleText="{Binding Name}"`, `BadgeText="{Binding IssueCountLabel}"`, `ShowProgress` omitted
    (defaults false), `Command="{Binding $parent[UserControl].((vm:HomeScreenViewModel)DataContext).OpenSeriesCommand}"`,
    `CommandParameter="{Binding}"`.
  - Remove the now-unused `Button.rowCard`/`Border.rowCover`/`Border.countPill` style blocks from
    `HomeScreen.axaml`'s `UserControl.Styles`.
  - The three `ItemsControl`s' own `AutomationProperties.AutomationId`
    (`HomeContinueReadingList`/`HomeRecentlyAddedList`/nested Because-You-Read rows) and each row's
    `ItemsPanelTemplate`/horizontal `ScrollViewer` wrapper are unchanged.
- `HomeScreenTests.cs`: fix `RecentlyAddedCard_Click_NavigatesAwayFromHome` (the real regression
  found above) — replace
  `.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))!` with
  `.FindFirstDescendant(cf => cf.ByAutomationId("PosterTileCard"))!`, and replace
  `card.AsButton().Invoke()` with `card.Click()`.

**Depends on:** Step 1 (Continue Reading's `ProgressFraction` source), Step 2 (not a hard dependency,
but do Step 2 first so nothing here references a not-yet-existing icon key — n/a for this step
specifically, no icon usage in the rows themselves)

**Verify:** `dotnet build`; `dotnet test --filter HomeScreenViewModelTests` green (pure ViewModel,
untouched by this step); `dotnet test --filter HomeScreenTests` (UI automation) green, specifically
confirming `RecentlyAddedCard_Click_NavigatesAwayFromHome` still passes with the new lookup; manual
on-screen check that all three rows render real `PosterTile` cards with covers/badges/progress bar
(Continue Reading only).

## Step 5: Hero card restyle (Spotlight → hero)

**Files:** `src/Paperbunkr.App/Views/HomeScreen.axaml` (edit)

**What:** Search bar and refresh button stay exactly where/how they look today (explicit in the
design doc — "keep it quiet", not an oversight). Below the header, restyle the existing
`Button.spotlightCard`/`CurrentSpotlight` block into a larger contained hero:
- New `Border.heroCard` style (replaces reliance on `Button.spotlightCard` for this one instance;
  `Button.spotlightCard` itself stays, still used by Step 6's Try-This-Reading-List card) —
  `CornerRadius="{DynamicResource PbRadiusLg}"`, `ClipToBounds="True"`, taller/wider than today's
  fixed 120x172 cover slot (e.g. full card width, ~260 height — exact figure is an implementation-time
  detail per the design doc's own "PosterTile sizing... is an implementation-time detail" precedent
  extended to this card too).
- Inside: a `Panel` with (1) `Image Source="{Binding CurrentSpotlight.CoverImage}" Stretch="UniformToFill"`
  filling the card as backdrop, (2) a vignette `Border` on top with
  `Background` set to a vertical `LinearGradientBrush` from `{StaticResource PbHeroGradientStartColor}`
  (top, transparent) to `{StaticResource PbHeroGradientEndColor}` (bottom, opaque) — matches the
  design doc's explicit "transparent at the art fading to PbSurface0 at the bottom/edges, never
  tinted" — (3) title/CTA text anchored bottom-left over the gradient.
- Title `TextBlock` (`{Binding CurrentSpotlight.Title}`) gets `Classes="pbTextHero"` in place of
  today's plain `FontWeight="Bold"` — this is the first real (non-showcase) consumer of
  `PbTextHero`/Bebas Neue. Series-name kicker (`CurrentSpotlight.SeriesName`) and "Read Now" stay
  plain-styled `TextBlock`s (`pbTextCaption`/`PbAccentTextBrush` respectively), unchanged in kind.
- Auto-rotation (`_spotlightTimer`), dot indicators (`SpotlightItems`/`SetSpotlightItemCommand`/
  active-dot `ObjectConverters.Equal` binding), and `OpenSpotlightCommand` — all unchanged, same
  bindings, just re-parented into the new visual container. The card root stays a `Button`
  (`AutomationProperties.AutomationId="HomeSpotlightCard"` unchanged) — `HomeScreenTests.
  SpotlightCard_Click_NavigatesAwayFromHome`'s existing `.AsButton().Invoke()` keeps working
  unmodified, no regression here (unlike Step 4 — this card was never migrated to `PosterTile`, the
  design doc explicitly keeps it as its own hero shape).
- Refresh icon: swap `Border.heroIcon`/`ImageBrush Source="/Assets/Icons/Refresh.png"` for
  `<Path Data="{StaticResource PbIconRefresh}" Classes="pbIcon" />` (Step 2).

**Depends on:** Step 2 (icon)

**Verify:** `dotnet build`; `dotnet test --filter HomeScreenTests` (confirms
`SpotlightCard_Click_NavigatesAwayFromHome` and `AllFiveModules_RenderTheirEmptyStates_OnAFreshLibrary`
— the latter asserts `HomeSpotlightCard` is `Null` on an empty library, i.e. the restyled card must
still respect `HasSpotlight`'s `IsVisible` gating exactly as today); manual on-screen check
(backdrop/vignette/Bebas Neue title render correctly, rotation/dots/click still work) — flagged
manual-only per this phase's established computer-use limitation.

## Step 6: "Try This Reading List" restyle

**Files:** `src/Paperbunkr.App/Views/HomeScreen.axaml` (edit)

**What:** Keeps its own distinct layout (synopsis + tag list `PosterTile` has no slots for — design
doc is explicit this card is not force-fit into `PosterTile`). `Button.spotlightCard` picks up
`Background="{DynamicResource PbSurface2Brush}"` and `CornerRadius="{DynamicResource PbRadiusLg}"`
in place of its current `PbChromeBrush`/`10`. Tag list: replace the local `Border.tagPill` style
usage with `Classes="pbChip"` (`Border.pbChip` from `Primitives.axaml`, same swap already done for
`DetailPills.axaml` in Phase 1). Remove the now-unused `Border.tagPill` style block from
`HomeScreen.axaml`'s `UserControl.Styles`. `Border.spotlightCover` (the 120x172 cover slot) is
unchanged — this card's cover treatment isn't part of the hero redesign.

**Depends on:** none (independent of Steps 4-5)

**Verify:** `dotnet build`; `dotnet test --filter HomeScreenTests` (`AllFiveModules_RenderTheirEmptyStates_OnAFreshLibrary`
asserts `HomeReadingListSpotlightHeader` present / `HomeReadingListSpotlightCard` absent on empty —
must still hold); manual on-screen check of the restyled card + chip tags.

## Step 7: Final pass

**Files:** none new — verification only

**What:** Full `dotnet build` + `dotnet test` run. Grep `HomeScreen.axaml` for any leftover
`Border.rowCard`/`Border.rowCover`/`Border.countPill`/`Border.tagPill` references to confirm the
Step 4/6 style removals didn't leave a dangling selector. Re-read the design doc's Testing section
and confirm every item was actually done: `HomeScreenViewModelTests` unmodified-and-passing (Step
1's new case aside), full test suite green, manual visual/interactive checklist from Steps 4-6
complete or explicitly flagged as user-verification-needed.

**Depends on:** Steps 1-6

**Verify:** `dotnet build` clean, `dotnet test` green, grep confirms no dangling style references,
manual checklist from Steps 4-6 complete (or explicitly deferred to the user, same honest framing
Phases 1-2 used for their own computer-use limitation).
