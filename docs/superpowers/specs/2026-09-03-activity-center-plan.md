# Activity Center — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-03-activity-center-design.md*

## Decisions taken on the design's open questions (to keep momentum)

1. **Idle indicator** — a faint idle dot (discoverable), not fully hidden.
2. **Drawer** — no scrim; fully non-blocking.
3. **Status-bar left region (v1)** — library total + size only (`"2,329 books · 9.2 GB"`),
   sourced once from the DB and refreshed on the `Changed` event. Live per-screen selection count
   is deferred (needs every screen VM to feed it — not worth it for v1).
4. **`Scrape`** — the `ActivityJobKind.Scrape` value exists, but no caller is migrated to it in v1
   (there is no bundled bulk-scrape op yet; it lands with the bulk-scraper feature).

## Convention corrections vs. the design doc

- **Enums are stored as strings**, not ints (`PaperbunkrDbContext.OnModelCreating` opening comment):
  `builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32)`. Brand-new table → no
  `HasDefaultValue` / `HasSentinel` needed (nothing to backfill).
- Migration test lives in `Paperbunkr.Data.Tests`, one file, mirroring
  `AddLastContentTypeSweepUtcMigrationTests` (up-migrates, round-trips, down-migrates one step).

---

## Step 1: `ActivityRun` entity + context + migration

**Files:**
- `src/Paperbunkr.Data/Entities/ActivityRun.cs` (new)
- `src/Paperbunkr.Data/Entities/ActivityJobKind.cs` (new — enum)
- `src/Paperbunkr.Data/Entities/ActivityTrigger.cs` (new — enum)
- `src/Paperbunkr.Data/Entities/ActivityRunStatus.cs` (new — enum: `Succeeded`, `Failed`, `Cancelled`, `Interrupted`)
- `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit — `DbSet<ActivityRun> ActivityRuns`, entity config)
- `src/Paperbunkr.Data/Migrations/*_AddActivityRuns.cs` (new — `dotnet ef migrations add`)
- `src/Paperbunkr.Data/Migrations/PaperbunkrDbContextModelSnapshot.cs` (regenerated)

**What:** `ActivityRun` fields — `Id` (int identity, matching every other entity's `HasKey(x => x.Id)`),
`Kind`, `Title` (required), `Trigger`, `StartedUtc`, `FinishedUtc` (nullable), `Status`,
`ResultSummary` (nullable), `ResultLinkKind` (nullable string — store the `ActivityLinkKind` name
directly, no separate enum column type), `ResultLinkPayload` (nullable), `ItemsProcessed`
(nullable int), `ItemsFailed` (nullable int). Config: `HasKey(Id)`; enum props
`HasConversion<string>().HasMaxLength(32)`; `HasIndex(r => r.StartedUtc)`. Only persisted on
terminal state, so `ActivityRunStatus` has no `Queued`/`Running`.

**Depends on:** none.
**Verify:** `dotnet build src/Paperbunkr.Data`; migration file is a clean `CreateTable`/`DropTable`.
Generate the migration against a **throwaway** `--connection` or restore the dev DB afterward
(`feedback_worktree_shares_user_db`).

## Step 2: migration test

**Files:** `src/Paperbunkr.Data.Tests/AddActivityRunsMigrationTests.cs` (new)
**What:** Mirror `AddLastContentTypeSweepUtcMigrationTests`: migrate to HEAD, insert an
`ActivityRun`, round-trip it, assert the `StartedUtc` index exists
(`SELECT ... FROM pragma_index_list('ActivityRuns')`), migrate down one step
(`PriorMigration = "..._AddActivityRuns"`'s predecessor), assert the table is gone.
**Depends on:** Step 1.
**Verify:** `dotnet test src/Paperbunkr.Data.Tests --filter AddActivityRunsMigrationTests`.

## Step 3: activity model types (App)

**Files:**
- `src/Paperbunkr.App/Models/ActivityJob.cs` (new — `ObservableObject`: `Id` (Guid), `Kind`,
  `Title`, `Detail`, `Status` (`ActivityJobStatus`), `Done?`, `Total?`, `IsIndeterminate`,
  `Trigger`, `StartedUtc`, `FinishedUtc?`, `ResultSummary?`, `ResultLink?`, `IsUpkeep`;
  computed `Fraction` like `ToastProgressViewModel.Fraction`)
- `src/Paperbunkr.App/Models/ActivityJobStatus.cs` (new — `Queued`, `Running`, `Succeeded`, `Failed`, `Cancelled`)
- `src/Paperbunkr.App/Models/ActivityAlert.cs` (new — `Id` (Guid), `Severity` (`ActivityAlertSeverity`), `Title`, `Detail?`, `ActionLabel?`, `ActionLink?`, `DedupeKey`, `CreatedUtc`)
- `src/Paperbunkr.App/Models/ActivityAlertSeverity.cs` (new — `Info`, `Warning`, `Error`)
- `src/Paperbunkr.App/Models/ActivityLink.cs` (new — `ActivityLinkKind Kind`, `string Payload`)
- `src/Paperbunkr.App/Models/ActivityLinkKind.cs` (new — `LibrarySavedFilter`, `SeriesDetail`, `UpdateChangelog`, `MigrationReview`, `Preferences`)

**What:** Plain data/VM-ish types, `CommunityToolkit.Mvvm` `ObservableObject` where they mutate
during a run (`ActivityJob`). `ActivityJobKind` / `ActivityTrigger` are reused from
`Paperbunkr.Data.Entities` (Step 1) — App already references Data.
**Depends on:** Step 1 (reuses the two enums).
**Verify:** `dotnet build src/Paperbunkr.App`.

## Step 4: `IActivityService` / `ActivityService`

**Files:**
- `src/Paperbunkr.App/Services/IActivityService.cs` (new)
- `src/Paperbunkr.App/Services/ActivityService.cs` (new)
- `src/Paperbunkr.App/Services/ActivityJobHandle.cs` (new — internal, `IActivityJobHandle` + `IDisposable`)
- `src/Paperbunkr.App/Services/ActivityHistoryStore.cs` (new — DB read/write/prune helper over `PaperbunkrDb.CreateContext`)

**What:**
- `ActivityService` holds `ObservableCollection<ActivityJob>` (active) + a bounded in-memory
  `RecentJobs` tail (last ~20) + `ObservableCollection<ActivityAlert>`. All mutations marshalled
  via `Dispatcher.UIThread.Post` (check `Dispatcher.UIThread.CheckAccess()` first — tests run
  headless with a real dispatcher, background jobs call from the thread pool).
- `StartJob(kind, title, cancellable, trigger)` → creates `ActivityJob` (`Queued` if paused, else
  `Running`), a `CancellationTokenSource`, returns `ActivityJobHandle`.
- `ActivityJobHandle.Report(done,total,detail?)` / `Report(detail)` — coalesced to ≤10/s per handle
  (timestamp gate), updates the job in place.
- `Succeed` / `Fail` / dispose-without-terminal(→`Cancelled`): set terminal state, move job
  active→recent, write one `ActivityRun` via `ActivityHistoryStore.Record(...)`, raise
  `CompletionToastRequested` unless suppressed, raise `Changed`.
- `PauseAll()` / `ResumeAll()`: `PauseAll` cancels every running job's token and flips a `_paused`
  flag so new jobs enter `Queued`; `ResumeAll` clears the flag and starts queued jobs (they are
  re-run by their owners — v1 has no job re-entrancy, so "queued" jobs whose owner already returned
  just get dropped to `Cancelled`; **simplify: v1 `PauseAll` only affects jobs that cooperatively
  check the token — document that queued-then-resumed is best-effort**).
- `RaiseAlert` — dedupe by `DedupeKey` (refresh `CreatedUtc`, keep one row); `DismissAlert`.
- `ActivityHistoryStore`: `Record(run)`, `Query(filter, skip, take)` (`AsNoTracking`, order by
  `StartedUtc desc`), `PruneOnStartup()` (keep newer of "200 rows" / "< 30 days"),
  `MarkInterruptedOnStartup()` — **not needed**: `Queued`/`Running` are never persisted, so there
  are no stale rows to rewrite. Drop `Interrupted` from scope; keep the enum value unused-reserved.

**Depends on:** Steps 1, 3.
**Verify:** builds; unit tests in Step 8.

## Step 5: ViewModels

**Files:**
- `src/Paperbunkr.App/ViewModels/StatusBarViewModel.cs` (new)
- `src/Paperbunkr.App/ViewModels/ActivityCenterViewModel.cs` (new)
- `src/Paperbunkr.App/ViewModels/ActivityAlertViewModel.cs` (new — thin wrapper w/ `DismissCommand`, `FollowLinkCommand`)

**What:**
- `StatusBarViewModel(IActivityService, Func<(int books,long bytes)> libraryStatsProvider)` —
  `ContextText`, `HasActivity`, `IndicatorText` (`"◐ 340 / 1,200"` / `"◐ 2 running"` / idle dot),
  `IsPulsing`, `UnreadAlertCount`, `ToggleActivityCenterCommand`. Recomputes on `Changed`.
- `ActivityCenterViewModel(IActivityService, Func<ActivityLink,Task> followLink)` — `IsPeekOpen`,
  `IsDrawerOpen`, `ActiveTab` (`Active`/`History`); projections `RunningJobs`, `QueuedJobs`,
  `RecentJobs` (peek shows top 3), `Alerts`; `HistoryRows` (lazy, paged from
  `ActivityHistoryStore`) + filter props (`HistorySearch`, `HistoryTypeFilter`, `HistoryAgeFilter`,
  `HistoryFailuresOnly`); commands `PauseAllCommand`, `ResumeAllCommand`, `ClearFinishedCommand`,
  `OpenDrawerCommand`, `CloseCommand`, `CancelJobCommand`, `DismissAlertCommand`,
  `RunAgainCommand` (re-invokes via a stored `Func<Task>` on the run, when present — else hidden),
  `FollowLinkCommand`.
- `ClearFinishedCommand` clears `RecentJobs` + resolved history-tail only; never touches alerts or
  the upkeep job.

**Depends on:** Step 4.
**Verify:** VM tests in Step 8.

## Step 6: Views + shell wiring

**Files:**
- `src/Paperbunkr.App/Views/StatusBar.axaml` + `.axaml.cs` (new — **same step**, per the AVLN2000 gotcha)
- `src/Paperbunkr.App/Views/ActivityPeekView.axaml` + `.axaml.cs` (new)
- `src/Paperbunkr.App/Views/ActivityDrawerView.axaml` + `.axaml.cs` (new)
- `src/Paperbunkr.App/Views/ActivityJobRow.axaml` + `.axaml.cs` (new — shared row)
- `src/Paperbunkr.App/Views/ActivityAlertRow.axaml` + `.axaml.cs` (new — shared row)
- `src/Paperbunkr.App/Views/MainWindow.axaml` (edit — `StatusBar` docked `Bottom` inside the
  `DockPanel` before the content `Grid`, `IsVisible="{Binding !Reader.IsFullscreen}"`; drawer as an
  overlay sibling in the root `Grid` next to the migration overlay)
- `src/Paperbunkr.App/Views/MainWindow.axaml.cs` (edit — no new plumbing needed; the peek is a
  `Popup`/`Flyout` in XAML, the drawer is `IsVisible`-bound)
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — construct `ActivityService`, expose
  `StatusBar` + `ActivityCenter` properties, pass `IActivityService` into the screen VMs that need
  it, wire `ResolveActivityLink`, subscribe `CompletionToastRequested → ShowToast`)
- `src/Paperbunkr.App/App.axaml.cs` (edit — call `ActivityHistoryStore.PruneOnStartup()` in the
  same fire-and-forget `Task.Run` block as the auto-backup / content-type sweep)
- `src/Paperbunkr.App/Styles/Primitives.axaml` (edit — add job-status chip brush tokens if the
  existing `PbBadge*` / accent / muted set doesn't cover all three states)

**What:** Wireframes from the brainstorming session. Semantic tokens only, `DynamicResource`, no
hex. Motion per `avalonia-pro-max/motion`: indicator opacity pulse (only while jobs run), peek
fade+rise 150 ms, drawer `translateY` 220 ms. Icon-only buttons get `AutomationProperties.Name` +
≥36×36. Drawer moves focus in on open, restores to the indicator on close, `Esc` closes.

**Depends on:** Steps 4, 5.
**Verify:** `dotnet build` then launch the exe (per CLAUDE.md — "0 Errors" is not proof the XAML
weave ran); status bar visible, hides in reader fullscreen, peek opens, drawer slides.

## Step 7: ambient "Background upkeep" rollup

**Files:**
- `src/Paperbunkr.App/Services/LiveFolderWatchService.cs` (edit)
- `src/Paperbunkr.App/Services/CoverThumbnailService.cs` / `BookCoverThumbnailService.cs` (edit — or wrap at call site)
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — register the single upkeep job)

**What:** `IActivityService.RegisterUpkeep(...)` returns a lightweight handle the watch/decoder
services flip to active/idle with a detail string ("watching 4 folders · decoding 12 covers"). The
upkeep job is excluded from aggregate progress, from `ClearFinished`, and never reaches a terminal
state. If threading this through the thumbnail services is invasive, v1 scope can be **folder-watch
only** for the upkeep row — note it in the doc and file a follow-up.

**Depends on:** Step 4.
**Verify:** manual — drop a file into a watched folder, upkeep row activates.

## Step 8: migrate existing callers, delete the progress toast

**Files:**
- `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit — book scan, library scan,
  Generate Covers, Sync Metadata: swap `ToastProgressViewModel` + `_showProgressToast` /
  `_closeProgressToast` for `IActivityService.StartJob`; ctor loses the two delegate params, gains
  `IActivityService`)
- `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit — `DownloadUpdateAsync` becomes a real
  job; delete `ProgressToastRequested` / `ProgressToastCloseRequested` / `ShowProgressToast` /
  `CloseProgressToast`; `Preferences` ctor call updated)
- `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs`, Reading-list screen VM, tracker-fetch
  call sites, migration passes (edit — route through `IActivityService`; grep
  `ToastProgressViewModel` / `showProgressToast` for the full list)
- `src/Paperbunkr.App/Views/MainWindow.axaml.cs` (edit — delete the `_progressToasts` dict + the
  two `ProgressToast*` event subscriptions)
- `src/Paperbunkr.App/ViewModels/ToastProgressViewModel.cs` (**delete**)
- `src/Paperbunkr.App/Views/ToastProgressView.axaml` + `.axaml.cs` (**delete**)
- `src/Paperbunkr.App.Tests/*` (edit — any test referencing `ToastProgressViewModel` or the
  removed delegates; `DragImportServiceTests` etc. unaffected — `DragImportService` itself takes a
  plain `Progress<>` and is called by a VM, so only the VM call site changes)

**What:** Each migrated command: `using var job = _activity.StartJob(kind, title, trigger: …);` +
`new Progress<(int,int)>(p => job.Report(p.done, p.total))` + `job.Succeed(summary, link?)` /
`job.Fail(summary, ex: ex)`; keep the `IsXxx` guard flags. Completion `ShowToast` calls are
removed — `ActivityService` raises them from `CompletionToastRequested`.

**Depends on:** Steps 4–6.
**Verify:** `dotnet build`; `dotnet test src/Paperbunkr.App.Tests`; launch + run Generate Covers
from Preferences → progress shows in the status bar + panel, completion toast fires.

## Step 9: tests + review

**Files:**
- `src/Paperbunkr.App.Tests/ActivityServiceTests.cs` (new)
- `src/Paperbunkr.App.Tests/ActivityCenterViewModelTests.cs` (new)
- `src/Paperbunkr.App.Tests/StatusBarViewModelTests.cs` (new)
- `src/Paperbunkr.App.Tests/ActivityCenterHeadlessTests.cs` (new — headless render smoke, if the
  suite has an existing Avalonia headless fixture; otherwise skip and note manual verification)

**What:** job lifecycle; concurrent jobs + aggregate progress; indeterminate/determinate mix →
count form; cancel trips the token; cancel-after-terminal is a no-op; `PauseAll` cancels running
tokens; completion toast only when panel closed; alert dedupe + dismiss; `ActivityLink` resolution
dispatches to the right shell command per kind; history filter predicates; `ClearFinished` leaves
alerts + upkeep intact; `ActivityHistoryStore` prune keeps newer of 200-rows / 30-days.

**Depends on:** Steps 1–8.
**Verify:** `dotnet test src/Paperbunkr.App.Tests src/Paperbunkr.Data.Tests` (full suites green —
App was 1598/1598, Data 730/730 pre-change, plus the new tests). Then run
`~/.claude/skills/avalonia/avalonia-pro-max/review-checklist/SKILL.md` end-to-end. Update
`docs/alpha-todo.md` (Beta backlog — the `ce-feature-inventory.md:199` row) with what actually
shipped.

---

## Not in this plan (design "Later" — seams only)

Scheduled tab + scheduler · queue reordering · per-job pause · retry-failed as a first-class
re-run · remote/server jobs · top-bar entry point · server-request counters.
