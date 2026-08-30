# Database Corruption Safeguards — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-29-db-corruption-safeguards-design.md*

Branch: new branch off `master`, e.g. `db-corruption-safeguards` (current branch
`cover-thumbnail-content-verification` holds unrelated, already-shipped work and stays untouched).

## Step 1: WAL mode + crash-safe pragmas
**Files:** `src/Paperbunkr.App/Services/PaperbunkrDb.cs` (edit)
**What:** In `CreateContext()`, after building the context, open the connection and run
`PRAGMA journal_mode = 'WAL'`, `PRAGMA synchronous = 'FULL'`, `PRAGMA busy_timeout = 5000`,
`PRAGMA foreign_keys = 'ON'`, exactly as spec'd (§1) — `synchronous = FULL`, not `NORMAL`, per the
2026-08-30 decision. `PaperbunkrDbContextFactory` (design-time `dotnet ef` tooling) is untouched.
**Depends on:** none
**Verify:** new `src/Paperbunkr.App.Tests/PaperbunkrDbTests.cs` — via
`PaperbunkrDbContext.DatabasePathOverride` (same seam `BackupServiceTests`/`ReaderScreenViewModelTests`
use, join `AvaloniaTestCollection`), call `PaperbunkrDb.CreateContext()`, then run a follow-up raw
`PRAGMA journal_mode;` / `PRAGMA synchronous;` read against a fresh connection to the same file and
assert `wal` / `2` (FULL).

## Step 2: Auto-backup settings columns
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), `src/Paperbunkr.Data/PaperbunkrDbContext.cs`
(edit `OnModelCreating`), new migration under `src/Paperbunkr.Data/Migrations/`
**What:** Add `AutoBackupEnabled` (bool, default `true`) and `AutoBackupMinIntervalHours` (int,
default `4`) to `AppSettings`, doc-commented per this file's existing convention (see
`LastCoverVerificationUtc` for the most recent example). Add `HasDefaultValue` configuration for
both in `OnModelCreating` (mirror `BackupsToKeep`'s `builder.Property(a => a.BackupsToKeep).HasDefaultValue(5)`
at line ~805). Scaffold the migration with `dotnet ef migrations add AddAutoBackupSettings
--project src/Paperbunkr.Data` (follow the `20260830163017_AddLastCoverVerificationUtc.cs` shape —
two `AddColumn` calls, `nullable: false` with a `defaultValue`).
**Depends on:** none
**Verify:** extend `src/Paperbunkr.Data.Tests/AppSettingsTests.cs` — `GetOrCreateAppSettings_CreatesRowWithDefaults_OnFirstAccess`
gets two new assertions (`AutoBackupEnabled == true`, `AutoBackupMinIntervalHours == 4`).

## Step 3: Checkpoint-before-copy in BackupService
**Files:** `src/Paperbunkr.App/Services/BackupService.cs` (edit)
**What:** In `BackupNow()`, before the existing `ClearAllPools()` + `File.Copy`, open a throwaway
`SqliteConnection` to the live db path and run `PRAGMA wal_checkpoint(TRUNCATE);`, then
`ClearAllPools()` again before the copy (spec §2's exact snippet). This makes the plain-file-copy
correct now that Step 1 puts every context into WAL mode.
**Depends on:** Step 1 (WAL mode is what makes this necessary; harmless no-op against a
rollback-journal-mode file if run first, so order isn't strictly load-bearing, but ship after Step 1
so the test in this step is verifying a real gap)
**Verify:** extend `src/Paperbunkr.App.Tests/BackupServiceTests.cs` with a new fact: open a WAL-mode
connection to the test db (raw `PRAGMA journal_mode='WAL'` via `_dbOptions`), write a row via a
*second*, still-open connection (don't dispose it — simulates a pending checkpoint), call
`BackupNow()`, then open the backup file directly on a fresh context and assert the written row is
present. This is the exact scenario the design doc calls out as the one worth proving.

## Step 4: Auto-backup trigger logic + settings plumbing
**Files:** `src/Paperbunkr.App/Services/BackupService.cs` (edit)
**What:** Add `GetAutoBackupEnabled()`/`SetAutoBackupEnabled(bool)` and
`GetAutoBackupMinIntervalHours()`/`SetAutoBackupMinIntervalHours(int)`, mirroring the existing
`GetBackupsToKeep`/`SetBackupsToKeep` pair. Add `RunAutoBackupIfDue()`: no-op if
`AutoBackupEnabled` is false; otherwise inspect `GetAvailableBackups()` (already newest-first),
parse the newest filename's timestamp (`paperbunkr_backup_yyyyMMdd_HHmmss.db`, same format
`BackupNow()` writes), and call `BackupNow()` only if that timestamp is older than
`AutoBackupMinIntervalHours` (or no backups exist yet). Wrap the whole thing in try/catch —
callers use this as best-effort and must never let a backup failure propagate.
**Depends on:** Step 2 (new AppSettings columns), Step 3 (checkpoint-safe `BackupNow()`)
**Verify:** extend `BackupServiceTests.cs` — `RunAutoBackupIfDue` skips when a recent backup exists
and under the interval, runs when none exist, runs when the newest is older than the interval, and
no-ops entirely when `AutoBackupEnabled` is false.

## Step 5: Startup integrity check service
**Files:** new `src/Paperbunkr.App/Services/DatabaseIntegrityService.cs`
**What:** Static `CheckIntegrity(out string? detail)` exactly as spec'd (§3) — `true` with `detail =
null` if the db file doesn't exist yet (first launch), otherwise open a throwaway connection and run
`PRAGMA integrity_check;`, returning `result == "ok"`.
**Depends on:** none
**Verify:** new `src/Paperbunkr.App.Tests/DatabaseIntegrityServiceTests.cs` — no file → true/null;
a clean EF-created temp db → true; a temp db file with its middle bytes overwritten with garbage →
false with a non-"ok" `detail`.

## Step 6: DatabaseRecoveryWindow
**Files:** new `src/Paperbunkr.App/Views/DatabaseRecoveryWindow.axaml` + `.axaml.cs` (**both in the
same step** — see CLAUDE.md's AVLN2000 build gotcha: a new `.axaml` without its compiled
code-behind partial class in the same build fails XAML weaving)
**What:** Mirror `CrashReportWindow`'s pattern exactly — plain code-behind `Window`, no ViewModel,
shown before `MainWindow` exists via a blocking `ShowModal`-style static method using
`DispatcherFrame` (same reasoning: this runs before the UI thread has anything else pumping).
Content: explanatory text ("Paperbunkr's database could not be opened" + the `detail` string from
`CheckIntegrity`), a list of `BackupService.GetAvailableBackups()` entries with timestamps (reuse
the filename-parsing done in Step 4, or just show the filename — keep it simple, this is a rare
recovery screen not a polished list), three buttons: **Restore** (disabled + explanatory line if
the backup list is empty), **Start Fresh**, **Quit**. Expose an outcome enum
(`DatabaseRecoveryOutcome`: `Restore`, `StartFresh`, `Quit`) and, when `Restore` is chosen, which
backup path was selected.
**Depends on:** Step 5 (needs `CheckIntegrity`'s detail string to display), reads `BackupService`
(no code changes needed there beyond what Steps 3-4 already added)
**Verify:** manual/on-screen only — this window is startup-path UI shown before `MainWindow` exists,
same as `CrashReportWindow`, which has no automated test coverage either. Confirm by temporarily
corrupting a throwaway test db and launching the app (see Step 8's manual verification).

## Step 7: Wire integrity check + recovery + auto-backup into App.axaml.cs
**Files:** `src/Paperbunkr.App/App.axaml.cs` (edit)
**What:**
1. Before the existing `HasAnySeries()` try/catch, call `DatabaseIntegrityService.CheckIntegrity(out
   var detail)`. If false: log via `DiagnosticsService`, show `DatabaseRecoveryWindow`, and act on
   the outcome —
   - `Restore`: `BackupService.RestoreBackup(path)`, then relaunch the process (mirror however
     `CrashReportWindow`'s `Restart` outcome currently triggers a relaunch — check
     `Program.cs`/wherever `CrashOutcome.Restart` is consumed and reuse that same relaunch path)
     and exit this process.
   - `StartFresh`: rename the corrupt file to `paperbunkr.db.corrupt-<timestamp>` (and its
     `-wal`/`-shm` sidecars if present) next to the original rather than deleting, then fall through
     to the existing fresh-install flow unchanged (full reset — no settings/folder paths carried
     over, since renaming the old file away means `EnsureCreated()` just creates a brand-new one).
   - `Quit`: call `desktop.Shutdown()` (or equivalent) and return.
2. After the existing `EnsureCreated()` succeeds, fire-and-forget
   `Task.Run(() => new BackupService().RunAutoBackupIfDue())` (background thread, non-blocking,
   per spec §2's startup trigger).
3. On the existing `desktop.Exit += (_, _) => pluginHost.Shutdown();` line, add a second handler
   (or extend the existing one) calling `new BackupService().RunAutoBackupIfDue()` synchronously —
   best-effort, wrapped in try/catch internally (Step 4 already does this), never blocking/delaying
   exit beyond a normal synchronous backup-file-copy.
**Depends on:** Steps 4, 5, 6
**Verify:** no automated test (this is composition-root startup wiring, matching how the existing
`HasAnySeries`/`EnsureCreated` calls here have no direct test either) — covered by Step 8's manual
pass plus the unit tests on the services it calls.

## Step 8: Preferences Advanced tab UI for auto-backup settings
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/Preferences/AdvancedSection.axaml` (edit)
**What:** Add `[ObservableProperty] private bool _autoBackupEnabled` and
`[ObservableProperty] private int _autoBackupMinIntervalHours`, loaded in the same block as
`BackupLocation`/`BackupsToKeep` (~line 522-526, under the existing `_suppressBackupSettingsApply`
guard) and written back via `partial void OnAutoBackupEnabledChanged`/`OnAutoBackupMinIntervalHoursChanged`
calling the Step 4 setters, mirroring `OnBackupLocationChanged`/`OnBackupsToKeepChanged` exactly.
In `AdvancedSection.axaml`'s "Backup Manager" group box, add a `CheckBox` bound to
`AutoBackupEnabled` ("Automatically back up on startup and shutdown") and a `NumericUpDown` bound
to `AutoBackupMinIntervalHours` (minimum 1, sensible max e.g. 168) directly after the existing
"Backups to Keep" `NumericUpDown` (~line 87) and before the "Backup Now" button.
**Depends on:** Step 4 (needs the getter/setter methods)
**Verify:** extend `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` with a fact
asserting the new properties round-trip through `BackupService` the same way the existing
`BackupLocation`/`BackupsToKeep` tests do (find and mirror that existing test).

## Step 9: Manual verification pass
**What:** Per spec's Testing section — `taskkill /F` the app mid-scan of a large library a few
times against a throwaway library/db, confirm no "database disk image is malformed" occurs
post-change where it was reproducible before. Also manually corrupt a throwaway
`paperbunkr.db` (overwrite some bytes) and launch the app to confirm `DatabaseRecoveryWindow`
appears with working Restore/Start Fresh/Quit paths. This can't be scripted; do it once before
calling the feature done, per the design doc's own note that this is the actual failure mode that
prompted the spec.
**Depends on:** all prior steps
**Verify:** manual, on the running app (not the CI test suite).

## Real bug found during implementation

Manual verification (Step 9) caught a genuine race condition, not anticipated by the design doc:
`App.axaml.cs`'s new startup auto-backup trigger (`Task.Run(() => new BackupService().RunAutoBackupIfDue())`,
Step 7) fires on a background thread immediately after `EnsureCreated()`. On a genuinely fresh
install, nothing has created the `AppSettings` singleton row yet - `RunAutoBackupIfDue()` calling
`GetAutoBackupEnabled()` races the main thread's own first `GetOrCreateAppSettings()` call
(`SkinService.ApplyPersistedSettings()`), and both can see no row and both attempt to `INSERT
Id=1`, crashing with `SQLite Error 19: 'UNIQUE constraint failed: AppSettings.Id'`. Reproduced via
a real `dotnet run` against a throwaway fresh db (`PAPERBUNKR_DB_PATH` env var), confirmed in
`crash-20260830-191742-407.log`. Fixed by making `PaperbunkrDb.EnsureCreated()` itself call
`context.GetOrCreateAppSettings()` synchronously (Step 1's file, `PaperbunkrDb.cs`) so the
singleton row deterministically exists before `EnsureCreated()` returns to any caller - every later
access, including the racing background thread, is then a plain SELECT. Re-verified with the same
fresh-db repro: clean startup, WAL file correctly checkpointed into the main file on shutdown
(confirmed by file size: 4KB stub with a 2.2MB pending `-wal` → 516KB single file with `-wal`
gone), and the corruption/recovery path also verified against a genuinely corrupted file (real
`PRAGMA integrity_check` failure: "Tree 50 page 50: btreeInitPage() returns error code 11") -
`DatabaseRecoveryWindow`'s Restore path completed and the app relaunched cleanly.

## Test suite notes
- Every new/extended App-side test that touches the real db path must use
  `PaperbunkrDbContext.DatabasePathOverride` and join `AvaloniaTestCollection`, per
  `BackupServiceTests`'s own doc comment about the shared static field racing other test classes.
- Run `dotnet test` for both `Paperbunkr.App.Tests` and `Paperbunkr.Data.Tests` after each step,
  not just at the end — several steps touch the same shared `AppSettings`/`BackupService` surface.
