# Scheduled Tasks + Cover-Cache Durability — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md*

## Status — implemented 2026-09-06

All phases done. App + Data build clean; `dotnet build Paperbunkr.sln` → 0 errors.

- **Phase A (cover durability):** shipped. `CoverFingerprint` → `{id}` shim; `CoverThumbnailPaths` /
  `BookCoverThumbnailPaths` `{id}.jpg` + attic dirs; `CoverCacheState` / `CoverCacheAttic` /
  `CoverCacheUpgrade` / `CoverCacheMaintenance` (new, `Services/Covers/`); `CustomCoverPaths` /
  `CustomBookCoverPaths`; both thumbnail services reworked (attic not delete, missing-source guard,
  mtime-smart verify, `RepairMissingAsync`, custom-cover dir, best-effort `RunCacheMaintenance`
  wrapper); `CoverImageCache` / `BookCoverImageCache` id-keyed + `Clear()`; `LruCache.Clear()`;
  `MainViewModel.ReconcileCoverCachesAsync` rewrite (upgrade + rebuild-pending + heuristic +
  generate), `PeriodicCoverVerificationAsync` deleted; rebuild hooks in `MigrationViewModel.Commit`
  + `App.HandleDatabaseRecovery`; "Repair Missing Covers" command + button in `LibrarySection`.
  Tests: `CoverFingerprintTests`, `CoverThumbnailServiceTests`, `BookCoverThumbnailServiceTests`,
  `CoverImageCacheTests` rewritten; new `CoverCacheStateTests`, `CoverCacheAtticTests`,
  `CoverCacheUpgradeTests`; new `CoverCacheTestRedirect` helper + process-wide safety net in
  `AvaloniaTestCollection` (several fixtures were letting the sweep hit the real per-user cache).
- **Phase B (scheduler core):** shipped. `ScheduledTaskState` + `ScheduleMode` /
  `ScheduledRunStatus` / `ScheduledTaskNotificationLevel` enums; `AddScheduledTaskState` migration
  (`ScanFoldersOnStartup` kept as a dormant column, not dropped); `LibraryFolderScanner`
  `RunContentTypeSweepCore`; `Services/Scheduling/` — `SchedulerResourceClass`,
  `ScheduledTaskDescriptor`, `ScheduledTaskCatalog` (7 tasks), `SchedulerDueLogic`,
  `ScheduledRunStore`, `ISchedulerService` + `SchedulerService`; `ActivityService` gained
  `ActivityToastPolicy` + `startQueued`/`Begin()`; `ActivityHistoryStore` retention exempts
  scheduled successes; composed in `MainViewModel`, wired `Start()`/`Stop()` in `App.axaml.cs`
  (old auto-backup / content-sweep startup triggers removed); `NotifyRan` in the 5 manual commands.
  Tests: `SchedulerDueLogicTests`, `SchedulerServiceTests`, `ScheduledRunStoreTests`,
  `AddScheduledTaskStateMigrationTests`.
- **Phase C (scheduler UI):** shipped. `PreferencesSection.Automation`; `AutomationSection.axaml`
  (+ `.axaml.cs`) + nav item in `PreferencesScreen.axaml`; Automation state on
  `PreferencesScreenViewModel` (`ScheduledTasks`, `ScheduledTaskNotificationLevel`,
  `AttachScheduler`); `ScanFoldersOnStartup` checkbox removed from `GeneralSection`;
  `ActivityCenterViewModel` gained `Scheduled` tab + `UpcomingTasks` + `IsActiveTab`;
  `ActivityDrawerView.axaml` Scheduled tab enabled + read-only pane; `PreferenceIndex` entry.
- **Phase D:** `Paperbunkr-Roadmap.md` updated. **GUI pass by the user still pending.** Not committed.

**Pre-existing, not from this work:** 7 `*MigrationTests.*IsReversible*` failures on this branch
(`AddFb2Mobi`, `ReworkBookHighlightAnchor`, `AddLastContentTypeSweepUtc`, `AddBooksBrowseState`,
`AddBookReaderErgonomics`, `AddWorkspaces`, `LibraryDetailsColumns`) — the rollback-chain bug that
commit `03ed4d4` fixes on `master` but which isn't on `claude/metadata-editor-affordances` yet.
`AddScheduledTaskState`'s own tests pass. Full-suite runs also flake under concurrent load
(`project_paperbunkr_full_suite_headless_flake`) — targeted `--filter` subsets are green.


Order: **Phase A first** (cover durability — the active defect, and Phase B's cover tasks depend on
the non-destructive GC). Then B (scheduler core), C (scheduler UI), D (docs + verification).

Enum storage convention confirmed: this codebase stores enums as **string name** via
`HasConversion<string>().HasMaxLength(n)` (not int). Migration HEAD on this branch:
`20260905064447_AddIssueDuplicateAcknowledged` — rebase the new migration onto actual HEAD at start.

---

## Phase A — Cover-cache durability (no schema changes)

### A1: Path helpers + `CoverFingerprint` shim
**Files:** `src/Paperbunkr.App/Services/CoverFingerprint.cs` (edit),
`src/Paperbunkr.App/Services/CoverThumbnailPaths.cs` (edit),
`src/Paperbunkr.App/Services/BookCoverThumbnailPaths.cs` (edit),
`src/Paperbunkr.App/Services/Covers/CustomCoverPaths.cs` (new),
`src/Paperbunkr.App/Services/Covers/CustomBookCoverPaths.cs` (new)
**What:**
- `CoverFingerprint.Stem(id, path, size)` → `id.ToString(CultureInfo.InvariantCulture)`. `TryGetId`
  → `int.TryParse`. Keep the signature (params unused) so the ~10 call sites compile.
- `CoverThumbnailPaths` / `BookCoverThumbnailPaths`: `GetCachePath(stem)` still `{stem}.jpg` (stem
  is now the bare id). Add `AtticDirectory` (`thumbnails/.attic`, `book-thumbnails/.attic`),
  `EnumerateAtticFiles()`. `EnumerateForIssue`/`EnumerateForBook` glob stays `{id}-*.jpg` **only**
  for the A5 upgrade to find legacy files; add `GetExactCachePath(int id)` for the common path.
  `EnumerateAll()` must **exclude** `.attic/` (it's a subdir; `Directory.GetFiles` non-recursive
  already does, but assert).
- `CustomCoverPaths` / `CustomBookCoverPaths`: mirror `ArcCoverPaths` shape — `custom-covers/`,
  `custom-book-covers/` under `%AppData%\Paperbunkr`, `GetCachePath(int id)` → `{id}.jpg`,
  mutable `Directory` for tests.
**Depends on:** none
**Verify:** `CoverFingerprintTests` updated (stem == id string); new `CustomCoverPathsTests`
(round-trips a path). Build.

### A2: `CoverCacheState` + `CoverCacheAttic`
**Files:** `src/Paperbunkr.App/Services/Covers/CoverCacheState.cs` (new),
`src/Paperbunkr.App/Services/Covers/CoverCacheAttic.cs` (new)
**What:**
- `CoverCacheState` — reads/writes `%AppData%\Paperbunkr\cover-cache-state.json`:
  `{ schemaVersion:int, generation:string, issueCount:int, bookCount:int, rebuildPending:bool }`.
  Methods: `Read()`, `Write(state)`, `RefreshCounts(issueCount, bookCount)`,
  `MarkRebuildPending()`, `NewGeneration()`. Corrupt/missing file → default state
  (`schemaVersion:0`, empty generation, zero counts). Static, best-effort (swallow IO), mutable
  path for tests.
- `CoverCacheAttic` — `MoveToAttic(string cacheFilePath, string atticDir)` →
  `File.Move` to `{id}.{DateTime.UtcNow.Ticks}.jpg`; `Prune(atticDir)` — delete entries older than
  14 days, then oldest-first until under 500 MB; `TryRestoreById(int id, string cacheDir, string
  atticDir)` → newest attic file for that id moved back to `{id}.jpg` (returns bool). Swallows
  `IOException`/`UnauthorizedAccessException` (multi-instance convention).
**Depends on:** A1
**Verify:** new `CoverCacheStateTests` (round-trip, corrupt→default, refresh counts),
`CoverCacheAtticTests` (move creates timestamped file; prune by age; prune by size oldest-out;
restore reattaches newest). 

### A3: In-memory caches → id-keyed
**Files:** `src/Paperbunkr.App/Services/CoverImageCache.cs` (edit),
`src/Paperbunkr.App/Services/BookCoverImageCache.cs` (edit)
**What:**
- Key is the id string. `Get(int id, string? path, long? size)` → `Get(id.ToString(...))`.
- `Get(string idKey)` serving order: custom dir file → main cache file → decode-miss (unchanged
  "misses not cached" semantics).
- `Invalidate(int id)` matches exactly `{id}.jpg` in both main + custom dirs and removes the exact
  in-memory key (not `{id}-` prefix).
- New `Clear()` — drop all in-memory entries (for the rebuild purge).
- `InvalidateMemoryOnly(string key)` unchanged in intent (key is now the id string).
**Depends on:** A1
**Verify:** `CoverImageCacheTests` / `BookCoverImageServiceTests` (as applicable) updated: id key;
`Invalidate(5)` doesn't touch id 50; custom file served before main; `Clear()` empties.

### A4: `CoverThumbnailService` / `BookCoverThumbnailService` rework
**Files:** `src/Paperbunkr.App/Services/CoverThumbnailService.cs` (edit),
`src/Paperbunkr.App/Services/BookCoverThumbnailService.cs` (edit)
**What:**
- Cache path per id is `{id}.jpg` (via the shimmed `Stem`). Remove `SweepStaleSiblings` calls +
  method (one file per id now).
- `CollectOrphans(validIds)` → for each `{n}.jpg` in the main dir whose `n` ∉ current id set:
  `CoverCacheAttic.MoveToAttic(...)` instead of `File.Delete`. Never sweep for anything else.
- `GenerateAllAsync` / `VerifyAllAsync`: build `validIds` = `HashSet<int>` of `context.Issues`
  (`.Where(i => i.FilePath != null)` → `.Select(i => i.Id)`). After the generate/verify loop:
  `CollectOrphans(validIds)`, then `CoverCacheAttic.Prune(...)`, then
  `CoverCacheState.RefreshCounts(issueCount, bookCount)` (comic service refreshes `issueCount`,
  book service `bookCount` — read the other count from state so neither clobbers).
- **Missing-source guard:** the loop already skips a file it can't open (returns false); ensure a
  skipped/failed decode never removes an existing `{id}.jpg`. `CollectOrphans` only targets id-less
  files, so a present-but-unreadable source's cover (id still valid) is safe — assert with a test.
- `VerifyAllAsync` **mtime-smart:** for each candidate, only `force`-regenerate when
  `File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(cachePath)` (or cache file absent).
  Keep `TryGenerateThumbnail(..., force:true)` for those; skip the rest.
- `TrySetCustomCover(id, img)` writes `CustomCoverPaths.GetCachePath(id)`; also delete any main
  `{id}.jpg`; `CoverImageCache.InvalidateMemoryOnly`. `ResetCover(id, filePath)` deletes the custom
  file, then regenerates the decoded one at `{id}.jpg`.
- Aspect-ratio persistence (`CoverAspectRatioStore`, `PersistAspectRatios`) unchanged.
- `BackfillAspectRatiosCore`: uses `CoverFingerprint.Stem` → now just `{id}.jpg`, fine.
**Depends on:** A1, A2, A3
**Verify:** `CoverThumbnailServiceTests` / `BookCoverThumbnailServiceTests` rewritten for:
id-less file → attic (not delete); live issue w/ changed path keeps its cover; unreadable source
keeps its cover; `VerifyAllAsync` re-decodes only mtime-newer; custom-cover set/reset uses the
custom dir; `GenerateAllAsync` refreshes state counts.

### A5: `CoverCacheUpgrade.RunOnce()`
**Files:** `src/Paperbunkr.App/Services/Covers/CoverCacheUpgrade.cs` (new)
**What:** one-time, no DB. Gated by: `cover-cache-state.json` missing OR `schemaVersion < 2` OR any
`*-*.jpg` present in a thumbnails dir. For each of `thumbnails/`, `book-thumbnails/`: group
`{id}-{hash}.jpg` files by leading id; per id keep newest `LastWriteTimeUtc` → rename to `{id}.jpg`
(if a bare `{id}.jpg` already exists, keep the newer, attic the other); attic the rest. Leave
pre-existing bare `{id}.jpg`. Then `CoverCacheState.Write` with `schemaVersion:2`, a fresh
`generation`, and current counts (or 0/0 if no DB access here — counts get set on the next
`GenerateAllAsync`). Idempotent: second run finds no `*-*.jpg` and `schemaVersion == 2` → no-op.
**Depends on:** A1, A2
**Verify:** `CoverCacheUpgradeTests` — `{id}-{hash}.jpg`→`{id}.jpg`; two hashes for one id → newest
kept, other atticked; bare `{id}.jpg` untouched; idempotent second run; sets `schemaVersion:2`.

### A6: `MainViewModel` reconcile rewrite
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:**
- `ReconcileCoverCachesAsync`: (1) `CoverCacheUpgrade.RunOnce()`; (2) read `CoverCacheState`, if
  `rebuildPending` → `CoverCacheState`-driven full purge (attic both dirs, `NewGeneration()`,
  `CoverImageCache.Clear()` + book, clear `rebuildPending`); (3) heuristic: query
  `COUNT(Issues)` + `COUNT(Books)`, if state has non-zero stored counts and current < 50 % of
  stored → same purge + `Activity.RaiseAlert` ("Library changed a lot — cover cache rebuilt",
  Info); (4) `await CoverThumbnailService().GenerateAllAsync(...)` + book equivalent (these now do
  the attic sweep + prune + count refresh internally).
- **Delete** `PeriodicCoverVerificationAsync`, `CoverVerificationInterval`,
  `ShouldRunCoverVerification`, and the `_ = PeriodicCoverVerificationAsync();` call. (Its
  `MainViewModelTests` `ShouldRunCoverVerification_*` tests are removed; the behaviour moves to the
  scheduler's `verify-covers` task, tested there.) `LastCoverVerificationUtc` column stays — the
  scheduler task will mirror it.
**Depends on:** A2, A3, A4, A5
**Verify:** `MainViewModelTests` — remove the 4 `ShouldRunCoverVerification` tests; no new tests
here (logic is in the services + a new `CoverReconcileTests` if a seam is extracted). Build.

### A7: Library-rebuild hooks
**Files:** `src/Paperbunkr.Data/CeMigration/CeLibraryMigrator.cs` (edit),
`src/Paperbunkr.App/App.axaml.cs` (edit — `HandleDatabaseRecovery` Restore branch),
plus the reset/"start fresh" path (survey: `grep -rn "start fresh\|StartFresh\|ResetLibrary\|
HasAnySeries" src/Paperbunkr.App` — wire whichever command clears the library).
**What:** `CeLibraryMigrator` can't reference `Paperbunkr.App` — it exposes the fact of a completed
re-migration and the App-side caller (`MigrationViewModel`, `MigrationViewModel.cs:294` area)
invokes a new `CoverCacheState`-based `OnLibraryRebuilt()` helper after migration. `App.HandleDatabaseRecovery`
Restore branch: `CoverCacheState.MarkRebuildPending()` before relaunch. Reset flow: call
`OnLibraryRebuilt()` directly (app is running).
**Depends on:** A2, A3
**Verify:** unit test the `OnLibraryRebuilt()` helper (attics both dirs, bumps generation, leaves
custom dirs); manual note for the CE-migration + restore paths (hard to headless-test end to end).

### A8: "Repair Missing Covers" action
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit),
`src/Paperbunkr.App/Services/CoverThumbnailService.cs` + book service (edit — add
`RepairMissingAsync`)
**What:** `RepairMissingAsync(progress, ct)` on both services: issues/books with no `{id}.jpg`, no
custom cover, and `File.Exists(source)` → `TryGenerateThumbnail(force:false)`. A
`[RelayCommand] RepairMissingCovers` (guard flag `IsRepairingCovers`) starting an
`ActivityJobKind.GenerateCovers` job "Repairing missing covers", both passes, one summary.
Button in `LibrarySection.axaml` next to "Generate Covers" / "Verify Covers".
**Depends on:** A4
**Verify:** `CoverThumbnailServiceTests.RepairMissingAsync_*` (only blank+readable regenerated;
custom-covered id skipped; offline-source id skipped); `PreferencesScreenViewModelTests` command
smoke.

### A9: Part-A regression sweep
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~Cover"` and
`~BookCover`; `dotnet build`. Fix fallout in the ~10 `CoverFingerprint.Stem` call sites if any
relied on the hash (none should — they only use it as an opaque key).

---

## Phase B — Scheduler core

### B1: `ScheduledTaskState` entity + migration
**Files:** `src/Paperbunkr.Data/Entities/ScheduledTaskState.cs` (new — entity + `ScheduleMode`,
`ScheduledRunStatus` enums), `src/Paperbunkr.Data/Entities/ScheduledTaskNotificationLevel.cs`
(new enum), `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit — add
`ScheduledTaskNotificationLevel` prop default `OnlyFailures`, remove `ScanFoldersOnStartup`),
`src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit — `DbSet`, entity config, AppSettings enum
column config mirroring `:834+`), `src/Paperbunkr.Data/Migrations/*_AddScheduledTaskState.cs` (new).
**What:** entity per the design. `PaperbunkrDbContext`: `builder.Entity<ScheduledTaskState>` — PK
`TaskId`, `Mode`/`LastRunStatus` `HasConversion<string>().HasMaxLength(16)`, `Mode` default
`Interval`. `AppSettings.ScheduledTaskNotificationLevel`
`HasConversion<string>().HasMaxLength(16).HasDefaultValue(OnlyFailures)` + `HasSentinel` per
neighbours. Migration `Up()`: create table; add notification column; write `library-scan` /
`book-scan` rows with `Enabled` = old `ScanFoldersOnStartup` value (read via raw SQL before drop);
`DropColumn ScanFoldersOnStartup`. `Down()`: drop table + notification column only (no-op the
`ScanFoldersOnStartup` re-add — rollback-orphan rule).
**Depends on:** none (rebase onto real migration HEAD)
**Verify:** new `AddScheduledTaskStateMigrationTests` (table + column exist post-up; `ScanFoldersOnStartup`
gone; `Down` leaves no orphan); `AppSettingsTests` (default `OnlyFailures`); update
`AddBehaviorSettingsBatch2MigrationTests` / `PreferencesScreenViewModelTests` refs to the removed
`ScanFoldersOnStartup`.

### B2: Extract the content-type-sweep body
**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit)
**What:** pull the work inside `RunContentTypeSweepIfDue()` into
`Task RunContentTypeSweepAsync(IProgress<(int,int)>, CancellationToken)` (or sync `RunContentTypeSweepCore`).
`RunContentTypeSweepIfDue()` keeps its 7-day gate and calls the new method. The scheduler task
calls the new method directly (gate is the scheduler's job). Cover generate/verify bodies are
**already** service methods (`CoverThumbnailService.GenerateAllAsync` / a new mtime-smart
`VerifyAllAsync` from A4) — no extraction needed there.
**Depends on:** none
**Verify:** existing `LibraryFolderScannerTests.RunContentTypeSweep*` still green (they can call the
core directly or via the gate).

### B3: `ScheduledTaskDescriptor` + `ScheduledTaskCatalog`
**Files:** `src/Paperbunkr.App/Services/Scheduling/ScheduledTaskDescriptor.cs` (new),
`src/Paperbunkr.App/Services/Scheduling/SchedulerResourceClass.cs` (new enum),
`src/Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs` (new)
**What:** the record from the design + `SchedulerResourceClass { Db, DiskCpu, Network }`. Catalog =
7 descriptors with `RunAsync` lambdas wrapping the real ops (`new BackupService().BackupNow()`
wrapped; `LibraryFolderScanner().ScanAllAsync`; `BookFolderScanService().ScanAllAsync`;
`SyncMetadataAsync`; `RunContentTypeSweepAsync`; `VerifyAllAsync` mtime-smart both services;
`GenerateAllAsync` both services). `RunAsync` returns a short result summary string. Catalog takes
a `Func<PaperbunkrDbContext>` for the services that need it.
**Depends on:** A4 (mtime-smart verify), B2
**Verify:** `ScheduledTaskCatalogTests` — 7 entries, unique ids, priorities distinct, every
`RunAsync` non-null.

### B4: `SchedulerDueLogic` (pure)
**Files:** `src/Paperbunkr.App/Services/Scheduling/SchedulerDueLogic.cs` (new)
**What:** `Evaluate(ScheduledTaskDescriptor, ScheduledTaskState, DateTimeOffset now) → DueDecision`
(`Run` / `Skip(reason)` / `NotDue`). Interval: `now - LastRunUtc >= IntervalHours` (null ⇒ Run).
DailyAt: local-time, ran-today guard. Disabled ⇒ `NotDue`.
**Depends on:** B1
**Verify:** `SchedulerDueLogicTests` — the full matrix from the design's test plan.

### B5: `ActivityService` — `Quiet` + real `Queued`
**Files:** `src/Paperbunkr.App/Services/IActivityService.cs` (edit),
`src/Paperbunkr.App/Services/ActivityService.cs` (edit),
`src/Paperbunkr.App/Models/ActivityJob.cs` (edit)
**What:** `ActivityJob.Quiet` (bool init). `StartJob(..., bool startQueued = false)` — when true,
job enters `_active` with `Status = Queued`; new `IActivityJobHandle.Begin()` promotes to
`Running`. `SettleJob`: skip `CompletionToastRequested` when `job.Quiet` and status is not
`Failed` (failures still toast unless caller says otherwise — scheduler passes `Quiet=true` and
suppresses via the notification level check in B6, so keep it simple: `Quiet` suppresses all
non-failure toasts; scheduler decides failure toasts).
**Depends on:** none
**Verify:** `ActivityServiceTests` — queued job promotes on `Begin()`; `Quiet` success raises no
toast; `Quiet` failure still raises.

### B6: `SchedulerService` + `ISchedulerService`
**Files:** `src/Paperbunkr.App/Services/Scheduling/ISchedulerService.cs` (new),
`src/Paperbunkr.App/Services/Scheduling/SchedulerService.cs` (new),
`src/Paperbunkr.App/Services/Scheduling/ScheduledRunStore.cs` (new — DB read/write/seed of
`ScheduledTaskState`, mirrors `ActivityHistoryStore` shape)
**What:** interface from the design. `ScheduledRunStore`: `SeedMissing(catalog)`, `LoadAll()`,
`Save(state)`, plus legacy-column mirror for the 3 mirrored tasks (`db-backup`,
`content-type-sweep`, `verify-covers`). `SchedulerService`: ctor `(IActivityService, catalog,
ScheduledRunStore, Func<DateTimeOffset> now, Func<Func<Task>,Task> run)` (the `run` seam lets tests
execute synchronously). `Start()` → seed, startup pass (evaluate all, enqueue due), start
`PeriodicTimer(15min)` loop. Pump: priority-ordered queue, start while `running < 2` and no
running task shares resource class; others sit `Queued`. Per run: `StartJob(kind, name,
cancellable, trigger:Scheduled)` with `Quiet` per `ScheduledTaskNotificationLevel`; `handle.Begin()`
when it leaves the queue; run `descriptor.RunAsync`; `Succeed`/`Fail`; on fail raise the deduped
alert (respecting level for the toast); write state (`LastRunUtc`, `LastRunStatus`,
`LastRunActivityId`) + mirror. `NotifyRan` stamps state. `RunNowAsync` bypasses enabled+due, still
queues. `Stop()` cancels timer + tokens.
**Depends on:** B1, B3, B4, B5
**Verify:** `SchedulerServiceTests` — queue order; resource concurrency; skip-same-kind;
failure→alert+stamp; `NotifyRan`; `RunNowAsync`; notification levels; mirror writes.

### B7: `ScheduledTaskRow`
**Files:** `src/Paperbunkr.App/Models/ScheduledTaskRow.cs` (new)
**What:** `ObservableObject` projection per the design; `NextRunLabel` logic; `IsRunning`/`IsQueued`
observed from `IActivityService.Changed` (or fed by the VM). `RunNowCommand` delegates to
`ISchedulerService.RunNowAsync`.
**Depends on:** B6
**Verify:** covered by `AutomationSectionViewModelTests` in C2.

### B8: History retention change
**Files:** `src/Paperbunkr.App/Services/ActivityHistoryStore.cs` (edit)
**What:** in `PruneOnStartup`, the Nth-newest-timestamp floor query filters out
`r.Trigger == ActivityTrigger.Scheduled && r.Status == ActivityRunStatus.Succeeded`. Age delete
unchanged.
**Depends on:** none
**Verify:** `ActivityHistoryStoreTests` (or wherever prune is tested) — scheduled successes past
#200 pruned by age; manual rows past #200 kept; scheduled failures kept.

### B9: Compose in `MainViewModel` + `App.axaml.cs`
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit),
`src/Paperbunkr.App/App.axaml.cs` (edit)
**What:** `MainViewModel`: build `SchedulerService` (after `Activity`), expose `ISchedulerService
Scheduler { get; }`. `App.axaml.cs`: delete `scanFoldersOnStartup` block (`:145-190`) + the two
`RunAutoBackupIfDue`/`RunContentTypeSweepIfDue` startup `Task.Run`s (`:89-97`); after
`pluginHost.Initialize`, `mainViewModel.Scheduler.Start()`; add `mainViewModel.Scheduler.Stop()` to
the `desktop.Exit` handler (keep the existing backup-on-exit line).
**Depends on:** B6
**Verify:** `MainViewModelTests` build; app launches (manual/headless smoke).

### B10: `NotifyRan` in the 5 manual commands
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)
**What:** `ScanLibraryFolders`, `ScanBookFolders`, `GenerateCovers`, `VerifyCovers`, `SyncMetadata`
each take an `ISchedulerService` (thread through the ctor) and call
`_scheduler.NotifyRan("<taskId>", Succeeded/Failed)` in their terminal path.
**Depends on:** B6, B9
**Verify:** `PreferencesScreenViewModelTests` — a fake `ISchedulerService` records the `NotifyRan`
call after each command.

### B11: Scheduler core regression
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~Scheduler"` +
`~ActivityService` + `~ActivityHistory`; `dotnet test src/Paperbunkr.Data.Tests --filter
"FullyQualifiedName~ScheduledTaskState"`; `dotnet build`.

---

## Phase C — Scheduler UI

### C1: `PreferencesSection.Automation`
**Files:** `src/Paperbunkr.App/Models/PreferencesSection.cs` (edit),
`src/Paperbunkr.App/Models/PreferenceIndex.cs` (edit — add searchable entries),
`src/Paperbunkr.App/Views/PreferencesScreen.axaml` (edit — nav button + section host)
**What:** add `Automation` between `Library` and `Reader` in the enum. `PreferencesSectionMeta.Label`
default `"Automation"` is fine. Nav `Button Classes="prefNavItem"
Classes.active="{Binding IsAutomationSection}" Command="{Binding GoAutomationCommand}"
Content="Automation"` and `<pref:AutomationSection IsVisible="{Binding IsAutomationSection}" />`.
**Depends on:** none
**Verify:** `PreferenceIndexTests` if entries added; build.

### C2: `AutomationSectionViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/Preferences/AutomationSectionViewModel.cs` (new)
**What:** `Tasks` (`ObservableCollection<ScheduledTaskRow>` built from `ISchedulerService.Tasks`),
`NotificationLevel` (bound to `AppSettings.ScheduledTaskNotificationLevel`, persists on change),
subscribes to `ISchedulerService.Changed` to refresh rows. Row edits (`Enabled`, `Mode`,
`IntervalHours`, `DailyAtTime`) call `ISchedulerService.SetEnabled` / `SetSchedule`.
**Depends on:** B6, B7
**Verify:** `AutomationSectionViewModelTests` — rows reflect catalog+state; toggling persists;
schedule edit persists; `NextRunLabel` "on next launch" vs a time; notification level persists.

### C3: `AutomationSection` view
**Files:** `src/Paperbunkr.App/Views/Preferences/AutomationSection.axaml` (new),
`src/Paperbunkr.App/Views/Preferences/AutomationSection.axaml.cs` (new — same step, AVLN2000)
**What:** header `ComboBox` (notification level) + `ItemsControl` over `Tasks` with the row layout
from the design. Mode `ComboBox` toggles interval `NumericUpDown` ↔ time `TimePicker` via
`IsVisible`. "Run now" `Button` → `RunNowCommand`. "→ History" opens the Activity drawer History
tab filtered (reuse existing nav if available, else a plain drawer-open). Semantic
`DynamicResource` tokens only. Load `~/.claude/skills/avalonia/avalonia-pro-max/layout-patterns`
+ `components` SKILL.md before writing.
**Depends on:** C2
**Verify:** headless smoke (C7).

### C4: `PreferencesScreenViewModel` wiring
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)
**What:** `IsAutomationSection`, `GoAutomation()` `[RelayCommand]`, `OnActiveSectionChanged`
notifies `IsAutomationSection`, hold an `AutomationSectionViewModel` (needs `ISchedulerService` +
the settings context — thread through the ctor from `MainViewModel`). `PreferencesScreen.axaml`
already hosts sections via `pref:` controls; give `AutomationSection` its `DataContext`.
**Depends on:** C2, B9
**Verify:** `PreferencesScreenViewModelTests` — section toggles; VM exposes the Automation VM.

### C5: Remove `ScanFoldersOnStartup` UI
**Files:** `src/Paperbunkr.App/Views/Preferences/GeneralSection.axaml` (edit — remove the
checkbox), `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit — remove
`ScanFoldersOnStartup` prop + `OnScanFoldersOnStartupChanged` at `:571`/`:791`)
**What:** delete the control + binding + VM member (column already dropped in B1).
**Depends on:** B1
**Verify:** `PreferencesScreenViewModelTests` — remove the `ScanFoldersOnStartup` assertions
(`:322`, `:344`); build.

### C6: Activity drawer "Scheduled" tab + `UpcomingTasks`
**Files:** `src/Paperbunkr.App/ViewModels/ActivityCenterViewModel.cs` (edit),
`src/Paperbunkr.App/Views/ActivityDrawerView.axaml` (edit)
**What:** `ActivityCenterViewModel` gains `IReadOnlyList<UpcomingTaskRow> UpcomingTasks` (name +
`NextRunLabel`, enabled only, sorted by next run) fed by `ISchedulerService` (thread it in).
`ActivityDrawerView.axaml:99` — remove `IsEnabled="False"`, add a `ShowScheduledTabCommand` +
`IsScheduled` and a read-only pane: `ItemsControl` over `UpcomingTasks` + a "Manage in Preferences
→" `Button` running the go-to-preferences-Automation nav.
**Depends on:** B6
**Verify:** `ActivityCenterViewModelTests` — `UpcomingTasks` content/order; tab switch.

### C7: UI smoke + review checklist
**Verify:** headless (`avalonia-testing`) — Automation tab renders 7 rows, toggle/interval persist
across a VM rebuild, mode combo swaps controls, "Run now" starts a visible job, drawer Scheduled
tab shows up-next + nav works. Then walk `~/.claude/skills/avalonia/avalonia-pro-max/review-checklist/SKILL.md`.

---

## Phase D — Docs + final verification

### D1: Roadmap docs
**Files:** `docs/alpha-todo.md` (edit), `docs/Paperbunkr-Roadmap.md` (edit)
**What:** scheduler recorded as a shipped deliberate deviation (with what was verified, not just
the commit message); cover-durability root fix recorded as closing the "covers wiped on close"
defect and superseding the 2026-08-27 identity-fingerprint scheme + the 2026-08-30 periodic
verification pass.

### D2: Full verification
**Verify:** targeted `dotnet test` subsets across `Paperbunkr.App.Tests` (Cover, Scheduler,
Activity, Preferences) + `Paperbunkr.Data.Tests` (ScheduledTaskState, migration); `dotnet build`
clean; launch the app once and confirm it starts, the Automation tab loads, and a "Run now" job
appears in the Activity Center. Report results honestly (which suites, pass counts, anything
skipped).

---

## Risk notes

- **A4 is the delicate step** — `CoverThumbnailService` / `BookCoverThumbnailService` have real
  test coverage and several callers (`MainViewModel`, `LiveFolderWatchService:266`,
  `LibraryScreenViewModel:1184`, `MigrationViewModel:294`, `PreferencesScreenViewModel` ×several).
  Change semantics, keep signatures; run every `~Cover` test after.
- **B10 ctor threading** — `PreferencesScreenViewModel` already has a 15-arg ctor; adding
  `ISchedulerService` touches `MainViewModel` composition and every `PreferencesScreenViewModelTests`
  construction. Add it as a defaulted trailing param on the internal test ctor to limit churn.
- **Concurrent sessions / shared tree** — check `git status` before starting; the migration must
  rebase onto real HEAD. Shared dev DB (`%APPDATA%\Paperbunkr\paperbunkr.db`) — running the app or
  `dotnet ef` migrates it.
- **Suite flake** — never run the whole `Paperbunkr.App.Tests` at once; targeted `--filter` only.
