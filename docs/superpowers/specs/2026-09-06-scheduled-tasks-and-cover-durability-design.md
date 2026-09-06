# Scheduled Tasks + Cover-Cache Durability — Design

Two coupled pieces in one spec:

1. **Scheduled Tasks** — a code-defined catalog of recurring **maintenance** operations the app runs
   automatically on a per-task schedule, replacing today's scattered "run if due" one-offs and the
   operations the user still triggers by hand. New **Automation** Preferences tab.
2. **Cover-cache durability** — a root fix so routine file-path changes (metadata write-back, file
   moves, the `~RF*.TMP` watch bug, drive-letter changes) stop destroying generated cover
   thumbnails. Coupled because the scheduled cover tasks would otherwise run the destructive GC
   automatically and more often.

Date: 2026-09-06. Status: design approved via grilling (2026-09-06, rounds 1–4 for the scheduler,
rounds 1–4 for cover durability). Proceeding to `writing-plans` + implementation per the user's
instruction.

---

## CE-parity check (standing rule)

**Scheduler:** ComicRack CE has none. `TasksDialog` is a live, modal view of in-memory server
queues — no persisted history, no recurring-job concept. CE's only recurring behaviours are
`Settings.ScanStartup` (a one-shot scan at launch) and its reactive folder-watch. Paperbunkr
already carries two ad-hoc "if due" triggers CE never had (`BackupService.RunAutoBackupIfDue`,
`LibraryFolderScanner.RunContentTypeSweepIfDue`). This design generalises that existing pattern
into one scheduler with a visible control surface — a **deliberate deviation**, consistent with the
Activity Center already being a deliberate expansion of `TasksDialog`. Maintenance-only: no
acquisition, RSS, indexers, or auto-download.

**Cover cache:** CE cached page thumbnails in `ThumbnailDiskCache` keyed by a content/updated-count
key, not by file path, and never GC'd them against the library on a schedule. Paperbunkr's
path-fingerprint scheme (2026-08-27) was a Paperbunkr-specific answer to `Issue.Id` reuse after a
rebuild; it over-reached. This design keeps the *goal* (never show issue #900's cover for a reused
id #5) but moves the defense to a single explicit "library was rebuilt" signal, closer to CE's
"the cache is keyed to identity, not location" spirit.

---

# Part 1 — Scheduled Tasks

## Goals

- One place to see and control every recurring maintenance operation: what it is, when it last ran,
  when it runs next, whether it's on, how often.
- The operations the user runs manually today (folder scans, generate/verify covers, sync
  metadata) can run on their own on a cadence.
- Fold the two existing `*IfDue()` triggers into the same mechanism.
- Runs are visible in the Activity Center (live + history + "up next") without drowning it in toast
  noise.
- Nothing stacks up: a task due 8× while the app was closed runs **once** on next launch.

## Non-goals (v1)

- Acquisition of any kind (RSS, indexers, download, post-processing).
- **Bulk tracker refresh** (AniList / MangaBaka / ComicVine) — deferred: no "refresh all tracked
  series" operation exists (tracker sync is per-series in `DetailTabsViewModel`). A follow-up builds
  the bulk op, then adds it as an 8th catalog task.
- Duplicate-file sweep / DB compaction as scheduled tasks.
- User-defined custom tasks / arbitrary command scheduling — the catalog is fixed in code.
- Cron expressions. Per-task schedule is **Interval** or **Daily-at-time** only.
- Retry policies beyond "try again at the next normal due time".
- Deferring tasks based on reader state, battery, CPU load, network metering.
- Remote/server jobs.

## The task catalog (v1)

Seven tasks defined in code (`Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs`).

```csharp
sealed record ScheduledTaskDescriptor(
    string Id,                       // stable key, e.g. "library-scan"
    string DisplayName,
    string Description,              // one line, shown in the Automation tab
    ActivityJobKind ActivityKind,
    int Priority,                    // queue order when several are due at once (lower = first)
    SchedulerResourceClass Resource, // concurrency gate
    TimeSpan DefaultInterval,
    bool DefaultEnabled,
    ScheduleMode DefaultMode,
    Func<IActivityJobHandle, CancellationToken, Task<string>> RunAsync); // returns result summary
```

| Id | Display | Underlying op | Priority | Resource | Default | Default schedule |
|---|---|---|---|---|---|---|
| `db-backup` | Back up database | `BackupService` backup path | 1 | `Db` | **On** | Interval, `AppSettings.AutoBackupMinIntervalHours` (4h) |
| `library-scan` | Scan comic library folders | `LibraryFolderScanner.ScanAllAsync` | 2 | `Db` | **Off**¹ | Interval 6h |
| `book-scan` | Scan book folders | `BookFolderScanService.ScanAllAsync` | 3 | `Db` | **Off**¹ | Interval 6h |
| `sync-metadata` | Re-read embedded metadata | `LibraryScanner.SyncMetadataAsync` | 4 | `Db` | **Off** | Interval 7d |
| `content-type-sweep` | Classify unknown series | `LibraryFolderScanner` sweep body | 5 | `Db` | **On** | Interval 7d (`LastContentTypeSweepUtc`) |
| `verify-covers` | Verify cover thumbnails | mtime-smart re-decode (Part 2) | 6 | `DiskCpu` | **On** | Interval 14d (`LastCoverVerificationUtc`) |
| `generate-covers` | Generate missing covers | `CoverThumbnailService.GenerateAllAsync` | 7 | `DiskCpu` | **Off** | Interval 7d |

¹ `library-scan` / `book-scan` seed **On** if `AppSettings.ScanFoldersOnStartup` was `true` at
migration time.

Bodies are extracted from their current `PreferencesScreenViewModel` commands
(`GenerateCovers` at `PreferencesScreenViewModel.cs:1429`, `VerifyCovers` at `:1478`, the sweep body
inside `RunContentTypeSweepIfDue`) into plain service methods that both the descriptor and the
existing manual command call. **This extraction is the highest-risk part of Part 1** — it refactors
tested, working code; the plan sequences it with the existing command tests as the regression net.

### Resource classes and concurrency

```csharp
enum SchedulerResourceClass { Db, DiskCpu, Network }
```

Runner executes **up to 2 tasks concurrently**, **never two of the same resource class**. With the
v1 catalog: at most one `Db` task at a time (all scans, sweep, metadata sync, backup — all SQLite
writers), alongside at most one `DiskCpu` task (cover work). `Network` reserved for the future
tracker task. No user-facing knob — structural.

## Scheduling model

**Per-task schedule (user-editable, all 7):**
- **Interval** — runs when `now − LastRunUtc ≥ IntervalHours`. `LastRunUtc == null` ⇒ due now.
- **Daily-at-time** — `DailyAtMinutes` past local midnight. Runs at most once per **local calendar
  day**, at the first check point on/after that wall-clock time where it hasn't run yet today.

Either mode selectable for any task; the table is only the default.

**Check points:**
1. **Startup pass** — once, shortly after the main window exists (replacing the `App.axaml.cs`
   `RunAutoBackupIfDue` / `RunContentTypeSweepIfDue` / `ScanFoldersOnStartup` fire-and-forgets).
2. **In-session tick** — `PeriodicTimer` every **15 min** (fixed).
3. **Shutdown** — `desktop.Exit` cancels the timer + any in-flight token. The existing
   unconditional backup-on-exit call is **kept as-is** (belt-and-suspenders, 4h floor).

**Catch-up:** level-triggered — "due" is a function of `LastRunUtc` / "ran today", never a count of
missed windows. Closed a week ⇒ each due task runs **once**.

**Skip conditions (no `LastRunUtc` stamp):**
- A job of the same `ActivityJobKind` already active (manual or scheduled) ⇒ skip, retry next tick.
- Task already queued/running from an earlier check point ⇒ not re-enqueued.

**Manual-run integration:** any completed run of a task's underlying operation stamps `LastRunUtc`.
The 5 existing manual commands (`ScanLibraryFolders`, `ScanBookFolders`, `GenerateCovers`,
`VerifyCovers`, `SyncMetadata`) call `_scheduler.NotifyRan(taskId, status)` in their terminal path.
Only full-scope runs count.

## Failure handling

Task body throws ⇒ `Failed` run in history (trigger `Scheduled`); one deduped Activity **alert**
per task (`DedupeKey = $"sched:{taskId}"`, severity `Warning`, "Run now" action); `LastRunUtc` **is
stamped** (status `Failed`) so it backs off to the normal interval. No automatic retry beyond the
next due time.

## Notifications

New `AppSettings.ScheduledTaskNotificationLevel` (enum int): **`EveryRun` / `OnlyFailures` /
`Never`**, default **`OnlyFailures`**. Governs only the transient completion toast. Every scheduled
run always shows in the Activity peek's "Finished" list and in History. `IActivityJobHandle` gains
a `Quiet` flag (set for `Scheduled` jobs unless level is `EveryRun`; failures always toast unless
`Never`).

## Persistence

### New entity — `Paperbunkr.Data/Entities/ScheduledTaskState.cs`

```csharp
public class ScheduledTaskState
{
    public string TaskId { get; set; } = "";      // PK — catalog Id
    public bool Enabled { get; set; }
    public ScheduleMode Mode { get; set; }         // 0 = Interval, 1 = DailyAt
    public int IntervalHours { get; set; }
    public int DailyAtMinutes { get; set; }        // 0..1439, local
    public DateTime? LastRunUtc { get; set; }
    public ScheduledRunStatus? LastRunStatus { get; set; } // Succeeded/Failed/Skipped
    public int? LastRunActivityId { get; set; }    // -> ActivityRun.Id (int) for the History deep-link
}

public enum ScheduleMode { Interval, DailyAt }
public enum ScheduledRunStatus { Succeeded, Failed, Skipped }
```

Enums stored as their **string name** via `HasConversion<string>().HasMaxLength(16)` — this
context's convention (see `ActivityRun`, `Series.Status`). `Mode` gets
`.HasDefaultValue(ScheduleMode.Interval)`. New `DbSet` on `PaperbunkrDbContext`, no index (≤ 8
rows). `LastRunStatus` is nullable (no default).

### Mirrored columns — kept, not migrated

`db-backup`, `content-type-sweep`, `verify-covers` keep reading/writing their **existing** columns
(`AutoBackupEnabled`/`AutoBackupMinIntervalHours`, `LastContentTypeSweepUtc`,
`LastCoverVerificationUtc`). For these, the descriptor + due-check read the existing column as
source of truth; the `ScheduledTaskState` row **mirrors** it (writes go to both) so the Automation
tab has a uniform data shape. The four new tasks live entirely in `ScheduledTaskState`.

### Seeding

On first scheduler construction, upsert a row for every catalog `Id` missing one, using descriptor
defaults. Future catalog additions self-seed — no migration per task.

### Migration — `AddScheduledTaskState` (the only migration in this spec)

Appended after the current branch HEAD `20260905064447_AddIssueDuplicateAcknowledged` — **rebase
onto whatever is actually HEAD when implementation starts** (`AddReadingEventLog` merged to master
via PR #57 but isn't on this branch yet). Worktree shares `%APPDATA%\Paperbunkr\paperbunkr.db`.

1. `CreateTable ScheduledTaskState`.
2. `AddColumn AppSettings.ScheduledTaskNotificationLevel` (TEXT, default `"OnlyFailures"`),
   configured `HasConversion<string>().HasMaxLength(16).HasDefaultValue(ScheduledTaskNotificationLevel.OnlyFailures)`
   with the same `HasSentinel` handling the other AppSettings enum columns use
   (`PaperbunkrDbContext.cs:834+`).
3. `DropColumn AppSettings.ScanFoldersOnStartup` — `Down()` is a **no-op** for this drop (re-adding
   an orphan column violates the rollback-chain rule, `project_paperbunkr_migration_rollback_orphan_column_bug`);
   `Down()` drops the table + the notification column only.

Seeding reads `ScanFoldersOnStartup` **before** it's dropped — order the migration so the data
seed (or a one-time `__seedScanFromLegacy` marker) captures it; simplest is: the migration writes
`library-scan` / `book-scan` rows with `Enabled` = the old flag value directly in `Up()`, then
drops the column.

`ScanFoldersOnStartup`'s checkbox + VM property (`PreferencesScreenViewModel.cs:571/791`,
`GeneralSection`) and the `App.axaml.cs:145-190` startup-scan block are removed.

## Architecture

No DI container — hand-composed in `MainViewModel`'s ctor, like `IActivityService`.

### `SchedulerService` — `Paperbunkr.App/Services/Scheduling/`

```csharp
public interface ISchedulerService
{
    IReadOnlyList<ScheduledTaskRow> Tasks { get; }   // catalog ⋈ state, UI-thread affine
    event EventHandler? Changed;

    void Start();                                     // startup pass + begin the 15-min timer
    void Stop();                                      // cancel timer + in-flight tokens
    Task RunNowAsync(string taskId);                  // ignores Enabled + schedule; still queued
    void NotifyRan(string taskId, ScheduledRunStatus status);
    void SetEnabled(string taskId, bool enabled);
    void SetSchedule(string taskId, ScheduleMode mode, int intervalHours, int dailyAtMinutes);
}
```

- Ctor takes `IActivityService`, `Func<PaperbunkrDbContext>`, the catalog, and injectable
  `Func<DateTimeOffset> now` + a task-scheduling seam so due-logic + the queue are unit-testable
  without Avalonia or a real timer (same style as `ActivityService(dispatch, recordRun)`).
- **Due evaluation** is a pure function `SchedulerDueLogic.Evaluate(descriptor, state, now) →
  Run | Skip(reason) | NotDue`. Most unit tests target this.
- **Queue**: priority-ordered due list; a pump loop starts tasks while `runningCount < 2` and no
  running task shares the next task's resource class. Non-startable tasks show as
  `ActivityJobStatus.Queued` — first real use of that reserved state, so `ActivityService` gains
  `StartJob(..., startQueued: true)` + `handle.Begin()` to create-then-promote.
- **Per run**: `activity.StartJob(kind, name, trigger: Scheduled)`; run body with
  `handle.CancellationToken`; `Succeed` / `Fail` + alert; then write
  `LastRunUtc`/`LastRunStatus`/`LastRunActivityId` (+ mirror legacy column).
- Only touches the UI thread via `IActivityService`. Its own `Changed` fires through the dispatch
  seam for the Automation tab + the drawer's read-only view.

### `ScheduledTaskRow` — `Paperbunkr.App/Models/`

`ObservableObject` projection of `descriptor ⋈ state`: `DisplayName`, `Description`, `Enabled`,
`Mode`, `IntervalHours`, `DailyAtTime` (`TimeSpan`), `LastRunUtc`, `LastRunRelative`, `NextRunUtc`
/ `NextRunLabel` ("on next launch" when null + interval), `LastRunStatus`, `IsRunning` / `IsQueued`
(observed from `IActivityService`), `LastRunActivityId`. `RunNowCommand`.

### ViewModels

- `AutomationSectionViewModel` (`ViewModels/Preferences/`) — `Tasks`
  (`ObservableCollection<ScheduledTaskRow>`), `NotificationLevel`, subscribes to
  `ISchedulerService.Changed`. Wired into `PreferencesScreenViewModel` alongside the other sections
  + `GoAutomationCommand` / `IsAutomationSection`.
- `ActivityCenterViewModel` — new read-only `UpcomingTasks` projection (name + `NextRunLabel`,
  enabled only, sorted by next run).

### Views

- `Preferences/AutomationSection.axaml` (+ `.axaml.cs` same step — AVLN2000 gotcha). Header row
  with the "Notify when a scheduled task finishes" `ComboBox`, then an `ItemsControl` over `Tasks`
  (not a `DataGrid` — matches the rest of Preferences):

  ```
  {Enabled ToggleSwitch}  {DisplayName}                              {Run now}
                          {Description — faint}
                          Mode: [Interval ▾]  every [ 6 ] hours   |   at [ 04:00 ]
                          Last run: 2 days ago (Done) →History     Next: in 4 hours
  ```

  Mode combo toggles interval-spinner ↔ time-picker visibility. Numeric spinner + combo already
  exist (metadata-editor-affordances, `cc8b276`).
- `PreferencesScreen.axaml` — new `prefNavItem` **"Automation"** between "Library" and "Reader";
  `<pref:AutomationSection IsVisible="{Binding IsAutomationSection}" />`.
- `ActivityDrawerView.axaml:99` — the `IsEnabled="False"` "Scheduled" tab becomes an enabled
  read-only pane: "Up next" list + "Manage in Preferences →" (runs the go-to-preferences nav
  targeting Automation). No config controls here.
- Styling: semantic `DynamicResource` tokens only, no hex. Last-run status pill reuses the
  Activity job-status chip palette.

### `App.axaml.cs` changes

- Delete the `scanFoldersOnStartup` block (`:145-190`) and the two `RunAutoBackupIfDue` /
  `RunContentTypeSweepIfDue` startup `Task.Run`s (`:89-97`).
- After `pluginHost.Initialize(...)`, call `mainViewModel.Scheduler.Start()`.
- Keep `desktop.Exit += … RunAutoBackupIfDue()`; add `mainViewModel.Scheduler.Stop()` to exit.

## History retention change

`ActivityHistoryStore.PruneOnStartup` currently keeps `max(200 rows, < 30 days)`. Change: the
"Nth-newest timestamp" floor (`ActivityHistoryStore.cs:92`) counts only rows that are **not**
(`Trigger == Scheduled && Status == Succeeded`). The final age-based delete is unchanged, so
scheduled successes older than 30 days are always deletable regardless of row count; manual runs
and failures keep the existing rule.

---

# Part 2 — Cover-Cache Durability (no schema changes)

## Problem

Cover thumbnails cache at `%AppData%\Paperbunkr\thumbnails\{issueId}-{hash}.jpg`, where `{hash}` is
`FNV1a(normalized full path)` (`Issue.FileSize` is a dead column — nothing populates it, so the
fingerprint is path-only in practice). `CollectOrphans` **hard-deletes** any cache file whose stem
doesn't match a current issue's computed stem
([CoverThumbnailService.cs:381](../../src/Paperbunkr.App/Services/CoverThumbnailService.cs)). That
GC runs every launch (`ReconcileCoverCachesAsync` → `GenerateAllAsync`) and every 7 days silently
(`PeriodicCoverVerificationAsync` → `VerifyAllAsync`).

So **any change to an issue's stored path orphans its cover**, and the next reconcile deletes it —
metadata write-back (`File.Replace` + the `~RF*.TMP` watch bug, fixed in `52b20ce`/`4911682` but
covers already lost), file moves/reorg, drive-letter changes, path-normalization drift. If the
source file isn't readable at reconcile time (slow NAS, locked, genuinely offline) regeneration
fails and the cover stays blank. User-visible symptom: "closed the app, most covers gone,
regenerate them all."

Books have the identical bug (`BookCoverThumbnailService`, also path-only). Arc covers
(`ArcCoverImageCache`) are already keyed by id with no GC — the model to follow, not a victim.

## Root fix

**Cover files become `{id}.jpg`.** Identity defense (id-reuse after a rebuild) moves from
per-filename hashing + constant GC to one explicit "library was rebuilt" signal. **All new state
is on the filesystem — zero database changes.**

```
%AppData%\Paperbunkr\
  thumbnails\{issueId}.jpg                 decoded comic covers
  thumbnails\.attic\{issueId}.{ticks}.jpg  soft-deleted; 14-day + 500 MB cap
  book-thumbnails\{bookId}.jpg             decoded book covers
  book-thumbnails\.attic\...
  custom-covers\{issueId}.jpg              hand-picked comic covers — never swept
  custom-book-covers\{bookId}.jpg          hand-picked book covers — never swept
  cover-cache-state.json                   { schemaVersion, generation, issueCount, bookCount }
```

### Custom covers → own directory (replaces the `HasCustomCover` column idea)

The grilling outcome ("custom covers survive a rebuild") is achieved without a schema change by
giving them their own directories, exactly as `ArcCoverPaths` already does for arc covers:

- `TrySetCustomCover(id, img)` writes `custom-covers/{id}.jpg` (comics) / `custom-book-covers/{id}.jpg`.
- `ResetCover(id)` deletes that file (and any `thumbnails/{id}.jpg`, then regenerates the decoded one).
- **Serving** checks `custom-covers/{id}.jpg` first, then `thumbnails/{id}.jpg`.
- The attic sweep and the rebuild purge **never touch** `custom-covers/` or `custom-book-covers/`.
- Trade-off (accepted): after a rebuild that reassigns ids, a custom cover can display on a reused
  id — rare, and fixed by re-picking it. Same trade-off arc covers already carry.

### `CoverFingerprint` collapses to a shim

`Stem(id, path, size)` returns `id.ToString(CultureInfo.InvariantCulture)`. Kept with its current
signature so the ~10 call sites (`LibraryTile`, `IssueListRow`, `SeriesCardSample`,
`SmartScreenViewModel`, `EventMemberRowViewModel`, `ReadingListItemRowViewModel`,
`PaperbunkrApplication`, the `CoverImageCache.Get` overloads, …) compile untouched. `path`/`size`
become unused params — a follow-up sweep removes them. `TryGetId(stem)` → `int.TryParse`.

### `CoverImageCache` / `BookCoverImageCache`

- Keyed by the id string; `Get(int id, string? path, long? size)` ignores path/size.
- New `Clear()` — used by the rebuild purge.
- `Invalidate(id)` matches the exact `{id}.jpg` (+ custom), not a `{id}-` prefix.
- Serving path: `custom` dir → `thumbnails` dir → decode-miss (unchanged miss semantics: misses
  not cached, re-checked next lookup).

### Orphan GC — never deletes for a mismatch

`CollectOrphans` (both services) rewritten:

- A `{id}.jpg` in `thumbnails/` whose id matches **no** `Issue` row → **moved to `.attic/`**
  (`File.Move` to `{id}.{DateTime.UtcNow.Ticks}.jpg`), not deleted.
- Nothing else is ever swept. A live issue whose path changed keeps its cover, untouched.
- `SweepStaleSiblings` is **deleted** — under `{id}.jpg` there is exactly one file per id.
- Runs on the every-launch reconcile (now: one `SELECT Id` + a directory diff) + attic pruning.

### Missing-source guard

`GenerateAllAsync` / `VerifyAllAsync` never attic a cover whose issue still exists — only genuinely
id-less files. A dismounted drive costs nothing.

### Library-rebuild purge — `CoverCacheState.OnLibraryRebuilt()`

- Moves everything in `thumbnails/` and `book-thumbnails/` (not `custom-*`, not `.attic/`) into the
  respective `.attic/`.
- Writes a fresh `generation` GUID to `cover-cache-state.json`.
- Calls `CoverImageCache.Clear()` + `BookCoverImageCache.Clear()`.

Wired into the three id-reassigning paths:
- `CeLibraryMigrator` (CE re-migration) — at the end of a successful migration.
- The reset / "start fresh" flow (`App` fresh-install path / any "reset library" command).
- DB-restore-from-backup — `App.HandleDatabaseRecovery` `Restore` branch writes
  `cover-cache-state.json`'s `rebuildPending: true` before relaunching; the relaunched process's
  startup reconcile sees the flag, runs `OnLibraryRebuilt()`, clears it. (The restore branch can't
  safely attic during shutdown/relaunch — the flag defers it to a clean startup.)

### Heuristic safety net (for an unwired 4th path)

The every-launch reconcile reads `cover-cache-state.json`'s stored `issueCount` / `bookCount`; if
the **current** count has dropped below **50 %** of stored, treat it as an unannounced rebuild:
attic + `CoverImageCache.Clear()` + an Activity alert ("Library changed a lot — cover cache
rebuilt"). Cost: one `COUNT(*)` per entity. Stored counts are refreshed on every successful
`GenerateAllAsync`. First run (no stored counts) just records them, never purges.

### `verify-covers` goes mtime-smart

Replaces the silent 7-day force-re-decode-everything. `VerifyAllAsync` re-decodes a cover only when
its source file's `LastWriteTimeUtc` is **newer** than the cached `{id}.jpg`'s. `LastCoverVerificationUtc`
already exists (no migration); the scheduled task's `LastRunUtc` mirrors it. `PeriodicCoverVerificationAsync`
+ its call site in `MainViewModel` are deleted — the scheduler owns this now, and it's no longer
silent (respects `ScheduledTaskNotificationLevel`).

A manual **"Rebuild All Covers"** (force re-decode, ignore mtime) — the existing Preferences action
at `PreferencesScreenViewModel.cs:1547` — is retained for suspected corruption.

### One-time on-disk cache migration — `CoverCacheUpgrade.RunOnce()`

No DB. First run after upgrade (gated by any `*-*.jpg` present in a thumbnails dir, or
`cover-cache-state.json` absent / `schemaVersion < 2`):

- Rename `{id}-{hash}.jpg` → `{id}.jpg` in `thumbnails/` and `book-thumbnails/`.
- Multiple hashes for one id → keep newest `LastWriteTimeUtc`, attic the rest.
- Pre-2026-08-27 bare `{id}.jpg` already correct — leave.
- Write `cover-cache-state.json` `{ schemaVersion: 2, generation: <new guid>, issueCount, bookCount }`.
- Idempotent — a second run finds no `*-*.jpg` and no-ops.
- **Known gap:** custom covers set *before* this ships weren't tracked, so they can't be moved into
  `custom-covers/` automatically — they'll be treated as ordinary decoded covers (regenerable, and
  swept on a later rebuild). Only affects custom covers set before the update. Documented, no clean
  workaround.

### "Repair Missing Covers" — new manual action

Preferences → Libraries (near "Generate Covers"). Scans for issues/books with no `{id}.jpg` and no
custom cover and a **readable** source file; regenerates just those; watchable, cancellable,
resumable (presence-based). Manual only (no auto-alert). Distinct from "Rebuild All Covers" (force,
everything) and "Generate Covers" (which currently also runs the destructive GC — that GC becomes
the non-destructive attic sweep).

## Files touched — Part 2

**New**
- `Paperbunkr.App/Services/Covers/CoverCacheState.cs` — reads/writes `cover-cache-state.json`,
  `OnLibraryRebuilt()`, `NeedsHeuristicPurge(issueCount, bookCount)`, count refresh.
- `Paperbunkr.App/Services/Covers/CoverCacheAttic.cs` — move-to-attic, prune (14d + 500 MB),
  restore-by-id.
- `Paperbunkr.App/Services/Covers/CoverCacheUpgrade.cs` — the one-time on-disk migration.
- `CustomCoverPaths.cs` / `CustomBookCoverPaths.cs` (or fold into the existing `*Paths` classes).
- Test files.

**Modified**
- `CoverFingerprint.cs` → shim.
- `CoverThumbnailPaths.cs` / `BookCoverThumbnailPaths.cs` — `{id}.jpg`; drop `EnumerateForIssue`
  glob to exact; add attic dir helpers.
- `CoverThumbnailService.cs` / `BookCoverThumbnailService.cs` — `CollectOrphans` → attic;
  `VerifyAllAsync` mtime-smart; remove `SweepStaleSiblings`; `TrySetCustomCover` / `ResetCover` →
  custom dir.
- `CoverImageCache.cs` / `BookCoverImageCache.cs` — id key, `Clear()`, exact `Invalidate`.
- `MainViewModel.cs` — `ReconcileCoverCachesAsync` calls the upgrade once, then the attic sweep +
  heuristic check; delete `PeriodicCoverVerificationAsync`.
- `CeLibraryMigrator.cs`, the reset flow, `App.HandleDatabaseRecovery` — call `OnLibraryRebuilt()`.
- `PreferencesScreenViewModel.cs` / `LibrarySection.axaml` — "Repair Missing Covers" command +
  button; `GenerateCovers` / `VerifyCovers` bodies extracted (shared with Part 1).
- `AsyncCoverImage.cs` — unchanged if it goes through `CoverImageCache` (verify).

---

## Error handling summary

| Situation | Behaviour |
|---|---|
| Scheduled task throws | `Failed` run · deduped `Warning` alert w/ Run-now · `LastRunUtc` stamped · normal-interval backoff |
| Same-kind job already running | Skip cycle · no stamp · retry next tick |
| App quits mid scheduled task | Token cancelled on `Stop()`; job settles `Cancelled`; not persisted as stale `Running` |
| `ScheduledTaskState` write fails | Swallowed (best-effort, like `ActivityHistoryStore.Record`) |
| Issue's file path changes | Cover **kept** — served by id, nothing sweeps it |
| Issue's file offline at reconcile | Cover **kept** — missing-source guard |
| Issue row deleted | `{id}.jpg` moved to `.attic/`, recoverable 14 days |
| Library rebuilt (wired path) | `OnLibraryRebuilt()` — full attic + fresh generation + memory `Clear()`; custom covers survive |
| Library rebuilt (unwired path) | Heuristic (count < 50 % of stored) — same purge + an alert |
| Custom cover + rebuild | Survives (own directory, never swept) |
| `cover-cache-state.json` missing/corrupt | Treated as first run — record counts, no purge; upgrade re-runs harmlessly |
| Two app instances share the cache | `File.Move` / `File.Delete` swallow `IOException` (existing convention) |
| DST / clock change | Daily-at uses local "ran today" guard; interval uses UTC deltas |

---

## Testing

### Part 1 — `Paperbunkr.Data.Tests`
- `ScheduledTaskState` round-trips; `Mode` / `LastRunStatus` store as string name, default
  `Interval`, nullable status round-trips as null.
- `AddScheduledTaskState`: table + notification column created; `ScanFoldersOnStartup` dropped;
  `Down()` drops table + column, no-op for the `ScanFoldersOnStartup` re-add.
- Seed: `ScanFoldersOnStartup == true` ⇒ `library-scan`/`book-scan` rows `Enabled`.
- Retention: mix of manual + scheduled-success + scheduled-failure > 200; oldest scheduled
  successes pruned by age while manual rows past #200 survive; failures survive.

### Part 1 — `Paperbunkr.App.Tests` (injected clock + fake `IActivityService`)
- `SchedulerDueLogic`: interval due/not-due/never-run; daily-at once per local day on/after time;
  catch-up = one run after a multi-window gap; disabled ⇒ never due.
- Queue: priority order; resource-class rule (two `Db` serialise, `Db` + `DiskCpu` overlap, max 2);
  `Queued` emitted then promoted.
- Same-kind-active ⇒ skip, no stamp.
- Failure ⇒ `Failed` recorded, one deduped alert, `LastRunUtc` stamped, next due at normal interval.
- `NotifyRan` from a manual command moves next due out.
- `RunNowAsync` runs a disabled / not-due task, respects the queue + resource rule, records
  `Manual`, stamps on completion.
- Notification level: `OnlyFailures` suppresses success toasts but records history; `Never`
  suppresses all; `EveryRun` toasts all (subject to `PanelIsOpen`).
- `db-backup` reads `AutoBackupEnabled`/`AutoBackupMinIntervalHours`; toggling the row writes both.
- `AutomationSectionViewModel`: row projection, `NextRunLabel`, edits persist.
- `ActivityCenterViewModel.UpcomingTasks`: enabled only, sorted by next run, excludes running.

### Part 2 — `Paperbunkr.App.Tests`
- `CoverFingerprint.Stem(id, …)` == `id.ToString()`; `CoverImageCache.Get` resolves by id;
  `Invalidate(id)` drops exactly `{id}.jpg`, not prefix.
- `CollectOrphans` (comics + books): id-less `{id}.jpg` → `.attic/`; a live issue with a **changed
  path** keeps its cover; a `custom-covers/{id}.jpg` is never touched.
- Missing-source guard: unreadable source ⇒ cover kept, not atticked.
- `CoverCacheState.OnLibraryRebuilt`: `thumbnails` + `book-thumbnails` moved to attic; `custom-*`
  untouched; fresh generation; `Clear()` invoked.
- Heuristic: current count < 50 % of stored ⇒ purge + alert; ≥ 50 % ⇒ no-op; no stored counts ⇒
  record only.
- `CoverCacheAttic`: 14-day + 500 MB prune (oldest-out); restore-by-id reattaches before a
  re-decode is attempted.
- `verify-covers` mtime-smart: re-decodes only covers whose source mtime > cached `{id}.jpg` mtime.
- `CoverCacheUpgrade.RunOnce`: `{id}-{hash}.jpg` → `{id}.jpg`; multiple hashes ⇒ newest kept, rest
  atticked; idempotent second run.
- `TrySetCustomCover` writes `custom-covers/`; `ResetCover` clears it and regenerates the decoded
  one; serving prefers custom.
- "Repair Missing Covers": regenerates only blank-cover ids with a readable source; cancellable;
  resumable.

### Headless smoke (`avalonia-testing`)
- Automation tab renders 7 rows; toggle + interval edit persist and survive a VM rebuild; mode
  combo swaps interval spinner ↔ time picker; "Run now" starts a visible Activity job; drawer
  "Scheduled" tab shows the up-next list and the Preferences link navigates.
- A card whose issue path changes mid-session still shows its cover; after a simulated epoch bump
  the grid regenerates rather than showing wrong art.
- `avalonia-pro-max/review-checklist` before calling UI done (per CLAUDE.md).

> **Suite note** (`project_paperbunkr_full_suite_headless_flake`): run App.Tests via targeted
> `--filter` subsets, not the whole suite at once.

---

## Roadmap docs

On completion, update `docs/alpha-todo.md` / `docs/Paperbunkr-Roadmap.md`: the scheduler as a
shipped deliberate deviation; the cover-durability root fix as closing the "covers wiped on
close" defect and superseding the 2026-08-27 path-fingerprint identity scheme + the 2026-08-30
periodic content-verification pass.

## Open questions

None — both design trees fully walked (grilling, 2026-09-06).
