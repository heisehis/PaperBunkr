# Entity pages redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md*

Ground rules for every step:
- **Working copy.** Build on it. `MetadataEntityDetailScreenView.axaml`, `MetadataEntityDetailScreenViewModel.cs` and
  `ContinuityOverviewView.axaml` already carry uncommitted Runs and map work, which must keep working (spec §13).
- **Before UI steps.** Load `paperbunkr-design-language`, and read the matching `avalonia` subskill `SKILL.md` off disk (CLAUDE.md).
- **Tests.** Run only the targeted classes, never two `dotnet test`/`dotnet build` at once, and kill build servers afterwards
  (`dotnet build-server shutdown`).
- **New views.** Every new `.axaml` lands together with its code-behind `.cs` (CLAUDE.md build gotcha).

## Step 1: Relation queries (data)
**Files:**
- `src/Paperbunkr.Data/Metadata/EntityRelations.cs` (new)
- `src/Paperbunkr.Data/Metadata/EntityPageResolver.cs` (edit: expose `CoverIssueIds` for the 5 newest appearances; creator role
  counts as distinct issues)
- `src/Paperbunkr.Data.Tests/EntityRelationsTests.cs` (new)
- `src/Paperbunkr.Data.Tests/EntityPageResolverTests.cs` (edit)

**What:**
- Records: `EntityRelation(Kind, Id, Name, SharedCount, ImageUrl, CustomImagePath, FirstIssueId)`, `CareerRun(Series, SeriesId, Role,
  FirstYear, LastYear, IssueIds, ReadCount)`, `PublisherSeriesRow(Id, Name, Imprint, Owned, Read, CoverIssueId)`,
  `PublisherCatalogue(Series, ImprintCounts, Continuities, ByYear, TopCharacters)`.
- Queries, all taking the already-filtered issue ids (spec §6):
  - `CoAppearances(context, kind, id, issueIds, take)`
  - `SeenHere(context, issueIds, take)`
  - `Collaborators(context, creatorId, issueIds, take)`
  - `CreatorCareer(context, creatorId, issueIds)`: built on `CreatorRunDetector.FindAll`, plus distinct-issue counts per role.
  - `PublisherCatalogue(context, publisherId, continuityId, imprint)`
- Grouped SQL on the `*Appearances` / `CreatorCredits` tables. Never load every issue for a publisher.

**Depends on:** none

**Verify:** `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~EntityRelationsTests|FullyQualifiedName~EntityPageResolverTests"`. Cases:
- ranking and self-exclusion;
- the `take` limit;
- the continuity filter flows through;
- characters ranked before teams for a team page;
- creator counts are distinct issues;
- blank or null imprints are ignored;
- the publisher's top characters limit.

## Step 2: Display helpers and the four bug fixes
**Files:**
- `src/Paperbunkr.App/ViewModels/EntityPage/EntityPageText.cs` (new)
- `src/Paperbunkr.App/ViewModels/MetadataEntityDetailScreenViewModel.cs` (edit)
- `src/Paperbunkr.App.Tests/EntityPage/EntityPageTextTests.cs` (new)

**What:**
- `EntityPageText.Plural(count, one, many)`.
- `RoleName(raw)`: "CoverArtist" → "Cover artist", and unknown values split at their capitals.
- `JoinDetail(params string?[])`: drops empty parts before joining with " · ".
- Wire them into the existing view model so the fixes land before the redesign:
  - the counts line;
  - the event detail;
  - "N appearances" captions;
  - the Roles card.
- `HasRuns` is computed from the creator kind *and* a non-empty list, and `CreatorRuns` is cleared on every load.

**Depends on:** none

**Verify:**
- `EntityPageTextTests`.
- A VM test: load a creator, then a location → `HasRuns` is false (fresh test class `EntityPageViewModelTests`, using the
  `SeriesStructureUiTests` setup pattern for a seeded context).

## Step 3: Kind profiles and the tab model
**Files:**
- `src/Paperbunkr.App/ViewModels/EntityPage/IEntityPageKind.cs` (new)
- `CharacterTeamKind.cs`, `LocationKind.cs`, `CreatorKind.cs`, `PublisherKind.cs` (new, same folder)
- `EntityPageTab.cs` (new)
- `EntityTileItem.cs` (new)
- `src/Paperbunkr.App.Tests/EntityPage/EntityPageKindTests.cs` (new)

**What:**
- `IEntityPageKind` holds:
  - `Overline`;
  - `ImageShape` (Portrait / Wide / Squircle / BrandMark);
  - `CountsLine(EntityPage, extra)`;
  - `PrimaryAction` (Continue / ReadInOrder / ShowInLibrary);
  - which secondary actions show (`ShowsSaveList`, `ShowsMapAction`);
  - `FilterMode` (Continuity / Roles / Imprints);
  - `TabIds` in order;
  - `LeadTabId`.
- `EntityPageTab` (ObservableObject): `Id`, `Label`, `Count`, `IsVisible`, `IsSelected`, `SelectCommand`.
- `EntityTileItem(Kind, Id, Name, Caption, ImagePath, CoverKey, Initials)` with `HasImage` / `ShowMonogram` / `HasInsetCover`.
- `EntityPageKinds.For(ComicMetadataEntityKind)`.

**Depends on:** none

**Verify:** `EntityPageKindTests`:
- each kind's tabs, lead tab, primary action and filter mode match spec §4;
- Team shares Character's profile except the overline.

## Step 4: View model rework
**Files:**
- `src/Paperbunkr.App/ViewModels/MetadataEntityDetailScreenViewModel.cs` (edit). Split into partials if it passes ~500 lines:
  `.Tabs.cs`, `.Relations.cs`, `.ContextMenu.cs`.
- `src/Paperbunkr.App.Tests/EntityPage/EntityPageViewModelTests.cs` (edit)

**What:**
- **Profile:** `Profile` from `EntityPageKinds`, and `Tabs` / `SelectedTab` reset to the lead tab on `Load`. Tab counts come from the loaded
  data; an empty tab is hidden.
- **Worker load:** the build also runs the Step 1 queries for the kind and maps them to `EntityTileItem`, using `EntityImageResolver.Choose`
  for the image, cover inset and initials. The results are:
  - `OftenWith`, `SeenHere`, `Collaborators`;
  - `CareerRuns`, `RoleChips`, `ImprintChips`;
  - `PublisherSeries`, `PublisherContinuities`, `PublisherCharacters`;
  - `YearColumns` (replaces `ByYear`), with `UndatedCount`.
- **Filters:**
  - `SelectedRole` / `SelectedImprint` restart the load the same way `SelectContinuity` does (generation-guarded).
  - The continuity choices carry their counts ("Earth 616 · 30").
- **Appearances:** `AppearanceSort` and `UnreadOnly` → `VisibleAppearances` (in memory). `ContinueLabel` / `ContinueCommand` take the first unread
  in reading order and fall back to Read in order.
- **Header:** `CollageKeys` follows spec Q14.
- **Menus:** the VM implements `IContextMenuProvider`. Its targets:
  - `EntityTileItem`: Open, Open on the map (char/team), Show in Library.
  - Event link: Open event, Open on the map.
  - `CareerRunItem`: Read this run, Save as reading list.
- **Commands:** the Manage-menu actions (`RefreshProfileCommand` added).
- **Kept as is:** the existing `ReadQueue` / `OpenContinuityMap` / runs wiring.

**Depends on:** Steps 1, 2, 3

**Verify:** `EntityPageViewModelTests`:
- the lead tab resets on load and empty tabs are hidden;
- Continue picks the first unread inside the continuity filter;
- unread-only and each sort;
- the role chip narrows the appearances;
- the collage keys;
- the menu entries for each target type.

## Step 5: Shared controls
**Files** (all `.axaml` with their `.axaml.cs`, new), in `src/Paperbunkr.App/Views/EntityPage/`:
- `EntityTile`
- `YearColumnChart`
- `EntitySeriesGrid`
- `EntityEventRow`
- `CareerTimeline`

Tests: `src/Paperbunkr.App.Tests/EntityPage/EntityPageControlsTests.cs` (new).

**What:**
- **`EntityTile`** (spec §5.1):
  - 76×100 art, a fixed two-line name row and a caption.
  - `Shape` property: Portrait / Squircle.
  - Monogram plus a 26×34 inset cover, and a hover lift on the art only.
- **`YearColumnChart`** (§5.4): an `ItemsControl` with a `UniformGrid` of bars whose heights are set by the VM, a tooltip per column, the
  axis captions and the "N undated" caption.
- **`EntitySeriesGrid`:** a plain `ItemsControl` with `controls:VirtualizingWrapPanel` holding `PosterTile`-style poster cells with a progress bar.
  Reuse the existing poster pieces; don't restyle a family.
- **`EntityEventRow`:** the `36,*,Auto` grid with a thumbnail that is always present (event cover or monogram).
- **`CareerTimeline`:**
  - One row per run, with its bar placed by `Grid` star columns that the VM computes (no custom drawing).
  - Clicking a bar opens a `Flyout` holding a `Border.dropdown` with `PopupFit.ToWindow`: range, read count, Read this run / Save as
    reading list.

**Depends on:** Step 3 (`EntityTileItem`). Can run in parallel with Steps 1, 2 and 4.

**Verify:**
- `EntityPageControlsTests`, headless: tiles with a 1-line name and a 2-line name have the same measured height; the monogram shows when
  there's no image and the inset when there's a cover key; the chart gives the tallest bar full height.
- `XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`.

## Step 6: Screen view rewrite
**Files:**
- `src/Paperbunkr.App/Views/MetadataEntityDetailScreenView.axaml` and `.axaml.cs` (rewrite)
- New panels in `src/Paperbunkr.App/Views/EntityPage/` (each `.axaml` with its `.axaml.cs`):
  - `EntityOverviewPanel` (character/team)
  - `LocationOverviewPanel`
  - `CreatorCareerPanel`
  - `EntityAppearancesPanel` (also used as Credits)
  - `EntityEventsPanel`
  - `PublisherSeriesPanel`
  - `PublisherContinuitiesPanel`
  - `EntityCharactersPanel`

**What:**
- **Root:** one `ScrollViewer`. In it: the back link, then `controls:HeroBand CoverKeys="{Binding CollageKeys}"`, holding the image slot
  (switched by `Profile.ImageShape`), the text block, the action `WrapPanel` and the filter row (continuity `segGroup` / `SuggestBox`, role
  chips or imprint chips).
- **Tabs:** a `StackPanel Spacing="20"` of `Button.tab`. Panels are switched by `IsVisible` + `controls:Replace.On`.
- **Wiring:** `controls:ContextMenuHost.Provider="{Binding}"` on the root; the existing `ScreenInput` / `Refresh` wiring stays.
- **Removed:** the old two-column card grid and every inner `ListBox`/`ScrollViewer`.
- **Loading / error / empty:** per spec §10, with each message collapsed when it's empty.

**Depends on:** Steps 4, 5

**Verify:**
- `EntityPageViewTests` (new, headless):
  - for each kind the expected tabs are visible;
  - no `ScrollViewer` exists inside a tab panel;
  - a seeded 1,000-appearance character realizes fewer than 100 `CoverThumb`s on the Appearances tab;
  - the existing `SeriesStructureUiTests` still pass (Runs).
- Lint set again.
- Renders: `FoundationStyleTests.WithFoundation` PNGs per kind on default, Daylight and Matrix at 1366×768 (`PB_RENDER_OUT`).

## Step 7: Continuity page strips
**Files:**
- `src/Paperbunkr.App/Views/ContinuityOverviewView.axaml` (edit)
- `src/Paperbunkr.App/ViewModels/ContinuityPageViewModel.cs` (edit)
- `src/Paperbunkr.App/Services/ContinuityScreen/ContinuityOverview.cs` (edit)
- `src/Paperbunkr.App.Tests/ContinuityScreen/ContinuityScreenViewTests.cs` (edit; it already has uncommitted changes, so add to it rather
  than rewrite)

**What:**
- `EntityTileTemplate` becomes `views:EntityTile`, over `OverviewEntity` mapped to `EntityTileItem`.
- Delete the `entityTileMap` button and its style.
- `ContinuityPageViewModel` implements `IContextMenuProvider`: Open / Open on the map / Show in Library, using the existing
  `OpenEntityCommand` / `OpenEntityOnMapCommand`.
- Add `ContextMenuHost.Provider` on the overview root, if the screen doesn't already have it.

**Depends on:** Step 5

**Verify:**
- `ContinuityScreenViewTests` and `ContinuityScreenViewModelTests`: the strip shows tiles; the menu has Open on the map; there's no
  `entityTileMap` element.

## Step 8: Detail chip menu
**Files:**
- `src/Paperbunkr.App/ViewModels/TagPillContextMenuBuilder.cs` (edit)
- `src/Paperbunkr.App/ViewModels/TagPillViewModel.cs` (edit: optional `SearchLibraryCommand` for entity pills)
- `src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs` and `DetailTabsViewModel.cs` (edit: pass `_goLibraryWithSearch` into entity pills)
- Tests: the existing `TagPillContextMenuBuilder` tests, if any (find them with Grep), otherwise a new `TagPillContextMenuBuilderTests.cs`

**What:** a pill with an `EntityKind` gets the rows **Open <name>** and **Search library**, then a separator and the weight rows when
`CanReweight` is true. Non-entity pills don't change (spec §8).

**Depends on:** none

**Verify:**
- Builder tests: entity pill rows, a reweightable entity pill, a plain pill unchanged.
- `DetailBandViewModelTests`.

## Step 9: Finish
**Files:**
- `docs/paperbunkr-todo.md` (edit: Beta backlog entry with status and what was verified)
- Memory note (new project memory + `MEMORY.md` line)

**What:**
- Run the `avalonia-pro-max/review-checklist`.
- Keyboard pass in `RealWindowKeyboardTests`: the tabs step with Ctrl+PageDown; tile and grid arrows don't drop focus (take them on the
  tunnel if they do).
- Build into `bin` for the user (`dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`, checking the XAML weave ran) and hand over for the
  on-screen check: Storm, Castle Doom, a creator and Marvel.

**Depends on:** Steps 6, 7, 8

**Verify:** the user's on-screen check is the only real proof of the look. Report anything not seen on screen as unverified.

## Order and parallelism

1, 2, 3, 5 and 8 don't depend on each other. 4 needs 1–3; 6 needs 4 and 5; 7 needs 5; 9 is last. Working in this one checkout (no new
worktrees: disk space), the order is 1 → 2 → 3 → 5 → 4 → 6 → 7 → 8 → 9.
