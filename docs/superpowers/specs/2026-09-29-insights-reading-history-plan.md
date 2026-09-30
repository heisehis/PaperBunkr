# Insights → History tab — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-29-insights-reading-history-design.md*

**Status: all 7 steps done 2026-09-29, uncommitted.** Deviations are recorded in the design's "Implementation notes". Only
the user's on-screen check is outstanding.

**Before starting:** the tree is shared with concurrent sessions (see memory
`project_paperbunkr_concurrent_sessions`). Run `git status` first. `PaperbunkrDbContext.cs` already carries
~130 uncommitted lines from other sessions (ReadingListFolder, DismissedRecommendation, StoryEventAlias,
etc.). Step 1's migration must not absorb their model changes. Don't launch the app or run `dotnet ef`
against the real dev DB casually either (memory `feedback_worktree_shares_user_db`).

Steps 1–3 are Data/service work with no UI. Steps 4–5 depend on them. Step 6 is the UI. Step 7 is verification.

## Step 1: Schema — snapshot columns + hidden flag + backfill
**Files:**
- `src/Paperbunkr.Data/Entities/ReadingEvent.cs` (edit): add `string? SeriesTitle`, `string? ItemLabel`, `bool HiddenFromHistory`. Update the class doc comment's "append-only" paragraph (PagesRead fill + HiddenFromHistory are the only in-place writes; the three consumers that must ignore the flag).
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit): inside the existing `modelBuilder.Entity<ReadingEvent>` block, `builder.Property(e => e.HiddenFromHistory).HasDefaultValue(false)`. Before relying on `HasDefaultValue` for a bool, check memory `project_paperbunkr_metron_provider_and_pull_list` (EF `HasDefaultValue` gotcha). If it bites, use `defaultValue: false` in the migration only and leave the model plain.
- `src/Paperbunkr.Data/Migrations/ReadingHistoryBackfill.cs` (new): static `Statements` + `Run(MigrationBuilder)`, same shape as `ReadingEventBackfill`. Four `UPDATE`s:
  1. Comic rows ← `Series.Name`, and `'#' || Issues.Number` / `'Vol. ' || Issues.Volume` / `Issues.Title` via `CASE`.
  2. Novel rows in a book series ← `BookSeries.Name` + `Books.Title`.
  3. Standalone novel rows ← `Books.Title`, with `ItemLabel` NULL.
  4. Only where the item still exists. Enum columns compare as strings (`'Comic'`/`'Novel'`).
- `src/Paperbunkr.Data/Migrations/<timestamp>_AddReadingHistoryColumns.cs` (+ `.Designer.cs`, snapshot) (new): `AddColumn` ×3, then `ReadingHistoryBackfill.Run`. `Down()` is a documented no-op, citing the orphan-column rule.
  - **Generation:** first run `dotnet ef migrations has-pending-model-changes` (project `src/Paperbunkr.Data`) *before* editing the entity. If it reports pending changes, those belong to another session. **Stop and ask the user** rather than folding them into this migration.
  - If it's clean, edit the entity, then `dotnet ef migrations add AddReadingHistoryColumns`.
  - Then inspect the generated `Up()`: it must contain only the three `ReadingEvents` columns.
**Depends on:** none
**Verify:**
- New `src/Paperbunkr.Data.Tests/ReadingHistoryBackfillTests.cs`: migrated DB, then null the two columns, re-run `Statements`, and assert:
  - a comic with a number gets `#12`; a comic with a volume only gets `Vol. 2`;
  - a book in a series gets series name + book title; a standalone book gets its title with a null label;
  - a row whose issue was deleted stays null;
  - running it twice gives the same result.
- New `AddReadingHistoryColumnsMigrationTests.cs`: full `Migrate()` → the columns exist and a round-trip insert reads `HiddenFromHistory == false`. No down-migrate assertion (documented antipattern).
- Run `dotnet test src/Paperbunkr.Data.Tests --filter "ReadingHistoryBackfill|AddReadingHistoryColumns|ReadingEventBackfill|AddReadingEventLog"`.

## Step 2: Recorder — fill snapshots, add `HideFromHistory`
**Files:**
- `src/Paperbunkr.App/Services/History/ReadingHistoryGroupKey.cs` (new): `enum ReadingHistoryGroupKind { ComicSeries, BookSeries, Book }`, `sealed record ReadingHistoryGroupKey(ReadingHistoryGroupKind Kind, int Id)`, plus static `For(ReadingItemType type, int itemId, int? seriesId)`.
- `src/Paperbunkr.App/Services/History/ReadingHistoryLabels.cs` (new): the one C# formatter for `SeriesTitle` / `ItemLabel`. Comic: `#{issue.EffectiveNumber()}` → `Vol. {Volume}` → `Title`. Book: title rules per spec §1. The recorder and the resolver both use it, so live and snapshot labels always agree.
- `src/Paperbunkr.App/Services/IReadingEventRecorder.cs` (edit): add `void HideFromHistory(ReadingHistoryGroupKey? group)`. Give it a **default no-op body**, like `ReadingFinished`, so existing test doubles don't break.
- `src/Paperbunkr.App/Services/ReadingEventRecorder.cs` (edit):
  - `Insert` → `FillSnapshot(context, row)` before `SaveChanges`. It looks up the issue with `Series` (or the book with `BookSeries`) via `IgnoreQueryFilters()`, inside a `try` that swallows lookup failures.
  - `HideFromHistory` → `ExecuteUpdate` setting `HiddenFromHistory = true`, filtered by `ItemType` + (`SeriesId` or `ItemId`) per key kind, or on all rows for `null`. Then raise `ReadingEventRecorded`.
  - Any test double that implements the interface explicitly: grep `: IReadingEventRecorder` in the test projects and confirm each still compiles.
**Depends on:** Step 1
**Verify:**
- Extend `src/Paperbunkr.App.Tests/ReadingEventRecorderTests.cs`:
  - Snapshot names are filled for a comic, a book in a series and a standalone book.
  - A missing item still inserts with null names.
  - `HideFromHistory(ComicSeries 5)` hides comic series-5 rows but **not** a Novel row with `SeriesId 5`.
  - `HideFromHistory(null)` hides everything.
  - Both raise the event.
- Run `dotnet test src/Paperbunkr.App.Tests --filter ReadingEventRecorderTests`.

## Step 3: Resolver
**Files:**
- `src/Paperbunkr.App/Services/History/ReadingHistoryRow.cs` (new):
  - `enum ReadingHistoryContentKind { Comic, Manga, Book }` and `enum ReadingHistoryAction { None, Resume, ReadNext, Open }`.
  - `sealed record ReadingHistoryRow(GroupKey, Title, ItemLabel, LastReadUtc, CoverKey, ContentKind, IsInLibrary, IsFinished, IsCaughtUp, ProgressText, Action, TargetId, BookFormat?, DetailSeriesId?, DetailBookId?)`.
- `src/Paperbunkr.App/Services/History/ReadingHistoryResolver.cs` (new): `public static IReadOnlyList<ReadingHistoryRow> Resolve(PaperbunkrDbContext ctx)`, implementing spec §2 steps 1–6 and the ▶ table.
  - **Loading:** one projection query over non-hidden events, then batched `IgnoreQueryFilters()` loads of `Series` (`Include(Issues).ThenInclude(MetadataProposals)`, the same include `ReadingOrderResolver` uses so `EffectiveNumber()`/`OrderByNumber()` behave identically), `BookSeries` and `Books` by the referenced ids.
  - **Next issue:** `series.Issues.OrderByNumber()` → the next element after the row's issue, skipping `FileIsMissing`.
  - **Manga vs comic:** reuse MainViewModel's `IsMangaFamily` logic. It is `private static` there (`MainViewModel.cs:1880`), so move it to a small `internal static` helper (e.g. `Models/ContentTypeFamily.cs`) and have MainViewModel call the helper. Mechanical, with no behavior change.
  - **Covers:** `SeriesCardSample.FromSeries(series).CoverKey`, `CoverFingerprint.Stem(book.Id, book.FilePath, null)`.
  - **Labels:** `ReadingHistoryLabels`.
**Depends on:** Steps 1–2
**Verify:**
- New `src/Paperbunkr.App.Tests/ReadingHistoryResolverTests.cs` (temp SQLite, fixture shape copied from `ReadingEventRecorderTests`), covering every case listed in spec §6:
  - grouping, and the comic-5 vs book-series-5 split;
  - hide, then read again (reappears, only post-hide events counted);
  - fallback to a surviving issue; greyed when the series is deleted; dropped when there's no snapshot;
  - remote source present vs removed;
  - each ▶ table row; manga `ContentKind`;
  - the perf smoke (500 groups / 20k events, assert < 2 s).
- Run `dotnet test --filter ReadingHistoryResolverTests`.

## Step 4: `HistoryTabViewModel`
**Files:**
- `src/Paperbunkr.App/ViewModels/HistoryTabViewModel.cs` (new), per spec §3:
  - **Items:** the `HistoryDayHeader` (`Label`) and `HistoryRowItem` (wraps `ReadingHistoryRow` + display strings: subtitle `ItemLabel · ProgressText · time`, tag text/kind, play tooltip, `ShowPlay`/`ShowOpen`) item classes, which can go in `Models/HistoryItems.cs`.
  - **Filtering:** `SearchText`/`TypeFilter` → `Rebuild()` over the cached rows.
  - **Loading:** `Refresh()` does a background resolve with a load-generation counter, then posts to the UI thread.
  - **Commands:** `OpenRow`, `PlayRow`, `RemoveRow`, `ClearAll`.
  - **Deferred removal:** the post-remove reload is deferred with `Dispatcher.UIThread.Post` (CLAUDE.md routed-event rule).
  - **Errors:** `IActivityService.RaiseAlert` on failure.
  - **Context menu:** implements `IContextMenuProvider` (the `Controls/ContextMenuHost` mechanism, as in `BooksScreenViewModel`) for the row menu.
- `src/Paperbunkr.App/ViewModels/HistoryDayLabel.cs` (new, or nested static): `For(DateTime localDate, DateTime localToday)`.
- `src/Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs` (edit):
  - Add the `goReaderForBook`/`goBookDetailForBook` ctor params, the `History` property, `IsHistoryTabSelected`, and `SelectHistoryTabCommand`.
  - Make the three tab flags mutually exclusive; `IsTodayTabSelected` = none of the three.
  - Update the class doc comment's tab list.
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit, line ~284): pass `GoBookReaderForBook` / `GoBookDetailForBook`. Both are private method groups already in scope there.
- Test call sites constructing `InsightsScreenViewModel` (grep `new InsightsScreenViewModel(` in `src/Paperbunkr.App.Tests`): add the two new delegates.
**Depends on:** Step 3
**Verify:**
- New `src/Paperbunkr.App.Tests/HistoryTabViewModelTests.cs`:
  - day-label cases;
  - header interleaving order;
  - search + type filter (including Manga);
  - each command routes to the right delegate per row kind, and greyed rows are no-ops;
  - `ClearAll` with a fake dialog returning false does nothing, and with true calls `HideFromHistory(null)`;
  - `ReadingEventRecorded` reloads only while `IsActive`.
  - Use `TestDispatcher.Drain()` for the posted work (memory `project_paperbunkr_full_suite_headless_flake`).
- Extend `InsightsScreenViewModelTests` for three-way tab exclusivity.
- Run `dotnet test --filter "HistoryTabViewModel|InsightsScreenViewModel"`.

## Step 5: Wire recorder consumers (sanity)
**Files:** none expected. Grep the consumers of `context.ReadingEvents` (`StatsResolver`, `InsightsResolver`, `GoalResolver`, `RecapResolver`, `ReadingListForecast`, `InsightsRecommendationResolver`, `DetailTabsViewModel`) and confirm **none** filter on `HiddenFromHistory`. Per the spec, hiding must not change them.
**Depends on:** Step 1
**Verify:**
- One regression test in `ReadingHistoryResolverTests` (or `StatsScreenViewModelTests` if it fits better): hiding a series leaves `StatsResolver`'s lifetime totals unchanged.

## Step 6: View
Load `avalonia` → `avalonia-xaml`, `avalonia-data-templates`, `avalonia-pro-max/components` and `avalonia-pro-max/layout-patterns` (read the `SKILL.md` files directly off disk, per CLAUDE.md) before writing XAML.

**Files:**
- `src/Paperbunkr.App/Views/Insights/InsightsHistoryView.axaml` + `InsightsHistoryView.axaml.cs` (new, **same step**, code-behind `partial class InsightsHistoryView : UserControl { ctor → InitializeComponent(); }`), `x:DataType="vm:HistoryTabViewModel"`, per spec §4:
  - **Toolbar:** search `TextBox`; four `pbChip` buttons bound to `TypeFilter`; a Clear-all icon button with FluentIcons `Delete`.
  - **List:** a `ListBox` over `Items` with typed `DataTemplate`s for the header and the row. Row: `CoverThumb` 32×46 or the hatched placeholder `Border`; title + tag chip; subtitle; ▶ (`Play`) / `Open` / nothing.
  - **Greyed rows:** `Classes.dead` → opacity 0.45.
  - **Context menu:** `ContextMenuHost` attached.
  - **Empty states:** both.
  - **Colors:** only `Pb*Brush` `DynamicResource`s; tag tints from the existing success/warning brushes (grep `App.axaml` for their names).
  - **Motion:** no row entrance animation.
- `src/Paperbunkr.App/Views/InsightsScreen.axaml` (edit):
  - A `History` tab button between Trends and Recap.
  - A fourth `Grid.Row="2"` host `<insights:InsightsHistoryView DataContext="{Binding History}" IsVisible="{Binding IsHistoryTabSelected}" Margin="28,0,28,20"/>`. It isn't wrapped in a `ScrollViewer`: the `ListBox` scrolls itself, and nesting would kill virtualization.
- Check `InsightsScreen.axaml.cs` (already modified by another session: 1 line) for any per-tab code-behind that needs a History case. It's expected to need none.
**Depends on:** Step 4
**Verify:**
- `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj -t:Rebuild` (new View, per the CLAUDE.md build gotcha).
- A headless render test in `src/Paperbunkr.App.Tests` (the existing headless Avalonia setup) that instantiates `InsightsHistoryView` with a seeded VM and asserts the header + row containers realize without exceptions.
- Then run `avalonia-pro-max/review-checklist`.

## Step 7: Full regression + docs
**Files:**
- `docs/paperbunkr-todo.md` (edit): Beta backlog entry for History with status, and the four backlog items from spec §7.
- `wiki/` Insights page (edit): a History section, following memory `project_paperbunkr_user_wiki`. Don't publish.
- `PRIVACY.md` (edit, one line): reading history is stored locally in the same reading log, and Remove / Clear all hide entries from History without deleting stats data.
**Depends on:** Steps 1–6
**Verify:**
- `dotnet test` on both test projects. Compare failures against the known pre-existing set before attributing any to this work.
- **On-screen check (manual, user):** open Insights → History on the real library. Check:
  - rows appear for recently read series;
  - ▶ resumes, and read-next works after a finished issue;
  - a PDF row shows Open;
  - Remove hides the row and Trends numbers are unchanged;
  - Clear all confirms;
  - search and chips filter;
  - skins restyle live.
  - Nobody has viewed this on screen until the user does, so say so explicitly when reporting.
