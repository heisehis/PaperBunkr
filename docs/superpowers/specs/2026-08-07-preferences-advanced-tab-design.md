# Preferences Screen — Advanced Tab (File Association + Backup Manager)

*Date: 2026-08-07. Fourth tab on the shell established by
docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md §5. Scoped after reading CE's
actual Advanced-tab source (`grpIntegration`, `groupMessagesAndSocial`, `groupMemory`,
`grpBackupManager`, `grpDatabaseBackup`, `groupOtherComics`, `grpLanguages`) directly.*

## 1. Triage

- **File Association** (`grpIntegration`) - real, small: `Paperbunkr.Common/Win32/ShellRegister.cs`
  already has `RegisterFileOpen`/`UnregisterFileOpen`/`IsFileOpenRegistered` fully ported and
  working (writes through `HKEY_CLASSES_ROOT`, which redirects to the per-user
  `HKCU\Software\Classes` without needing elevation - `ShellRegister.CanRegisterShell` already
  probes this). Just needs a UI + a thin service.
- **Backup Manager + Database Backup** (`grpBackupManager`/`grpDatabaseBackup`) - real, the
  biggest piece, explicitly flagged in docs/ce-feature-inventory.md §H as "build a full in-app
  system, not just guidance." **Not porting CE's own `BackupManager`/`BackupFileCollector`/
  `BackupArchiveCreator`** (`ComicRack.Engine/Backup/`) - that apparatus backs up CE's sprawling
  multi-file footprint (`ComicRack.ini`, alternate configs, scripts, resources, custom
  thumbnails, cache folders). Paperbunkr's entire footprint of record is one SQLite file; a
  from-scratch, right-sized service is a better fit than porting machinery built for a shape
  Paperbunkr doesn't have.
- **Skipped** (each gates something that either doesn't exist or is out of scope): memory/cache
  limit sliders (`groupMemory` - Paperbunkr's actual caches, `CoverImageCache`/
  `PageImageDecoder`'s page cache, have no size-limit enforcement to back a slider with yet),
  file write-back to embedded `ComicInfo.xml` (`groupOtherComics` - real but a standalone
  archive-rewrite feature, its own future spec), "reset hidden messages" (`groupMessagesAndSocial`
  - gates a dismissible-warning feature Paperbunkr doesn't have), language packs (`grpLanguages` -
  a full i18n system, a non-starter with nothing comparable planned).

## 2. File Association

New `IShellFileAssociation` (`Paperbunkr.App.Services`) wrapping `ShellRegister` behind an
interface - **deliberately abstracted, unlike `SkinService`/`CoverThumbnailService`'s
context-factory seam**, because this one touches the real Windows registry: tests must never
write real `HKEY_CLASSES_ROOT` entries on the dev/CI machine, so the test double is a fake
in-memory implementation, not a redirected-but-still-real one.

`FileAssociationService.GetAvailableFormats()` lists every `Providers.Readers.GetSourceFormats()`
entry (the same registered format list `PageImageDecoder`/`LibraryFolderScanner` already dispatch
against) with a per-format "is every one of its extensions currently associated" flag.
`SetAssociated(formatName, bool)` registers/unregisters every extension in that format under a
shared `Paperbunkr.{FormatName}` ProgID, then calls `ShellRegister.RefreshShell()`.

UI: a checklist (format name + checkbox), no separate "Associate" button - each toggle applies
immediately, consistent with every other Preferences control in this screen.

## 3. Backup Manager

`AppSettings` gains: `BackupLocation` (`string?`, null = `%AppData%\Paperbunkr\backups`),
`BackupsToKeep` (`int`, default 5). **On-startup/on-shutdown scheduled triggers (CE's
`OnStartup`/`OnExit` flags) are deliberately deferred** - manual "Backup Now"/"Restore" proves the
mechanism first, same "prove it manually before automating it" precedent as Book Folders'
on-demand-scan-before-live-watch staging.

`BackupService` (`Paperbunkr.App.Services`):
- `BackupNow()` - copies the live SQLite file (`PaperbunkrDbContext.GetDefaultDatabasePath()`) to
  `{BackupLocation}\paperbunkr_backup_{timestamp}.db`, then prunes anything beyond
  `BackupsToKeep` (oldest-first by filename timestamp - a plain `File.Copy`, not
  `SqliteConnection.BackupDatabase()`, since every `PaperbunkrDbContext` in this codebase is
  already short-lived and closed between operations, so there's no open connection to race
  against at the moment a user clicks "Backup Now").
- `GetAvailableBackups()` - lists existing backup files, newest first.
- `RestoreBackup(path)` - copies the chosen backup over the live database file. **Requires an app
  restart to take effect** (surfaced in the UI, not silently swapped mid-session) - simplest safe
  behavior given Paperbunkr has no long-lived single connection to safely hot-swap underneath.

UI: backup location (text + "Browse…" via `IFilePickerService.PickFolderAsync`), backups-to-keep
number box, "Backup Now" button, a list of existing backups each with a "Restore" button (behind
a confirm - first genuinely destructive one-click action in Preferences, same two-step-confirm
precedent as the Migration UX polish pass's file-removal action).

## Testing

- `FileAssociationServiceTests`: exercises `FileAssociationService` against a fake
  `IShellFileAssociation` (in-memory dictionary) - never touches the real registry.
- `BackupServiceTests`: `BackupNow` against a temp SQLite file + temp backup folder (redirecting
  `PaperbunkrDbContext.DatabasePathOverride`, the same seam `ReaderScreenViewModelTests` uses);
  retention pruning deletes the oldest beyond the configured count; `RestoreBackup` correctly
  overwrites the live file's content.
- `PreferencesScreenViewModelTests`: Advanced tab flag; toggling a file association calls through
  to the fake registry; Backup Now / Restore commands drive `BackupService` and refresh the list.
- Manual verification: same no-GUI-automation approach as prior specs - ask the user to associate
  a real extension and confirm double-clicking a `.cbz` in Explorer launches Paperbunkr; run a
  real Backup Now and confirm the file lands in the configured folder.
