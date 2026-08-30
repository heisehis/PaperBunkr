# Database Corruption Safeguards

**Date:** 2026-08-29
**Status:** Approved — open questions resolved 2026-08-30 (see bottom)
**Source doc:** Design session with Ehis (2026-08-29), prompted by a real "database disk image is
malformed" failure on his own machine, tracing back to a force-quit (or process crash) while a
write was in flight. Confirmed against the current codebase rather than assumed: `PaperbunkrDb.cs`
opens SQLite with no `journal_mode`/`synchronous`/`busy_timeout` pragma set anywhere (default
rollback-journal mode), `BackupService` (docs/superpowers/specs/2026-08-07-preferences-advanced-tab-design.md
§3) is manual "Backup Now"/"Restore" only — its own doc comment already flags "scheduled
on-startup/on-shutdown triggers are a deliberately deferred follow-up once this is proven" — and
`App.axaml.cs`'s two `Database.Migrate()`-adjacent try/catches (`HasAnySeries`, `EnsureCreated`)
log the exception via `DiagnosticsService.LogCrash` and then `throw`, which is a hard, silent
termination with no recovery path if the live `paperbunkr.db` itself is unreadable.

## Context

Paperbunkr's entire footprint of record is one SQLite file
(`%AppData%\Paperbunkr\paperbunkr.db`, `PaperbunkrDbContext.GetDefaultDatabasePath()`), opened via
plain `Microsoft.Data.Sqlite`/EF Core with no non-default connection pragmas. SQLite's default
rollback-journal mode is *supposed* to be crash-safe (that's the entire point of the journal), but
it depends on the OS actually completing an `fsync` before the file is killed, and on the
`-journal` sidecar being rolled back cleanly on next open — a hard process kill mid-write, a forced
shutdown, or (very commonly on Windows) antivirus/indexing grabbing the `-journal` file at the
wrong moment can all leave the main `.db` file itself in an inconsistent state that SQLite then
reports as "database disk image is malformed" — not a recoverable "roll back the journal" case,
outright page-level corruption.

Three independent things are missing today, and Ehis wants all three:

1. **Make corruption from a crash/force-quit less likely in the first place** — WAL mode plus
   correct pragmas.
2. **Make sure a recent backup exists even if the user never clicked "Backup Now"** — automated
   backups on top of the existing manual `BackupService`.
3. **Make a corrupted DB non-fatal** — detect it at launch and offer recovery instead of the
   current log-and-crash behavior.

## Scope

### 1. WAL mode + crash-safe pragmas

`PaperbunkrDb.CreateContext()` (`src/Paperbunkr.App/Services/PaperbunkrDb.cs`) is the single choke
point every `PaperbunkrDbContext` in the app is built through (this codebase's own doc comment:
"Each call to `CreateContext` opens a fresh short-lived context"). Set pragmas immediately after
opening, before any query runs:

```csharp
public static PaperbunkrDbContext CreateContext()
{
    var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
        .UseSqlite($"Data Source={PaperbunkrDbContext.GetDefaultDatabasePath()}")
        .Options;
    var context = new PaperbunkrDbContext(options);
    context.Database.OpenConnection();
    context.Database.ExecuteSqlRaw("PRAGMA journal_mode = 'WAL';");
    context.Database.ExecuteSqlRaw("PRAGMA synchronous = 'FULL';");
    context.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 5000;");
    context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = 'ON';");
    return context;
}
```

- **`journal_mode = WAL`** is the actual headline fix for the "force-quit mid-write" trigger Ehis
  confirmed: in WAL mode, writes go to a separate `-wal` file and the main `.db` file is never
  touched mid-transaction, so a hard kill can at worst leave an incomplete `-wal` tail, which
  SQLite detects and discards automatically on the next open — there is no page-level corruption
  mode for an interrupted write the way rollback-journal mode has. This is the single highest-
  leverage change here.
- **`synchronous = FULL`** — stricter than the standard WAL+NORMAL pairing; costs an extra fsync on
  the WAL file per transaction but guarantees zero committed-transaction loss even on true power
  loss, not just crash-safety. Ehis chose this over NORMAL on 2026-08-30 (see open questions).
- **`busy_timeout = 5000`** guards against a second short-lived context racing another (this app
  opens many short-lived contexts, not one long-lived one) hitting `SQLITE_BUSY` instead of just
  waiting — cheap insurance, not corruption-related but worth bundling since it's the same call
  site.
- `journal_mode` is sticky per-database-file (persisted in the file header), so this only needs to
  run once in practice, but it's idempotent and cheap enough to just set on every `CreateContext()`
  call rather than trying to track "have we already set this."
- **`PaperbunkrDbContextFactory`** (used by `dotnet ef` design-time tooling) is deliberately left
  alone — it never touches a real user's database.

**Side effect that matters for §2:** WAL mode means the live database is no longer just
`paperbunkr.db` — it's that file plus `paperbunkr.db-wal` and `paperbunkr.db-shm` while any
connection is open, and uncommitted-but-not-yet-checkpointed data can live in the `-wal` file
rather than the main file. `BackupService.BackupNow()`'s current plain `File.Copy(dbPath, ...)`
would silently produce a backup missing recent data (or, without care, an inconsistent snapshot)
once WAL is in effect. §2 below accounts for this.

### 2. Automated backups

Extends the existing `BackupService` (`src/Paperbunkr.App/Services/BackupService.cs`) rather than
building a parallel mechanism — it already has `BackupLocation`/`BackupsToKeep`
(`AppSettings`), rotation (`PruneOldBackups`), and a Preferences → Advanced tab UI
(docs/superpowers/specs/2026-08-07-preferences-advanced-tab-design.md §3,
`PreferencesScreenViewModel`).

- **Checkpoint before copy.** Once §1 ships, `BackupNow()` must force a WAL checkpoint before the
  raw file copy, or the copy can miss data still sitting in `-wal`:
  ```csharp
  Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
  using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
  {
      conn.Open();
      using var cmd = conn.CreateCommand();
      cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
      cmd.ExecuteNonQuery();
  }
  Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
  File.Copy(dbPath, backupPath, overwrite: false);
  ```
  `TRUNCATE` (not just `PASSIVE`/`FULL`) both flushes the WAL into the main file *and* truncates
  the `-wal` file back to zero bytes, so the plain `File.Copy` of just `paperbunkr.db` stays
  correct and `BackupService` doesn't need to start copying three files or gain an EF-Core-side
  seam for it.
- **New trigger: on app startup**, after a successful `EnsureCreated()` (so a launch never backs up
  a database it hasn't verified opens cleanly — see §3 for why open-verification comes first).
  Fire-and-forget on a background thread so it can't add to startup latency perceptibly. This is
  the fallback that catches crashes/force-quits that skip shutdown entirely.
- **New trigger: on clean shutdown**, best-effort (wrap in try/catch, never block/delay exit). This
  is the primary trigger — it catches sessions left open all day that never restart, which
  startup-only would otherwise miss entirely.
- **De-dupe guard:** skip the automated backup if the most recent backup file (by the existing
  `paperbunkr_backup_*` naming/sort in `GetAvailableBackups()`) is less than some minimum age (default
  4 hours, configurable alongside `BackupsToKeep`) — otherwise a user who restarts the app
  repeatedly in a short session accumulates a backup per launch and `BackupsToKeep`'s rotation
  window becomes mostly startup noise instead of meaningful history.
- **New `AppSettings` fields:** `AutoBackupEnabled` (bool, default `true`) and
  `AutoBackupMinIntervalHours` (int, default `4`) — both surfaced next to the existing
  `BackupLocation`/`BackupsToKeep` controls in the Advanced preferences tab. New EF migration to
  add the two columns.
- Manual "Backup Now" behavior is otherwise unchanged — same method, same rotation, same location.

### 3. Startup integrity check + recovery

Today, `App.axaml.cs` calls `PaperbunkrDb.HasAnySeries()` then `PaperbunkrDb.EnsureCreated()`; a
`SqliteException` from either is logged via `DiagnosticsService.LogCrash(..., isTerminating: true)`
and rethrown, which — per `DiagnosticsService`'s own hook design — ends up on
`AppDomain.UnhandledException` with `allowContinue: false`: a one-way crash dialog, no recovery
offered, even though a good backup very likely exists one directory over.

New `DatabaseIntegrityService` (`src/Paperbunkr.App/Services/DatabaseIntegrityService.cs`), called
from `App.axaml.cs` **before** `HasAnySeries()`/`EnsureCreated()` currently run:

```csharp
public static class DatabaseIntegrityService
{
    /// <summary>
    /// PRAGMA integrity_check against the live db file, run against a throwaway connection before
    /// EF/migrations ever touch it. Returns true if the file doesn't exist yet (nothing to check -
    /// first launch) or passes; false only on a genuine structural problem.
    /// </summary>
    public static bool CheckIntegrity(out string? detail)
    {
        string dbPath = PaperbunkrDbContext.GetDefaultDatabasePath();
        if (!File.Exists(dbPath))
        {
            detail = null;
            return true;
        }

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        string result = (string)cmd.ExecuteScalar()!;
        detail = result;
        return result == "ok";
    }
}
```

Startup flow (`App.axaml.cs`, replacing the top of `OnFrameworkInitializationCompleted`):

1. `DatabaseIntegrityService.CheckIntegrity()`.
2. If it fails: log via `DiagnosticsService`, then show a dedicated **Database Recovery** window
   (new, `Views/DatabaseRecoveryWindow.axaml`, same "shown before `MainWindow` exists" pattern
   `CrashReportWindow` already uses) listing `BackupService.GetAvailableBackups()` with their
   timestamps, offering:
   - **Restore from backup** (pick one, defaults to the newest) → `BackupService.RestoreBackup()`
     onto the live path, then relaunch (mirrors `RestoreBackup`'s existing doc comment: "requires
     an app restart... no safe way to hot-swap the file underneath a running app").
   - **Start fresh library** (rename the corrupt file to `paperbunkr.db.corrupt-<timestamp>` next
     to it rather than deleting — never destroy the one artifact that might let Ehis or a future
     session hand-recover data from it — then let normal fresh-install flow proceed as a full
     reset: no watched-folder paths or other settings carried over, identical to a brand-new
     install).
   - **Quit** (current behavior, unchanged, for anyone who'd rather investigate manually first).
   - If `GetAvailableBackups()` is empty, the "Restore" option is disabled with an explanatory
     line rather than hidden — a user should learn from this dialog that no backup existed, which
     is itself the argument for §2 defaulting to *on*.
3. If it passes (or the file doesn't exist yet): proceed exactly as today (`HasAnySeries()` →
   `EnsureCreated()` → the automated startup backup from §2).

This only covers the **launch-time** case, which matches what Ehis has actually seen (a SQLite
error *on launch*). A corruption that somehow surfaces mid-session (WAL mode makes this
substantially less likely per §1, but not provably impossible — e.g. disk hardware failure) is
explicitly **out of scope** for this phase; §1 is what pushes that risk down, not a live in-session
detector.

## Non-goals (this phase)

- No change to `RestoreBackup()`'s existing "requires restart" model — building a true hot-swap
  is a much bigger change for a problem WAL mode + this recovery flow already covers.
- No cloud/off-machine backup destination — `BackupLocation` stays a local (or user-chosen, e.g.
  an already-synced folder) path, unchanged from today.
- No corruption *repair* (e.g. `.recover`/dump-and-reimport of a partially-readable file) — restore
  from backup or start fresh are the two paths; a from-scratch repair tool is a much larger,
  separate effort and backups make it low-value to build first.
- No mid-session integrity re-checks/polling — see §3's last paragraph.

## Testing

- `PaperbunkrDbTests` (new or extend existing DB test file): assert `CreateContext()` actually
  yields `journal_mode = wal` and `synchronous = 1` (NORMAL) via a follow-up `PRAGMA` read against
  a temp `DatabasePathOverride` file, same seam `BackupServiceTests` already uses.
- `BackupServiceTests`: extend to open a WAL-mode connection, write a row, call `BackupNow()`
  *without* closing that connection first, then open the backup file directly and assert the
  written row is present — proves the checkpoint-before-copy actually closes the gap rather than
  just asserting on the happy path where nothing was pending in the WAL.
- New `DatabaseIntegrityServiceTests`: a clean temp db → `CheckIntegrity()` true; a temp db file
  with its middle bytes overwritten with garbage → `CheckIntegrity()` false with a non-"ok" detail
  string; a path that doesn't exist yet → true (first-launch case).
- Manual verification pass (can't be scripted): `taskkill /F` the app mid-scan of a large library
  a few times pre/post this change, confirm no corruption post-change where it reliably reproduced
  pre-change. Worth Ehis doing this once against a throwaway library before calling this done,
  since it's the actual failure mode that prompted the spec.

## Open questions — resolved 2026-08-30

1. `synchronous`: **`FULL`**, not `NORMAL`. Ehis chose zero committed-transaction loss over the
   marginal fsync cost.
2. Auto-backup trigger: **both startup and shutdown**, shutdown as the primary trigger (catches
   "was open all day" sessions that never restart), startup as the fallback (catches crashes that
   skip shutdown entirely). Both still gated by the same 4-hour min-interval de-dupe guard.
3. "Start fresh library": **full reset**, identical to a brand-new install — no watched-folder
   paths or other settings carried over from the corrupt database.
