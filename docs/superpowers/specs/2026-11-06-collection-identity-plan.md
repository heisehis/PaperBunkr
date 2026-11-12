# Collection identity — Implementation Plan
*Implements: docs/superpowers/specs/2026-11-06-collection-identity-design.md*

Order keeps every step compiling and testable. The tree already carries many uncommitted edits from other work: touch only the files named here, run only targeted
tests, never run two `dotnet build`/`test` at once, check `Get-Process testhost` first, and never regenerate a migration after the user has launched the app.
Before any XAML step `Read` the `avalonia` layout/styling/data-template/accessibility subskills and, at the end, `avalonia-pro-max/review-checklist` from
`~/.claude/skills/avalonia/...`; load `paperbunkr-design-language`. Every UI step ends with a build of `src/Paperbunkr.App` (prove `CompileAvaloniaXaml` ran) and the lint trio
`--filter "FullyQualifiedName~XamlConsistencyLintTests|FullyQualifiedName~ThemeHardcodedColorLintTests|FullyQualifiedName~SharedControlsStyleTests"`
(the ComicVine `FamilyButtonsCarryNoSizeOverrides` failure is known and not ours).

## Step 1: Entities, enums and the migration
**Files:** `src/Paperbunkr.Data/Entities/Collection.cs` (edit: `IconGlyph`, `CoverStyle`, `IsPinned`, `CoverMemberItemId`), `Entities/CollectionCoverStyle.cs` (new enum: Auto, Single, Mosaic, Fan),
`Entities/AppSettings.cs` (edit: `ShowCollectionProgress` default false, `CollectionRowDensity`, `CollectionDotShape`), `Entities/CollectionRowDensity.cs` and `CollectionDotShape.cs` (new enums),
`src/Paperbunkr.Data/Migrations/<timestamp>_AddCollectionIdentity.cs` + `.Designer.cs` + snapshot (generated), `src/Paperbunkr.Data.Tests/AddCollectionIdentityMigrationTests.cs` (new).
**What:** add the properties with the defaults from the spec; scaffold with `dotnet ef migrations add AddCollectionIdentity --project src/Paperbunkr.Data` (confirm `Up` is not empty - it is empty if a property edit did not land);
give the `AppSettings` columns a no-op `Down()` (SQLite rebuild drops orphaned columns); keep the `Collection` columns' `Down` normal.
**Depends on:** none
**Verify:** new migration test (columns exist, defaults, existing rows keep Auto/unpinned); the existing `CollectionsMigrationTests` still pass; note the known rollback-chain failures from other migrations.

## Step 2: Ordering helper, pin, reorder inside groups
**Files:** `src/Paperbunkr.Data/Collections/CollectionOrdering.cs` (new), `CollectionService.cs` (edit: `SetPinned`, `SetIdentity`, `Reorder` unchanged in contract),
`src/Paperbunkr.Data.Tests/CollectionOrderingTests.cs` (new), `CollectionServiceTests.cs` (edit)
**What:** `CollectionOrdering.Apply(IQueryable<Collection>)` = `OrderByDescending(IsPinned).ThenBy(SortOrder)` and an in-memory twin for lists; `CollectionService.SetPinned(context, id, bool)` (no renumbering);
`SetAppearance` extended (or a sibling `SetIdentity`) to persist `IconGlyph`, `CoverStyle`, `CoverMemberItemId`. Use the helper at the four read sites: `LibraryScreenViewModel.cs` (~L1205 load), `BooksScreenViewModel.cs` (~L864),
`HomeFeedResolver.GetHomeCollections`, `LibraryScreenViewModel.ShowInList.cs` (~L56).
**Depends on:** Step 1
**Verify:** Data tests: pinned first, stable order inside each group, unpin keeps its place in its group, `Reorder` with a mixed list keeps group order.

## Step 3: Cover hint with a chosen member, and progress aggregation
**Files:** `src/Paperbunkr.Data/Collections/CollectionResolver.cs` (edit `GetCoverHint`), `src/Paperbunkr.Data/Collections/CollectionProgressResolver.cs` (new), tests: `CollectionResolverTests.cs` (edit), `CollectionProgressResolverTests.cs` (new)
**What:** `GetCoverHint` honours `CoverMemberItemId` (stale/removed id -> first member). `CollectionProgressResolver.GetProgress(context, IReadOnlyList<int> collectionIds)` returns `Dictionary<int, (int Read, int Total)>`: one grouped query for series members (issues read / total), plus counts for standalone issues
(read) and books (finished), rule-matched members included through `CollectionResolver.GetMembers` only where the manual query cannot see them (keep it to one pass per collection with rules, none for plain ones). 0 units = no entry.
**Depends on:** Step 1
**Verify:** Data tests: series/issue/book mix, rule-matched members, empty collection, a stale cover member id; time the query on a few hundred synthetic series and note the figure in the doc.

## Step 4: Summaries and view-model surface
**Files:** `src/Paperbunkr.App/Models/CollectionSummary.cs` (edit: `IconGlyph`, `IsPinned`, `ProgressFraction`, `ProgressText`, `HasProgress`, `IsFirstUnpinned` for the divider), `LibraryScreenViewModel.cs` (edit ~L1205/1412/2024: load progress when the setting is on, build summaries, `TogglePinCollectionCommand`, group-aware `ReorderCollection`, new `MoveCollectionTo(source, target)` for the drag),
`BooksScreenViewModel.cs` (edit summary build), `src/Paperbunkr.App/Models/CollectionGlyphs.cs` (new: curated ~24 `(name, label)` list, `TryParse` to `Symbol`, fallback null), tests: `LibraryScreenViewModelTests` additions or a new `CollectionSidebarTests.cs`
**What:** summaries carry glyph/pin/progress; move up/down and drag-drop reorder only inside a group (a cross-boundary move is a no-op); pinning calls `CollectionService.SetPinned` then `ReloadAfterChange`; `Dispatcher.UIThread.Post` for any removal/reorder started from a routed event.
**Depends on:** Steps 2, 3
**Verify:** tests: summary fields, divider flag on the first unpinned row, boundary no-op, progress only when `ShowCollectionProgress` is on, glyph fallback.

## Step 5: Dialog state (glyph, pin, cover style, use-as-cover)
**Files:** `ViewModels/CollectionPropertiesScreenViewModel.cs` (edit), `ViewModels/CollectionMemberRowViewModel.cs` (edit: `IsCoverMember`, `UseAsCoverCommand`), `CollectionDialogPolishTests.cs` (edit) or `CollectionIdentityDialogTests.cs` (new)
**What:** buffered `IconGlyph` (+ `GlyphOptions` with selected flag, `ClearGlyphCommand`), `IsPinned`, `CoverStyle` (+ options), `CoverMemberItemId`; "Auto (Title)" label; Save persists all through `CollectionService`; Cancel discards; preview binds glyph/style/cover member; picking the current cover member clears it; rule-matched rows cannot be chosen.
**Depends on:** Steps 2, 3
**Verify:** view-model tests for each field's buffering, Cancel, the label, rule-matched rows, the preview following the chosen member.

## Step 6: Cover renderers (Fan, Mosaic style choice)
**Files:** `src/Paperbunkr.App/Controls/FanCoverStack.cs` (new, code-only), `Models/HomeCollectionCard.cs` (edit: style-aware mosaic/fan/single, `FanCovers`), `ViewModels/HomeScreenViewModel.cs` (edit ~L752 pass the style), `Views/Home/CollectionsSection.axaml` (edit if the card needs a `FanSources` property on `PosterTile`), `Views/PosterTile.cs`/`.axaml` as needed, tests: `HomeCollectionCardTests.cs` (new/edit)
**What:** Auto = today's rule; Single forces the single cover; Mosaic forces 2x2 with the Single fallback below 3 distinct covers; Fan draws up to 3 covers at -6/0/+6 degrees in one control (fallback Single below 2). A manual cover always wins. No animation.
**Depends on:** Steps 3, 5
**Verify:** `HomeCollectionCardTests` for each style and each fallback; headless render of the fan in default/daylight.

## Step 7: Dialog XAML
**Files:** `Views/CollectionPropertiesOverlay.axaml` (edit)
**What:** in Appearance add an Icon row (a `WrapPanel` of `iconButton` glyph tiles + "None", selected ring state style, names on each), a Pin toggle, a Cover style segmented choice (`Border.segGroup` / `Button.segItem`), the preview showing glyph and style, and a "Use as cover" `iconButton` on each manual member row; the Auto button text from the view model.
**Depends on:** Steps 5, 6
**Verify:** build + lint; extend `CollectionDialogRenderTests` to assert the new parts and write PNGs per theme.

## Step 8: Sidebar XAML (glyph, pin, divider, density, shape, ring, drag)
**Files:** `Views/MainWindow.axaml` (edit ~L297-419), `Views/CollectionSidebarRowDrag.cs` (new, modelled on `CollectionMemberRowDrag`), `Models/CollectionSummary.cs` (drag/drop flags), `Converters` if a symbol converter is needed (or bind `Symbol` through a small view-model property)
**What:** the row shows the glyph (else the dot, circle or squircle per `CollectionDotShape`), the Wand for smart ones, a pin icon when pinned, the `ProgressRing` beside the count when the setting is on and `HasProgress`, a hairline divider above the first unpinned row; hover actions gain Pin; a hover-only left grip (non-button `Border`) starts the drag with an accent drop line; Compact density through a `.compact` class on the Collections panel; Move up/down menu items stay.
**Depends on:** Step 4
**Verify:** build + lint; a headless render of the Collections block (default/daylight/matrix); `RealWindowKeyboardTests`-style walk is not needed, but Tab through hover actions on screen.

## Step 9: Preferences + Home card ring
**Files:** `ViewModels/PreferencesScreenViewModel.cs` (edit), the Appearance cosmetics section view that hosts the existing cosmetic toggles (locate beside the `ShowTaskbarBadge`/`ProgressRing` toggles; `Views/Preferences/GeneralSection.axaml` holds `ShowTaskbarBadge`), `Views/Home/CollectionsSection.axaml`/`PosterTile` (ring in the card corner), tests: `PreferencesScreenViewModelTests` additions (Speed=Slow class: run only the new tests by name)
**What:** three controls (Show collection progress toggle; Row density segmented; Dot shape segmented) wired to `AppSettings` with the existing apply/refresh path so the sidebar updates live; the Home card shows the ring only when the setting is on.
**Depends on:** Steps 4, 8
**Verify:** targeted preference tests by name; on screen.

## Step 10: Review, docs and on-screen pass
**Files:** `docs/paperbunkr-todo.md`, the spec's "Implementation notes", the plan's status, a memory update
**What:** run `avalonia-pro-max/review-checklist`; run `CollectionSidebarTests`, the dialog tests, Data tests for steps 1-3 and the lint trio; do **not** run the full suite. Then the user's screen check at 1366x768 (pin/unpin, drag within a group, glyph picker, cover styles on Home, the ring with the setting on, Compact density, Squircle) and record verified vs unverified in the roadmap entry by hand.
**Depends on:** Steps 1-9
**Verify:** as above; do not claim done until the user has seen it.

## Risks to watch while building
- Every collection list must use `CollectionOrdering` (sidebar, Books, Home, ShowInList) or the lists disagree.
- The progress query must stay one grouped query; measure before suggesting it on by default.
- A button swallows a left press: the sidebar grip must be a `Border`, never the row `Button`.
- The migration touches two tables; confirm `Up` is not empty, `AppSettings` `Down()` is a no-op, and watch for the known unrelated rollback-chain failures.
- New `.axaml` files need their code-behind in the same step; verify the weave, not just "0 Errors". A running Paperbunkr locks `bin`: compile-check with a scratch `-o` directory and never kill the app.

## Status (2026-11-06)

Steps 1-10 done in code, with targeted tests and lint green apart from the known ComicVine `FamilyButtonsCarryNoSizeOverrides` failure. On-screen check outstanding.
