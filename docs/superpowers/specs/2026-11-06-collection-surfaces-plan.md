# Collection surfaces — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-06-collection-surfaces-design.md*

The tree carries many uncommitted edits from other work: touch only the files named here, run only targeted tests, never run two `dotnet build`/`test` at once (check `Get-Process testhost`),
never regenerate a migration the user has launched. Before XAML read the `avalonia` layout/styling/data-template/accessibility subskills and, at the end, `avalonia-pro-max/review-checklist` from
`~/.claude/skills/avalonia/...`; load `paperbunkr-design-language`. Each UI step ends with a build of `src/Paperbunkr.App` (prove `CompileAvaloniaXaml` ran) and the lint trio
`--filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~SharedControlsStyleTests"` (the ComicVine `FamilyButtonsCarryNoSizeOverrides` failure is known).

## Step 1: Setting + migration
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit: `ShowCollectionBadges`, default false), `src/Paperbunkr.Data/Migrations/<ts>_AddCollectionBadges.cs` + `.Designer.cs` + snapshot (generated), `src/Paperbunkr.Data.Tests/AddCollectionBadgesMigrationTests.cs` (new)
**What:** add the property, scaffold with `dotnet ef migrations add AddCollectionBadges --project src/Paperbunkr.Data` (confirm `Up` is not empty), make `Down()` a no-op for the `AppSettings` column.
**Depends on:** none
**Verify:** new migration test (default false, no pending model changes); existing collection migration tests still pass.

## Step 2: Continue resolver (Home hero data)
**Files:** `src/Paperbunkr.Data/Collections/CollectionContinueResolver.cs` (new), `src/Paperbunkr.Data.Tests/CollectionContinueResolverTests.cs` (new)
**What:** `GetHero(context, IReadOnlyList<int> collectionIds)` returns `(int CollectionId, int? ContinueIssueId, string? ContinueLabel)?`: the collection whose members have the newest `Issue.OpenedTime` (series members' issues + standalone issue members; ties by the given order), and its next unread issue = the first manual member in collection order that has one (series: first unread by number with the same "unread = `LastPageRead` null or 0" test the cards use; standalone issue if unread; books skipped). Label "Series #N". One pass, no per-row queries beyond the member includes.
**Depends on:** none
**Verify:** Data tests: newest-opened wins, tie order, next unread by collection order, series first-unread by number, nothing unread = hero without chip, no activity = null, books skipped, rule-matched members ignored (manual order only).

## Step 3: Membership map (badges) and tile source interface
**Files:** `src/Paperbunkr.Data/Collections/CollectionMembershipResolver.cs` (new), `src/Paperbunkr.App/Models/ITileCollectionSource.cs` (new: `CollectionBadges`, `CollectionBadgeTooltip`) + `CollectionBadge.cs` (new record: accent hex or null, name), `IssueCardSample.cs` / `SeriesCardSample.cs` (edit: implement it), tests: `CollectionMembershipResolverTests.cs` (Data, new), `CollectionBadgeSourceTests.cs` (App, new)
**What:** `GetMemberships(context)` -> `Dictionary<int, List<int>>` series id -> collection ids and issue id -> collection ids (manual items plus rule matches, built from one `CollectionItems` pass plus `GetMembers` only for collections with a rule slot - the same set already resolved for sidebar counts). The two samples expose a settable prebuilt `IReadOnlyList<CollectionBadge>` (shared list per series, empty by default) and the tooltip text; an issue tile merges its own and its series' memberships.
**Depends on:** none (Step 1 only for the setting read in Step 4)
**Verify:** Data tests: manual vs rule-matched, a series in several collections, issue inherits its series' memberships, removed collection disappears; App tests: both samples report badges/tooltip, empty by default.

## Step 4: Library view-model state (banner, empty, badges)
**Files:** `ViewModels/LibraryScreenViewModel.cs` (edit: load `ShowCollectionBadges` + memberships in `ReadLibrary`/`LibraryLoad`, push badges onto samples when building tiles, `ActiveCollectionBanner` state), `Models/CollectionBannerState.cs` (new: name, glyph, accent, description blocks, stat chips, % read text, cover tile, `IsSmart`, `IsPinned`), tests: `CollectionBannerTests.cs` (new)
**What:** when `_activeCollectionId` is set build the banner state in the load (stats from `_activeCollectionMembers`/counts by kind; % from `CollectionProgressResolver.GetProgress` for that one id regardless of `ShowCollectionProgress`; description via `MarkdownLite.ParseDescription`; cover via `GetCoverHint` + `LibraryTile.FromMember`); `IsActiveCollectionEmpty` (no manual items and no rule matches) distinct from `HasAnyResults`; `IsBannerVisible`; commands reuse `OpenCollectionPropertiesCommand`/`TogglePinCollectionCommand`; a `DescriptionExpanded` toggle. Badges are attached only when the setting is on and no collection is the active filter.
**Depends on:** Steps 1, 3
**Verify:** view-model tests: banner appears only for an active collection, stats and the % with the sidebar setting off, empty flag vs filtered-empty, Pin updates the banner, badges attached only when enabled and not in a collection view.

## Step 5: Badge drawing in the tile overlay
**Files:** `Views/TileCosmeticsOverlay.cs` (edit), tests: `TileCosmeticsOverlayBadgeTests.cs` (new)
**What:** read `ITileCollectionSource` from `DataContext`; draw up to three 6 px dots (3 px apart) bottom-left, "+N" beyond three, never over the bottom-right rating/dog-ear or the progress bar; the generic accent resource for an unset accent; no new bindings, no allocation per render. A pure layout helper (dot rects for a given count and size) so it is testable.
**Depends on:** Step 3
**Verify:** layout-helper tests (0/1/3/5 badges, clear of the progress bar and corners); headless render of poster and panorama tiles with badges in default/daylight.

## Step 6: Banner control and the Library row
**Files:** `Views/CollectionBanner.axaml` + `.axaml.cs` (new), `Views/LibraryScreen.axaml` (edit: `RowDefinitions="Auto,Auto,*"`, banner in row 1, move the root-level row-1 children to row 2), `Views/LibraryScreen.axaml.cs` (edit if it references row 1, e.g. the pinned group header lookup), tests: `CollectionBannerRenderTests.cs` (new)
**What:** the banner per the spec (blurred cover under a scrim token, glyph + name `pbTextHeading`, `MarkdownView` description with `LinkCommand="{x:Static services:ExternalLinks.OpenWebCommand}"` capped at three lines with more/less, stat chips `pbChip`, Edit/Pin `iconButton`s with names, compact single-line mode under 700 px height). Move only **root-level** children of `RootGrid` (the 8-space-indented elements with `Grid.Row="1"`; there are 24 `Grid.Row="1"` matches in the file but most belong to nested grids - check each by indentation/parent) and the `PinnedGroupHeader` (`Grid.Row="1"`, L~1926), then re-check the code-behind for `Grid.GetRow` uses.
**Depends on:** Step 4
**Verify:** build + lint; `LibraryScreenViewModelTests`, `FocusGapsTests`, `LibraryOpenKeysTests` and the Library cases of `RealWindowKeyboardTests` (not the two Slow sweeps) still pass; headless render of the Library with a collection active (cover + description, no cover/description, mixed grid, normal grid) in default/daylight/matrix.

## Step 7: Empty-state card
**Files:** `Views/CollectionEmptyState.axaml` + `.axaml.cs` (new), `Views/LibraryScreen.axaml` (edit: host it in row 2 over the grids when `IsActiveCollectionEmpty`), tests: extend `CollectionBannerRenderTests`
**What:** centred card (glyph tinted by the accent, "Nothing in {name} yet", the Add-to-Collection / rule line, "Edit collection…" and "Go to All Series" buttons with names). The existing filtered-empty message stays for other cases.
**Depends on:** Steps 4, 6
**Verify:** render + a view-model test that an empty collection shows it and a filtered-empty one does not.

## Step 8: Preferences toggle for badges
**Files:** `ViewModels/PreferencesScreenViewModel.cs` (edit), `Views/Preferences/AppearanceSection.axaml` (edit)
**What:** `ShowCollectionBadges` toggle row beside the collection progress ring row, persisted through `PersistBehaviorSetting`, loaded with the other cosmetics.
**Depends on:** Step 1
**Verify:** build + lint; confirm the row appears in the Appearance render; the setting round-trips (migration test + a tiny `AppSettings` read in the Library badge test).

## Step 9: Home hero card
**Files:** `Models/HomeCollectionCard.cs` (edit: `IsHero`, `ContinueIssueId`, `ContinueLabel`), `ViewModels/HomeScreenViewModel.cs` (edit: call `CollectionContinueResolver`, mark the first card, `ContinueCollectionCommand` -> `_goReaderForIssue`), `Views/Home/CollectionHeroCard.axaml` + `.axaml.cs` (new), `Views/Home/CollectionsSection.axaml` (edit: hero first, then the existing `PosterTile`s), tests: `HomeCollectionHeroTests.cs` (new)
**What:** a wide card (cover per the collection's style via the Slice 2 mosaic/fan data, glyph + name, `N items`, ring when enabled, "Continue" chip as a real `Button` naming the issue); no hero when the resolver returns null or the Collections section is hidden; keyboard order hero then cards.
**Depends on:** Step 2
**Verify:** view-model tests (hero chosen, chip target, no hero without activity, the rest of the cards unchanged); headless render of the shelf with a hero in default/daylight/matrix; lint.

## Step 10: Review, docs and on-screen pass
**Files:** `docs/paperbunkr-todo.md`, the spec's "Implementation notes", this plan's status, the memory file
**What:** run `avalonia-pro-max/review-checklist`; run the new Data and App test classes, the Library/Home view-model classes and the lint trio - not the full suite. Then the user's 1366x768 screen check (banner with and without a cover/description, more/less, Edit/Pin, empty collection, badges on/off, Home hero and its Continue chip) and record verified vs unverified in the roadmap entry by hand; note that item 16 was dropped and 15 deferred.
**Depends on:** Steps 1-9
**Verify:** as above; do not claim done until the user has seen it.

## Risks to watch while building
- The `Grid.Row` move in `LibraryScreen.axaml`: change only root-level children and the pinned header; scroll-restore and pinned-header code in the code-behind may index rows.
- The tile overlay is priced for scroll smoothness: no allocation in `Render`, no new subscriptions, nothing when the setting is off.
- Membership and hero queries must stay one pass per load (no per-tile database work).
- New `.axaml` files need their code-behind in the same step; verify the weave, not just "0 Errors". A running Paperbunkr locks `bin`: compile-check with a scratch `-o` directory and never kill the app.

## Status (2026-11-06)

Steps 1-10 done in code (Step 2's resolver lives in `Paperbunkr.App/Services`). Targeted tests and lint green apart from the known ComicVine failure. On-screen check outstanding.
