# Insights Dashboard — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-05-insights-dashboard-design.md*

Precedents to mirror (read before starting the matching step):
- **Entity + DbContext config + new-table migration:** `ActivityRun` (`Entities/ActivityRun.cs`,
  `PaperbunkrDbContext.cs:1093`, `Migrations/20260903215515_AddActivityRuns.cs`).
- **Raw-SQL data backfill in a migration:** `Migrations/20260903133114_UnifyLibrarySortGroupFields.cs`.
- **Manually-composed App service passed to VMs:** `ActivityService` (`MainViewModel.cs:95`).
- **Read-only static resolver with in-memory tests:** `HomeFeedResolver` +
  `Paperbunkr.Data.Tests/HomeFeedResolverTests.cs`.
- **A lateral rail screen end-to-end:** the `events` screen — `EventsScreenViewModel`,
  `Views/EventsScreen.axaml`, and every `MainViewModel` site listed in Step 8.

---

## Step 1: `ReadingEvent` entity + `Book.CharacterCount`
**Files:**
- `src/Paperbunkr.Data/Entities/ReadingEvent.cs` (new) — entity + `ReadingItemType` (Comic=0,
  Novel=1) and `ReadingEventKind` (Opened=0, Finished=1) enums, per design §4.
- `src/Paperbunkr.Data/Entities/Book.cs` (edit) — add `public int? CharacterCount { get; set; }`.
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit) — `DbSet<ReadingEvent> ReadingEvents`;
  `modelBuilder.Entity<ReadingEvent>` block after the `ActivityRun` block: `HasKey(Id)`, both enums
  `HasConversion<string>().HasMaxLength(16)` (new table → no `HasDefaultValue`/`HasSentinel`, same
  as `ActivityRun`), `HasIndex(TimestampUtc)`, `HasIndex(new { ItemType, ItemId })`. No FK to
  `Issue`/`Book` (design §4). Add `builder.Property(b => b.CharacterCount)` — nullable, no default,
  in the existing `Entity<Book>` block.

**Depends on:** none.
**Verify:** solution builds. `Paperbunkr.Data.Tests` still green (no behaviour change yet).

## Step 2: Migration `AddReadingEventLog` + backfill
**Files:**
- `src/Paperbunkr.Data/Migrations/*_AddReadingEventLog.cs` (new, via `dotnet ef migrations add`) —
  creates `ReadingEvents` + `Book.CharacterCount` column, then in `Up()` after the `CreateTable`,
  `migrationBuilder.Sql(...)` inserts backfill rows (design §4.1):
  - comics `Opened`: `INSERT INTO "ReadingEvents" (...) SELECT 'Comic', Id, 'Opened', OpenedTime,
    NULL, SeriesId, Publisher, NULL FROM "Issues" WHERE OpenedTime IS NOT NULL`.
  - comics `Finished`: same but `Kind='Finished'` and
    `WHERE OpenedTime IS NOT NULL AND PageCount > 0 AND CAST(LastPageRead AS REAL)/PageCount >= 0.95`
    (inline of `ReadThresholdPercent`; verify the 95 constant against
    `IssueMetadataExtensions.ReadThresholdPercent` at write time).
  - novels `Opened` / `Finished`: from `Books` on `LastOpenedTime`, `Finished` flag; `Publisher`
    NULL, `SeriesId` = `BookSeriesId`.
  - `PrimaryGenre` left NULL in backfill (genre lives in the `IssueTag` child table — not worth a
    correlated subquery for historical rows).
- Run against a scratch DB with `PAPERBUNKR_DB_PATH` set to a temp file — **do not** migrate the
  real `%APPDATA%\Paperbunkr\paperbunkr.db` (worktree-shares-user-DB gotcha). `--connection` to a
  temp file, or set the env var.

**Depends on:** Step 1.
**Verify:** `dotnet ef migrations script` diff looks right; migration test in Step 9. Snapshot file
regenerated and committed with the migration (the `ef-migrations-remove` snapshot-desync gotcha —
if the migration is ever removed, remove via `dotnet ef migrations remove`, don't hand-delete).

## Step 3: `ReadingEventRecorder` service
**Files:**
- `src/Paperbunkr.App/Services/IReadingEventRecorder.cs` (new) — interface:
  `void RecordOpened(ReadingItemType type, int itemId, ...snapshot fields...)`,
  `void RecordFinished(... int? pagesRead)`, `void UpdateSessionPages(ReadingItemType type,
  int itemId, int pagesRead)` (updates the still-open `Opened` row in place), and an
  `event Action ReadingEventRecorded` the screen VM subscribes to for cache invalidation.
- `src/Paperbunkr.App/Services/ReadingEventRecorder.cs` (new) — writes via
  `PaperbunkrDb.CreateContext()` (same pattern as the reader VMs' own inline saves). `RecordFinished`
  always inserts a new row (re-reads re-emit, design §5). `UpdateSessionPages` finds the most recent
  `Opened` row for `(type,itemId)` with `PagesRead IS NULL` and sets it; no-op if none.
  Raises `ReadingEventRecorded` after each write.

**Depends on:** Step 1.
**Verify:** unit tests in Step 9. Build.

## Step 4: Wire the recorder into the three readers
**Files:**
- `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit):
  - ctor takes `IReadingEventRecorder recorder` (nullable-defaulted like `keyBindingService`
    neighbours so `<vm:MainViewModel/>` design-time still constructs).
  - In `Load` (`:829`), right after `issue.OpenCount++`, capture session start:
    `_sessionStartPage = issue.LastPageRead ?? 0; _sessionMaxPage = _sessionStartPage;` and
    `recorder.RecordOpened(Comic, issue.Id, issue.SeriesId, issue.EffectivePublisher(), issue.PrimaryGenreTag())`.
  - Add a `bool _finishedEmittedThisSession`. In both `LastPageRead` write sites — `GoToPage`
    (`:1910`) and `FlushPendingPositionSave` (`:1981`) — after the write, compute
    `ReadPercentage()`; if it crossed `>= 95` and `!_finishedEmittedThisSession`, call
    `recorder.RecordFinished(Comic, id, pagesRead: _sessionMaxPage - _sessionStartPage)` and set the
    flag. Track `_sessionMaxPage = Max(_sessionMaxPage, _currentPageIndex)`.
  - In `FlushPendingPositionSave` (runs on leave + next `Load`), also call
    `recorder.UpdateSessionPages(Comic, id, _sessionMaxPage - _sessionStartPage)`.
- `src/Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` (edit): ctor takes the recorder.
  `RecordOpened` where `LastOpenedTime` is set (`:417`/`:1052`); persist `Book.CharacterCount` from
  the parsed source there if null. On the existing `Book.Finished` false→true transition, call
  `RecordFinished(Novel, book.Id, pagesRead: (charsRead)/1800)`. `UpdateSessionPages` on teardown
  with the char-delta/1800 estimate (design §6).
- `src/Paperbunkr.App/ViewModels/PdfPageReaderScreenViewModel.cs` (edit): recorder in ctor;
  `RecordOpened` on load, `RecordFinished` on reaching the last page with a real page delta,
  `UpdateSessionPages` on leave.
- `src/Paperbunkr.Data/Metadata/IssueMetadataExtensions.cs` (edit): add small helpers
  `EffectivePublisher()` (already effectively `issue.Publisher ?? issue.Series?.Publisher` logic
  — check `SmartListCatalog.TextSelectors` for the canonical form) and `PrimaryGenreTag()` (first
  `IssueTag` with `Field == Genre`, ordered) if not already present.

**Depends on:** Steps 1, 3.
**Verify:** `Paperbunkr.App.Tests` reader-VM suites green (targeted `--filter`, per the full-suite
headless flake). New recorder-crossing tests in Step 9.

## Step 5: `InsightsResolver`
**Files:**
- `src/Paperbunkr.Data/Metadata/InsightsResolver.cs` (new) — `InsightsRange` enum (Days30, Days90,
  Months12, AllTime); `InsightsSnapshot` record with one property per tile (design §8.3);
  `static InsightsSnapshot Build(PaperbunkrDbContext ctx, InsightsRange range, DateTime nowUtc)`.
  One `AsNoTracking()` pass each over `Issues` (`.Include(Series).Include(Tags)`), `Books`
  (`.Include(BookSeries)`), and a range-scoped `ReadingEvents` query; all further computation in
  memory. Sub-computations as private static methods, each independently testable:
  `ComputeStalled`, `ComputeAlmostDone`, `ComputeGaps` (integer issue numbers via
  `NumberType()==Numeric` + `NumberSortKey()`), `ComputeUntouched`, `ComputeStreaks` (reading-day +
  finish, distinct local dates), `ComputePace` (weekly buckets ≤ Days90, monthly beyond),
  `ComputeCompletion`, `ComputeComposition`, `ComputeRatings`, `ComputeLifetimeTotals`.
- Constants as `internal const`: `StalledDays = 21`, `AlmostDoneMax = 3`, `UntouchedGraceDays = 7`,
  `EpubCharsPerPage = 1800`. Reuse `IssueMetadataExtensions.ReadThresholdPercent`.

**Depends on:** Step 1 (entity). Not Step 2/4 — testable with hand-seeded events.
**Verify:** the bulk of Step 9's tests.

## Step 6: Charting infra — ScottPlot + donut
**Files:**
- `src/Paperbunkr.App/Paperbunkr.App.csproj` (edit) — add `<PackageReference Include="ScottPlot.Avalonia"
  Version="5.1.59" />` (or the newest 5.x verified to resolve against Avalonia 12.1.1 — check its
  `Avalonia.Skia` dep pins to `>= 12.0.0`, and that `SkiaSharp` doesn't conflict with the version
  `Svg.Skia 5.1.0` brings; add an explicit `SkiaSharp` pin if MSB3277 shows up).
- `src/Paperbunkr.App/Services/InsightsChartTheme.cs` (new) — resolves the active skin's brushes via
  `Application.Current!.FindResource(key)` for the same keys used elsewhere (grep `DynamicResource Pb`
  in `Views/` for the real key names — e.g. `PbTextBrush`, `PbAccentBrush`, `PbSurfaceBrush`),
  returns a small struct of `ScottPlot.Color`s + applies them to a `Plot` (figure/axes/ticks/grid +
  palette). Called by each chart control's render path.
- `src/Paperbunkr.App/Views/Insights/CompletionDonut.cs` (new) — `Control` subclass, `OnRender`
  draws 3 arcs via `StreamGeometry`; colours from `DynamicResource` (re-renders free on skin change).
  `StyledProperty<double>` for Read/InProgress/Unread fractions.

**Depends on:** none (parallel with 1–5).
**Verify:** build; `dotnet run` shows an empty screen renders without a Skia/CSP error. No unit test
for ScottPlot draw; `InsightsChartTheme`'s brush resolution gets a test (falls back to sane defaults
when `Application.Current` is null, for headless).

## Step 7: `InsightsScreenViewModel`
**Files:**
- `src/Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs` (new) — ctor takes the callbacks it
  needs for attention-card click targets (`GoReaderForIssue`, `GoDetailForSeries`,
  `GoLibraryWithSearch` — all already exist on `MainViewModel` and are handed to other screen VMs)
  and `IReadingEventRecorder` (to subscribe `ReadingEventRecorded` → invalidate cache).
  `[ObservableProperty] InsightsRange _range` (default `Days90`); a `Dictionary<InsightsRange,
  InsightsSnapshot>` session cache; `Refresh()` builds via `InsightsResolver.Build(ctx, Range,
  DateTime.UtcNow)` if not cached. `OnRangeChanged` → `Refresh()`. Exposes one child VM / record per
  tile for binding. Called on screen activation (mirror how `EventsScreenViewModel` reloads on nav).
- `src/Paperbunkr.App/Models/Insights*.cs` (new, as needed) — small row records for the attention
  lists (title, subtitle, click-target id/kind).

**Depends on:** Steps 3, 5.
**Verify:** VM tests in Step 9 (cache reuse, invalidation, empty library).

## Step 8: `InsightsScreen` view + nav-rail wiring
**Files:**
- `src/Paperbunkr.App/Views/InsightsScreen.axaml` (+ `.axaml.cs` with the
  `InitializeComponent()` code-behind **in the same commit** — AVLN2000 gotcha) — outer
  `ScrollViewer`; range `SelectingItemsControl`/toggle row; "Needs attention" `UniformGrid`/`WrapPanel`
  of 4 attention tiles; "At a glance" stat row; 2×2 panel grid (pace `ScottPlot.Avalonia.AvaPlot`,
  `CompletionDonut`, composition h-bars, ratings `AvaPlot`). All colours via `DynamicResource` — run
  `avalonia-pro-max/review-checklist` before calling it done (hardcoded-hex gotcha).
- `src/Paperbunkr.App/Views/Insights/*.axaml` (new) — one small `UserControl` per tile if the screen
  file gets unwieldy; otherwise inline.
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit) — mirror `events` at every site:
  `Insights` property + `new InsightsScreenViewModel(...)` in ctor (near `:131`); `RailOrder`
  insert `["insights"] = 1` and renumber below (design §8.1 puts it under Home — decide: either
  Home=0/Insights=1/Library=2… renumber, **or** append `["insights"] = 7` and accept it sits last
  in Ctrl+Tab order but place the rail button visually under Home; recommend the renumber);
  `ActiveScreenContent` switch (`:529`); `IsInsights` bool + `OnCurrentScreenChanged` notify
  (`:571`/`:616`); `GoInsights()` method + `[RelayCommand]` + `ResetHistoryRoot("insights")`;
  `CycleScreen`/keyboard-shortcut map (`:964`); screen-title map (`:1897` → `"Insights"`);
  deep-link `case "insights"` (`:1774`, `:2008`); `WelcomeTourOverlayViewModel` ctor arg list
  (`:143`) — add `GoInsightsCommand`.
- `src/Paperbunkr.App/Views/MainWindow.axaml` (edit) — rail `Button` (label "Insights", FluentIcons
  `DataHistogram`), bound `Command="{Binding GoInsightsCommand}"`, `IsVisible`/active-state like the
  others; host the screen in the lateral content region.
- `src/Paperbunkr.App/ViewModels/WelcomeTourOverlayViewModel.cs` (edit) — accept the extra command
  param.

**Depends on:** Step 7 (VM), Step 6 (chart controls).
**Verify:** `dotnet run` — screen opens from the rail, renders with a real library, range switch
works, attention cards navigate. Manual on-screen pass (no computer-use — user verifies or FlaUI).
Force `CoreCompile` (`rm obj/.../Paperbunkr.App.dll`) if the new `.axaml` trips AVLN2000.

## Step 9: Tests
**Files:**
- `src/Paperbunkr.Data.Tests/InsightsResolverTests.cs` (new) — in-memory `PaperbunkrDbContext`
  (mirror `HomeFeedResolverTests` fixture). Cover: streak math across day boundaries / gaps /
  timezone (local-date bucketing); pace weekly-vs-monthly cutover at the Days90/Months12 boundary;
  stalled = exactly 21 days edge; almost-done 0/1/3/4 remaining; gap detection with Annual/`1A`/
  fractional numbers mixed in; completion counts across both schemas; range include/exclude;
  a `ReadingEvent` whose `ItemId` no longer exists still counts toward lifetime totals; empty DB.
- `src/Paperbunkr.Data.Tests/AddReadingEventLogMigrationTests.cs` (new) — seed a pre-log schema
  state, apply `Up()`, assert synthesised rows (mirror an existing migration test).
- `src/Paperbunkr.App.Tests/ReadingEventRecorderTests.cs` (new) — `Finished` fires once on the
  94→96% crossing, again on a fresh re-read session; `UpdateSessionPages` in-place update;
  EPUB char/1800 estimate; `RecordOpened` with a null `Application.Current` doesn't throw.
- `src/Paperbunkr.App.Tests/InsightsScreenViewModelTests.cs` (new) — snapshot cached per range,
  reused on re-activation, dropped on `ReadingEventRecorded`; empty library / empty log render
  without throwing; range default is `Days90`.
- Reader-VM suites: extend existing `ReaderScreenViewModelTests` / `BookReaderScreenViewModelTests`
  with a fake `IReadingEventRecorder` asserting the calls at open + finish.

**Depends on:** all prior steps.
**Verify:** `dotnet test` targeted `--filter` per suite (never the full App suite — headless flake).
`Paperbunkr.Data.Tests` and `Paperbunkr.Plugins.Tests` full runs green.

---

## Ordering / parallelism

```
Step 1 ─┬─ Step 2 (migration + backfill)
        ├─ Step 3 (recorder) ─── Step 4 (wire readers)
        └─ Step 5 (resolver) ──┐
Step 6 (charts, independent) ──┼─ Step 7 (screen VM) ── Step 8 (view + nav) ── Step 9 (tests)
                               ┘
```

Steps 2, 3+4, 5, and 6 are mutually independent after Step 1. Step 9's tests can be written
alongside each step and are listed together only for clarity.

## Out of scope (design §2)

Activity calendar, time-of-day heatmap, most-read-this-year, backlog-trend, format-split tiles;
configurable thresholds; incremental aggregates; live chart restyle on skin change; export.

## Roadmap

After it ships and is on-screen-verified: add a row to `docs/alpha-todo.md`'s Beta backlog / the
roadmap's Beta section with the commit ref and what was verified (not just what the commit claims).
