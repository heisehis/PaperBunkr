# Activity Center — Design

A persistent bottom **status bar** with a live activity indicator that opens a two-tier
**Activity Center**: a quick-glance **peek popover** (tier 1) and a full **drawer** (tier 2) that
unify every background job and every peripheral alert the app produces, backed by a persisted run
history. Replaces the scattered one-at-a-time progress toasts.

Date: 2026-09-03. Status: design approved (brainstorming), pending implementation plan.

---

## CE-parity check (standing rule)

ComicRack CE's `TasksDialog` (`_reference/ComicRackCE/ComicRack/Dialogs/TasksDialog.cs`) is
**primarily a server/sharing activity monitor** — its tabs show *Clients*, *Info Requests*,
*Library Requests n/m*, *Page Requests*, *Thumbnail Requests*, *Failed Authentications*, bucketed by
*Last Minute / Last 5 Minutes / Last Hour / Session*. Alongside that it lists
`QueueManager.IPendingTasks` — a fixed set of engine queues (`UpdateComicBookDynamicQueue`,
`ExportComicsQueue`, `ReadComicBookInfoFileQueue`, `WriteComicBookInfoFileQueue`,
`DeviceSyncQueue`), each a `Group` with per-item *Waiting / Running / Completed* state, a progress
message, and an `Abort` command. Errors live in separate `SmartList`s (`ExportErrors`,
`DeviceSyncErrors`, `UpdateErrors`). There is **no persisted history** — it is a live view of
in-memory queues, and it is modal.

`docs/ce-feature-inventory.md:199` already records "Background job/task monitor for server activity"
as *decided: build, needs its own design spec*, scoped to the dormant server feature.

**This design is a deliberate expansion, not literal parity.** It keeps CE's good bones — named job
groups, waiting/running/finished states, a per-job abort, errors as first-class — and generalises
them to *all* background work (not just the server), adds a **persisted, searchable history** CE
never had, and makes the surface **non-modal** (status-bar → popover → drawer). The server-request
counters CE showed are out of scope until the server feature itself exists; the drawer's tab model
leaves room for them.

---

## Goals

- One place that answers "what is the app doing right now?" and "what did it do earlier?"
- Every existing background operation reports through it: library folder scan, book folder scan,
  Generate Covers, Sync Metadata, tracker fetches (AniList / MangaBaka / ComicVine), drag-drop
  import, update download, CE migration passes.
- Long jobs are cancellable; a global "Pause all"; finished jobs with output link through to the
  affected items ("Review 12 failures →").
- History survives an app restart.
- Retire the single live progress toast (`ToastProgressViewModel`); keep lightweight completion /
  error toasts as thin surfacings of activity events.

## Non-goals (v1)

- **Scheduled / recurring jobs** and the scheduler that would drive them — the drawer reserves a
  *Scheduled* tab slot, nothing behind it yet.
- Queue **reordering**, **per-job pause/resume** (only "Pause all"), **retry-failed** as a
  first-class re-run (v1 offers a plain "Run again" that re-invokes the original command).
- **Remote / server jobs** in the panel — rides on the server feature (`ce-feature-inventory.md:198`).
- The **top-bar** entry point (option C from brainstorming) — additive later; v1 ships the status
  bar only.
- Server-request counters (Clients / Page Requests / …).

---

## Scope split (agreed in brainstorming)

**v1 — build now**

| Piece | Notes |
|---|---|
| Persistent bottom **status bar** | New chrome region, docked in `MainWindow`'s `DockPanel`. Left: library/selection context. Right: live activity indicator. Hidden in Reader fullscreen, same as the nav rail. |
| **Live indicator** | Idle → hidden or a flat dot. Active → count + rolled-up progress ("◐ 340 / 1,200"), subtle pulse. Click toggles the peek. |
| Tier 1 — **peek popover** | Anchored above the indicator, dismiss on click-away. Sections: Running, Queued, Finished (last few), Alerts. Footer: Pause all · Clear finished · "See all →". |
| Tier 2 — **drawer** | Slides up from the status bar, ~half window height. Tabs: **Active**, **History**. (*Scheduled* tab visible but disabled/"coming soon".) |
| Job lifecycle | Queued → Running → Succeeded / Failed / Cancelled. Determinate (done/total) or indeterminate. Cancel a running job; Pause all. |
| **Persisted history** | `ActivityRun` rows in `Paperbunkr.Data`, written on terminal state, with a retention cap. History tab reads from the DB; searchable + filterable (type, age, failures-only). |
| **Deep-link results** | A finished job may carry an `ActivityLink` the shell resolves into a navigation (e.g. Library filtered to the failed-import set). |
| **Alerts** | Non-job signal: update available, folder went offline, API rate-limited, plugin command failed. Severity (info / warn / error), dismissable, deduped. |
| **Ambient rollup** | Live folder-watch + background thumbnail decode surface as a single collapsed "Background upkeep" job row, expandable to recent events. |
| Migrate existing callers | Replace `_showProgressToast` / `_closeProgressToast` wiring with `IActivityService`. |

**Later — seams left, not built**

Scheduled tab + scheduler · queue reorder · per-job pause · retry-failed as re-run · remote/server
jobs · top-bar entry point · server-request counters.

---

## UX anatomy

### Status bar

- New element, `DockPanel.Dock="Bottom"`, added inside `MainWindow.axaml`'s existing `DockPanel`
  **before** the main-content `Grid` (which stays `LastChildFill`). ~24 px tall.
- `IsVisible="{Binding !Reader.IsFullscreen}"` — matches the nav rail's own fullscreen handling
  (`MainWindow.axaml:176`).
- **Left region:** contextual text — library totals ("2,329 books · 9.2 GB") or current selection
  count, mirroring CE's bottom-bar text. Owned by `StatusBarViewModel`, fed by whatever screen is
  active; v1 can start with just the library total and a selection count.
- **Right region:** the activity indicator (a real `Button`,
  `AutomationProperties.Name="Activity"`).

### Live indicator states

| App state | Indicator |
|---|---|
| Nothing running, no unread alerts | Hidden (or a faint idle dot — decide in review) |
| Jobs running | `◐ {aggregateDone} / {aggregateTotal}` or `◐ {n} running` when any job is indeterminate; slow opacity pulse (animate `Opacity` only) |
| Only unread alerts | Bell + count badge, severity-tinted |
| Both | Progress text + a small badge dot |

Aggregate progress = sum of determinate jobs' done/total; if any running job is indeterminate, show
the count form instead of a fraction.

### Tier 1 — peek popover

`Popup` / `Flyout` anchored to the indicator, ~300 px wide, max ~360 px tall, internal scroll.
Fade + 8 px rise on open (150 ms), exit ~100 ms, explicit easing (`avalonia-pro-max/motion`).

Sections, each shown only when non-empty:

- **Running · n** — job rows with a progress bar and a cancel (`✕`).
- **Queued · n** — job rows with a "Waiting" chip and a one-line reason ("Starts after scan finishes").
- **Finished** — the last ~3 settled jobs, status chip (Done / n failed), relative time, optional
  action link.
- **Alerts** — alert rows, newest first, each dismissable.
- **Footer** — `Pause all` · `Clear finished` · `See all →` (opens the drawer on the Active tab).

### Tier 2 — drawer

Slides up from the status bar to ~50% window height, rounded top corners, `BoxShadow` above.
Slide-up 200–250 ms (animate `RenderTransform`), scrim optional (lean: no scrim, the app stays
usable behind it). Dismiss: click the indicator again, `Esc`, or a chevron-down in the drawer
header. Focus moves into the drawer on open and returns to the indicator on close.

Header: tab strip **Active `n`** · **History** · **Scheduled** (disabled) — plus `Pause all` and a
`Settings ⚙` affordance (retention length, "show idle dot", per-type "notify on completion").

- **Active tab** — every Running + Queued job, full width, richer detail line, cancel per job,
  Pause all. No reordering in v1 (rows are start-order).
- **History tab** — DB-backed. Toolbar: search box, type filter, age filter ("Last 7 days"),
  "Failures only" toggle. Each row: status icon, name, trigger ("manual" / "drag-drop" /
  "scheduled" / "startup"), start time, **result summary**, relative finish time, and a row action
  (`Run again`; `Review …` deep-link when the run carries one).
- **Scheduled tab** — present but disabled with a "coming soon" note, so the IA is stable.

### Job row — states & content

```
{icon}  {Name}                                  {cancel ✕ | status chip}
        {detail: "340 / 1,200 files · ~2 min left" | "18 series updated" | "12 failed (corrupt)"}
        {progress bar — running & determinate only}
        {action link — finished with output only:  "Review 12 failures →"}
```

- **Status chips** are always icon + text, never colour alone (`avalonia-pro-max` a11y rule):
  `Waiting`, `Running`, `Done`, `n failed`, `Cancelled`.
- **Cancel** (`✕`) shows on running/queued rows; confirmation only if the job declares itself
  non-safely-cancellable (v1: none do).
- **Detail line** is supplied by the job as it runs (free text) and frozen to the result summary
  when it settles.

### Alerts

`ActivityAlert`: `Severity` (Info / Warning / Error), `Title`, optional `Detail`, optional
`(label, ActivityLink)` action, `DedupeKey`, `CreatedUtc`, `Dismissed`.

- Left-edge severity bar + matching icon.
- Deduped by `DedupeKey` — re-raising "folder X offline" updates the timestamp of the existing
  alert instead of stacking.
- Dismiss per alert; "Clear finished" in the peek footer does **not** touch alerts (separate
  "Dismiss all alerts" in the drawer).
- Alerts are **not** persisted to history in v1 (they are session-scoped signal); the update-available
  alert is re-derived from the update service on each launch anyway.

### Ambient "Background upkeep" rollup

`LiveFolderWatchService` and the thumbnail-decode services register **one** long-lived job of kind
`Upkeep` at startup. It sits Idle (not shown in Running) and flips to Running with a detail line
("watching 4 folders · decoding 12 covers") only while actually working. Expanding it in the drawer
shows the last ~24 h of discrete watch events (file added / removed / moved). This keeps continuous
plumbing visible per the brainstorming decision without letting it dominate the list.

### Toast relationship

- The live **progress toast is removed** — `ToastProgressViewModel` / `ToastProgressView` and the
  `ProgressToastRequested` / `ProgressToastCloseRequested` plumbing on `MainViewModel` are deleted.
  Running progress lives in the status bar + panel.
- **Completion / failure toasts stay** but are raised *by the Activity Center*, not by each caller:
  when a job settles, `ActivityService` raises a toast (`"Covers generated — checked 1,204 issues"`
  / `"Import finished — 12 failed"`) unless the panel is currently open. Per-type "notify on
  completion" is a drawer setting.
- `ShowToast(title, message)` on `MainViewModel` stays for genuinely one-off notices (minimize-to-
  tray, plugin errors) — those may *also* create an alert if they represent standing state.
- `UpdateReadyToastViewModel` (restart-to-apply, action buttons) is unchanged.

---

## Architecture / components

No DI container in this codebase — everything is hand-composed in `MainViewModel`'s constructor
(`MainViewModel.cs:100+`). The Activity Center follows the same pattern and the same "plain event on
the shell VM, `MainWindow` subscribes once the Window exists" plumbing already used for toasts
(`MainViewModel.cs:159-206`).

### `IActivityService` / `ActivityService` — `Paperbunkr.App/Services`

The single registry. Constructed in `MainViewModel` and passed to every screen VM that starts
background work (replacing the `showProgressToast` / `closeProgressToast` delegates threaded through
today, e.g. `PreferencesScreenViewModel` ctor at `PreferencesScreenViewModel.cs:70`).

```csharp
public interface IActivityService
{
    // Start a job. Returns a handle the caller drives.
    IActivityJobHandle StartJob(ActivityJobKind kind, string title,
                                bool cancellable = true, ActivityTrigger trigger = ActivityTrigger.Manual);

    void RaiseAlert(ActivityAlert alert);          // deduped by alert.DedupeKey
    void DismissAlert(Guid alertId);

    void PauseAll();
    void ResumeAll();

    // Observable state for the VMs (ObservableCollection, UI-thread affine)
    IReadOnlyList<ActivityJob> ActiveJobs { get; }
    IReadOnlyList<ActivityJob> RecentJobs { get; }   // in-memory tail of history
    IReadOnlyList<ActivityAlert> Alerts { get; }
    event EventHandler? Changed;
    event Action<string,string>? CompletionToastRequested;
}

public interface IActivityJobHandle
{
    CancellationToken CancellationToken { get; }     // trips on job cancel AND on PauseAll
    void Report(int done, int total, string? detail = null);
    void Report(string detail);                       // indeterminate
    void Succeed(string summary, ActivityLink? link = null);
    void Fail(string summary, ActivityLink? link = null, Exception? ex = null);
    // IDisposable: disposing without Succeed/Fail = Cancelled
}
```

- **Threading:** jobs run on the thread pool exactly as today (`Task.Run` / `await …Async`).
  `ActivityService` marshals every mutation of its observable collections onto the UI thread via
  `Dispatcher.UIThread.Post`. `Report` is cheap and coalesced (≤ ~10/s per job) so a tight loop
  calling `Report` doesn't flood the dispatcher — mirrors today's `Progress<T>` throttling
  expectation.
- **Cancel / Pause:** `StartJob` owns a `CancellationTokenSource`; cancelling one job or `PauseAll`
  cancels the token. Callers already take a `CancellationToken` in most `…Async` paths; where they
  take `Progress<(int,int)>` today (`PreferencesScreenViewModel.cs:1268`), the adapter wraps
  `handle.Report`.
- **Persistence:** on `Succeed` / `Fail` / cancel, write one `ActivityRun` row. On app start,
  any `ActivityRun` still in `Running`/`Queued` (process died mid-job) is rewritten to
  `Interrupted`. Retention: keep the most recent `N` (default 200) + anything < 30 days, whichever
  is larger; prune on startup.

### Models — `Paperbunkr.App/Models`

- `ActivityJob` — `Id`, `Kind`, `Title`, `Detail`, `Status`, `Done?`, `Total?`, `IsIndeterminate`,
  `Trigger`, `StartedUtc`, `FinishedUtc?`, `ResultSummary?`, `ResultLink?`, `IsUpkeep`.
- `ActivityJobKind` enum — `LibraryScan`, `BookScan`, `GenerateCovers`, `SyncMetadata`,
  `TrackerFetch`, `Scrape`, `Import`, `Update`, `Migration`, `Upkeep`, `Other`.
- `ActivityJobStatus` enum — `Queued`, `Running`, `Succeeded`, `Failed`, `Cancelled`, `Interrupted`.
- `ActivityTrigger` enum — `Manual`, `DragDrop`, `Startup`, `Scheduled`, `Plugin`, `Watch`.
- `ActivityAlert` — as above.
- `ActivityLink` — `{ ActivityLinkKind Kind, string Payload }`. Kinds v1: `LibrarySavedFilter`
  (payload = a serialized transient filter / id set), `SeriesDetail`, `UpdateChangelog`,
  `MigrationReview`, `Preferences`. Resolved centrally (below).

### `ActivityLink` resolution — `MainViewModel`

A single `ResolveActivityLink(ActivityLink)` on the shell, wired into `ActivityCenterViewModel`,
that switches on `Kind` and performs the navigation using existing commands
(`GoLibraryCommand` + a transient filter, `OpenMangaDetail`, open the update overlay, open the
migration overlay, `GoPreferencesCommand`). New link kinds are added here, not in the panel.

### Persistence — `Paperbunkr.Data`

- New entity `ActivityRun` (`Entities/ActivityRun.cs`): `Id` (Guid), `Kind` (int),
  `Title`, `Trigger` (int), `StartedUtc`, `FinishedUtc`, `Status` (int), `ResultSummary`,
  `ResultLinkKind` (int?), `ResultLinkPayload` (string?), `ItemsProcessed` (int?),
  `ItemsFailed` (int?).
- `DbSet<ActivityRun> ActivityRuns` on `PaperbunkrDbContext`, indexed on `StartedUtc`.
- One EF migration (`AddActivityRuns`), following the current chain
  (`20260903211057_AddMetadataWriteBackSettings` is HEAD). **Worktree DB caution**
  (`feedback_worktree_shares_user_db`): the dev run shares `%APPDATA%\Paperbunkr\paperbunkr.db`.
- Enum columns stored as `int` with an explicit sentinel-free conversion — see the
  `HasSentinel` gotcha noted in `project_paperbunkr_preferences_reader_tab`.
- History reads: `AsNoTracking`, paged, ordered by `StartedUtc desc` — same discipline as the
  library-lag work (`project_paperbunkr_thumbnail_decode_off_ui_thread`).

### ViewModels — `Paperbunkr.App/ViewModels`

- `StatusBarViewModel` — left-region context text + the indicator's aggregate state; subscribes to
  `IActivityService.Changed`. Owns `ToggleActivityCenterCommand`.
- `ActivityCenterViewModel` — backs both tiers. `IsPeekOpen`, `IsDrawerOpen`, `ActiveTab`;
  `RunningJobs` / `QueuedJobs` / `FinishedJobs` / `Alerts` projections; `HistoryPage` (lazy, DB);
  `PauseAllCommand`, `ClearFinishedCommand`, `OpenDrawerCommand`, `CancelJobCommand`,
  `DismissAlertCommand`, `RunAgainCommand`, `FollowLinkCommand`, history filter properties.
- `ActivityJobViewModel` / `ActivityAlertViewModel` — per-row, `CommunityToolkit.Mvvm`
  `ObservableObject`, `Fraction` computed like `ToastProgressViewModel.Fraction` does today.

### Views — `Paperbunkr.App/Views`

- `StatusBar.axaml` (+ `.axaml.cs` — mandatory same-step code-behind per the CLAUDE.md AVLN2000
  gotcha).
- `ActivityPeekView.axaml` — hosted in a `Popup`/`Flyout` off the indicator button.
- `ActivityDrawerView.axaml` — an overlay sibling inside `MainWindow.axaml`'s root `Grid` (beside
  the migration overlay at `MainWindow.axaml:844`), with a `RenderTransform` slide + `IsVisible`
  bound to `ActivityCenter.IsDrawerOpen`.
- `ActivityJobRow.axaml` / `ActivityAlertRow.axaml` — shared `DataTemplate`s used by both tiers.
- Styling: semantic tokens only, `DynamicResource`, no hex in views
  (`reference_avalonia_pro_max`). New tokens if needed: a job-status chip palette (success / danger
  / neutral) mapped to existing `PbBadgeBrush` / accent / muted.

### Caller migration

Each existing background command changes from the
`IsBusy` flag + `ToastProgressViewModel` + `_showProgressToast/_closeProgressToast` +
completion `_showToast` shape (see `PreferencesScreenViewModel.GenerateCovers`,
`PreferencesScreenViewModel.cs:1255`) to:

```csharp
using var job = _activity.StartJob(ActivityJobKind.GenerateCovers, "Generating covers");
var progress = new Progress<(int done,int total)>(p => job.Report(p.done, p.total));
try
{
    await new CoverThumbnailService(_contextFactory).GenerateAllAsync(progress, job.CancellationToken);
    job.Succeed($"Checked {total} issues");
}
catch (OperationCanceledException) { throw; }   // handle disposes → Cancelled
catch (Exception ex) { job.Fail("Cover generation failed", ex: ex); }
```

The `IsGeneratingCovers` / `IsSyncingMetadata` guard flags stay (they gate the buttons); they can
later bind to `_activity` job presence instead, but that's not required for v1.

Call sites to migrate (grep `ToastProgressViewModel` / `_showProgressToast`): `PreferencesScreen`
(book scan, library scan, generate covers, sync metadata), the update download
(`MainViewModel.DownloadUpdateAsync`, `MainViewModel.cs:1477` — note it currently abuses the
progress toast for a 0–100 %; becomes a normal job), drag-drop import (`DragImportService`),
tracker fetches, CE migration passes.

---

## Motion

Per `avalonia-pro-max/motion`: animate `Opacity` / `RenderTransform` / brushes only.

- Indicator pulse: `Opacity` 1 → 0.55 → 1, ~1.6 s loop, only while jobs run.
- Peek open: fade + `translateY(8→0)`, 150 ms, `PbMotionEase`; close ~100 ms.
- Drawer: `translateY(100%→0)`, 220 ms enter / ~150 ms exit.
- Progress bars: value change eased ~120 ms so counts don't visibly jump.
- Respect reduced-motion (skip pulse + slide, keep fade).

## Accessibility

- Indicator + every row action are real focusable controls with `AutomationProperties.Name`;
  icon-only buttons ≥ 36×36 hit area.
- Aggregate progress announced ("Activity: 2 jobs running").
- Drawer traps focus while open, restores to the indicator on close; `Esc` closes.
- Status conveyed by icon **and** text, never colour alone.
- Status bar text has AA contrast on `PbChromeBrush`.

---

## Edge cases

- **Job started before the Window exists** — `ActivityService` is constructed with `MainViewModel`
  (before the Window), same as the toast plumbing; it queues state and the `StatusBar` /
  `ActivityCenter` VMs bind once `MainWindow` sets its DataContext. A job that starts and finishes
  in that window still lands in history.
- **App quit mid-job** — persisted `Running`/`Queued` rows → `Interrupted` on next startup; no
  attempt to resume.
- **Pause all, then a new job starts** — new jobs enter `Queued` and do not run until `ResumeAll`.
- **Cancel race** — `Succeed`/`Fail` after cancel is ignored (handle checks its own terminal flag).
- **History bloat** — retention prune on startup; History tab is paged, never loads all rows.
- **Duplicate alerts** — `DedupeKey` collapses; timestamp refreshes, unread state re-arms.
- **Upkeep job never ends** — excluded from aggregate progress and from "Clear finished"; it has no
  terminal state by design.
- **Reader fullscreen** — status bar hidden; a job finishing while hidden still toasts (unless
  suppressed) and is in the panel when the bar returns.
- **Very long job title / detail** — trim with ellipsis, full text in tooltip.

---

## Testing

**`Paperbunkr.App.Tests`**

- `ActivityService`: job lifecycle (queued→running→succeeded/failed/cancelled); concurrent jobs and
  correct aggregate progress; indeterminate + determinate mix → count form; `PauseAll` cancels
  tokens and queues new jobs; `ResumeAll` releases; cancel-after-terminal is a no-op; completion
  toast raised only when panel closed.
- Alert dedupe by `DedupeKey`; dismiss; "dismiss all".
- `ActivityLink` resolution dispatches to the right shell command per kind.
- `ActivityCenterViewModel`: section projections; history filter predicates (type / age /
  failures-only); `ClearFinished` leaves alerts + upkeep intact.
- Caller migration regression: Generate Covers / Sync Metadata still report progress and a
  completion summary through the new path.

**`Paperbunkr.Data.Tests`**

- `ActivityRun` round-trips; enum columns store as int with no sentinel drift; `StartedUtc` index
  used by the paged history query; startup prune keeps the newer of "200 rows" / "< 30 days";
  startup rewrite of stale `Running` → `Interrupted`.

**Headless smoke** (`avalonia-testing`) — status bar renders and hides in fullscreen; peek opens
from the indicator; drawer slides and traps focus; a fake job drives a visible progress bar.

Run `avalonia-pro-max/review-checklist` before calling the UI done (per CLAUDE.md).

---

## Open questions for implementation review

1. Idle indicator — hidden entirely, or a faint dot so the affordance is discoverable?
2. Does the drawer get a scrim, or stay fully non-blocking?
3. Left status-bar region in v1 — just library total, or also live selection count from the active
   screen (needs each screen VM to feed `StatusBarViewModel`)?
4. Is `Scrape` (ComicVine, bulk) close enough to land in v1's caller migration, or does it wait for
   the bulk-scraper feature that motivated this?
