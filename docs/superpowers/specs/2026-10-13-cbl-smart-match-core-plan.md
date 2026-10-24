# CBL Manager Smart Match, part 1 — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md*

New code lives in `src/Paperbunkr.Data/ReadingLists/Matching/`; new data tests in `src/Paperbunkr.Data.Tests/ReadingLists/Matching/`.
App tests are run by class filter only, never the wide suite.

## Step 1: Keys
**Files:** `Matching/IssueNumberKey.cs` (new), `Matching/SeriesKeys.cs` (new), `Acquisition/IssueNumbers.cs` (edit: `Equal` forwards),
`Metadata/StoryEventIdCompletion.cs` (edit: `SameIssue` uses the key).
**What:** design §5.1 and §5.2 (decision Q16).
**Verify:** `IssueNumberKeyTests`, `SeriesKeysTests`; existing `IssueNumbers` users' tests.

## Step 2: Source record
**Files:** `Entities/ReadingListItemSource.cs` (new, with `MatchMethod`, `MatchState`), `Entities/ReadingListItem.cs` (navigation),
`PaperbunkrDbContext.cs` (DbSet, config, indexes), new migration `AddReadingListItemSource` with the backfill of §4.
**Depends on:** Step 1 (keys are stored).
**Verify:** `AddReadingListItemSourceMigrationTests`, following the existing migration-test pattern (up only; no up-down-up).

## Step 3: Engine
**Files:** `Matching/ComicReference.cs`, `Matching/LibraryIndex.cs`, `Matching/MatchEngine.cs`, `Matching/MatchResult.cs` (all new).
**What:** design §5.3 to §5.5. The engine is pure; the snapshot is the only part that queries.
**Depends on:** Step 1.
**Verify:** `MatchEngineTests` (no database), `ReadingListHealingTests`.

## Step 4: Sources carry ids and report what they drop
**Files:** `Sources/ArcModels.cs` (id fields, `ArcEntryList`), `Sources/IReadingListSource.cs` (`GetArcEntriesAsync` default),
`Sources/ComicVineSource.cs`, `Sources/MetronSource.cs`, `Sources/MetronReadingListSource.cs`, `Sources/ComicBookReadingOrdersSource.cs`.
**Verify:** parser tests against literal fixtures, as the adapters already do; no live network in tests.

## Step 5: Resolver and import report
**Files:** `Matching/ReadingListResolver.cs`, `Matching/ImportReport.cs` (new); edits to `CblReadingListIO.cs`, `CsvReadingListIO.cs`,
`CblImport.cs`, `ReadingListFolderIO.cs`, `ArcReadingListBuilder.cs`, `ReadingListCanonicalDiff.cs`, `ReadingListMatcher.cs` (wrappers).
**Depends on:** Steps 2 to 4.
**Verify:** `ReadingListResolverTests`; existing `ReadingListTests`, `ArcReadingListBuilderTests`, `CblImportTests`, canonical-diff and folder tests.

## Step 6: Provider ids in .cbl
**Files:** `CblReadingListIO.cs`.
**What:** confirm the element shape against real community files first; if it cannot be confirmed, leave this step out and say so.
**Verify:** fixture test with and without ids.

## Step 7: Healer
**Files:** `Matching/ReadingListHealer.cs` (new), `ReadingListItemLinker.cs` (records the method), `App/Services/LibraryFolderScanner.cs`
(hook after a run that added issues), `App/ViewModels/ReadingListPageViewModel.Drawer.cs` (Find & link records `User`).
**Depends on:** Step 5.
**Verify:** `ReadingListHealerTests`; `LibraryFolderScannerTests` class.

## Step 8: Repair
**Files:** `Matching/ReadingListRepair.cs` (new), `App/ViewModels/LibraryHealthSections.cs` and `PreferencesScreenViewModel.LibraryHealthTabs.cs`
plus the matching view (the card).
**Depends on:** Step 7.
**Verify:** `ReadingListRepairTests`; then a run against a **copy** of the user's database for acceptance 1.

## Step 9: Visible changes
**Files:** `ReadingListPageViewModel.Manage.cs` (counts in messages, Recheck command), `ReadingListItemRowViewModel.cs` and
`Views/ReadingListPageView.axaml` ("needs a choice", Suggested group, ⋯ item), `NewReadingListViewModel.cs` (counts).
**What:** design §7. Read `avalonia-data-templates` and `avalonia-pro-max/review-checklist` before the XAML.
**Verify:** targeted `ReadingListsScreenViewTests` / page view-model test classes. On-screen check by the user.
