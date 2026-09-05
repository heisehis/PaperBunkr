# Live folder-watch scanning

**Date:** 2026-08-23
**Status:** Approved, pending implementation
**Backlog ref:** `docs/alpha-todo.md` / `docs/ce-feature-inventory.md` — "Folder-watch continuous
scanning" is a decided "build it" item, called out as independent of one-time CE migration
("Paperbunkr becomes a first-class library manager, not just an import target"). The
`WatchedFolder` entity's own doc comment already flags this as the deferred follow-up: "CE's
`WatchFolder` minus the live `FileSystemWatcher` flag, deferred to a follow-up (v1 is on-demand
'Scan Now' only)."

## Context

Today, adding comics to the library happens only on explicit user action: "Scan Now" in
Preferences → Libraries runs `LibraryFolderScanner.ScanAllAsync`, a one-shot pass over every
`WatchedFolder` that finds files not already in the DB. There is no live detection of files added,
removed, or renamed after that scan completes — the code comment on `LibraryFolderScanner` says so
directly: "No live `FileSystemWatcher` auto-import — still a real, deliberately deferred
follow-up."

**Checked against CE's real behavior first, per this project's standing rule
(`_reference/ComicRackCE`), rather than assuming what "folder watch" means:** CE's own `WatchFolder`
(`ComicRack.Engine/Database/WatchFolder.cs`) is much narrower than the roadmap phrase "continuous
scanning" suggests. Its `FileSystemWatcher` only ever subscribes to the `Renamed` event
(`ComicDatabase.watcherRenamedNotification`) — purely to keep a book's `FilePath` in sync when a
file or folder is renamed on disk, so it doesn't turn into a "missing file." CE never wired
`Created`/`Deleted`/`Changed`. New-file detection happened only via a full scan on startup (if
`ScanStartup` was enabled) or a manual Scan/"Add Folder to Library" action
(`MainForm.cs:949-951`, `1926`, `1944-1946`). So true live auto-import of new files — the behavior
this spec builds — is a deliberate Paperbunkr enhancement beyond CE parity, confirmed with the
user rather than assumed.

**User decisions from brainstorming:**
- Build both halves: CE's rename path-sync *and* new live auto-import/removal-awareness (not
  CE-faithful-only, not import-only).
- Per-folder `Watch` toggle (CE parity), not one global on/off for every watched folder.
- Auto-imported batches surface as a quiet, auto-dismissing toast — not the blocking progress-bar
  toast Scan Now uses, since this runs unattended.

## Design

### 1. Data model

Add `bool Watch` to `WatchedFolder` (`src/Paperbunkr.Data/Entities/WatchedFolder.cs`), default
`false`. New EF Core migration. Existing installs' watched folders come in unwatched — opting a
folder into live watching is a deliberate per-folder action, not a silent behavior change on
upgrade.

### 2. `LiveFolderWatchService` (new, `src/Paperbunkr.App/Services`)

One `FileSystemWatcher` per `WatchedFolder` row with `Watch = true`, each with
`IncludeSubdirectories = true`, subscribed to `Created`, `Deleted`, `Renamed`. `Changed` is not
subscribed — out of scope, matching CE (which never watched it either).

- `Start()` — builds watchers from the current DB state; called once from `App.axaml.cs` after
  `PaperbunkrDb.EnsureCreated()`, alongside the app's other manually-composed services (this
  codebase has no DI container — see `App.axaml.cs`).
- `Reload()` — disposes all current watchers and rebuilds from the DB. Called by
  `PreferencesScreenViewModel` after any add/remove/Watch-toggle on `WatchedFolders`.
- `Dispose()` — best-effort; not safety-critical since a `FileSystemWatcher` only holds OS watch
  handles, not data. Called on app shutdown if convenient, but a missed call is not a bug.
- Wraps `EnableRaisingEvents = true` in a try/catch exactly like CE's `WatchFolder.UpdateWatcher`
  does, for the same reason: a folder can become inaccessible (deleted, network share dropped)
  between being added and the watcher starting, and that must not crash the app.

**Debounced batch flush, not per-event reaction.** `Created`/`Deleted` events land in a
thread-safe `Dictionary<string, WatcherChangeTypes>` buffer keyed by path (later events for the
same path overwrite the earlier type — e.g., a temp-file dance during a copy that ends in
`Created` is what survives). A single debounce timer resets on every new event; when the buffer
goes quiet for 2 seconds, the whole buffer is flushed as one batch. This means a bulk drag-drop of
hundreds of files produces one import pass and one toast, not hundreds of individual DB writes —
and it sidesteps a real correctness issue: `Created` fires the instant the OS creates the file
handle, often well before a large file finishes writing, so reacting immediately would frequently
try to open a partial/locked archive.

`Renamed` is **not** debounced — it's applied immediately as its own atomic operation (see below),
since there's nothing to batch or wait for.

### 3. Per-event-type handling

**Renamed (CE parity).** On the raw event (not the debounce buffer): if `e.FullPath` is a
directory, prefix-update every `Issue.FilePath` that starts with the old directory path to the new
one (`ComicDatabase.watcherRenamedNotification`'s directory branch, ported as-is). If it's a file,
update the one `Issue` whose `FilePath` equals the old path. Either branch also sets
`FileIsMissing = false` on every touched issue — CE didn't need this (it had no persisted missing
flag to worry about), but Paperbunkr does, and a rename must not leave a previously-missing issue
looking missing at its new, entirely valid path. Single, cheap DB writes — no re-import, no
metadata proposals.

**Created, on flush.** Refactor `LibraryFolderScanner.ScanAll`'s per-file import body (the
`foreach (string file in candidateFiles)` block, currently inline) into a shared method that takes
an explicit file list rather than always enumerating an entire watched folder. Both the manual
"Scan Now" batch and the live-watch flush call this same method, so embedded-metadata handling,
filename-parsing fallback, series find-or-create, and metadata-proposal creation behave
identically either way — no parallel/duplicated import logic. Before importing each flushed path,
verify the file is readable: attempt to open it, and if it's locked (still being written or held
by another process), retry a few times with a short backoff over a bounded window (e.g. up to
~5 attempts / a few seconds total), then give up silently on that file for this flush — same "one
bad file doesn't stop the batch" contract `ScanAll` already has today. A file that's still locked
after the bounded retry window isn't lost: the next manual Scan Now (or the next time it's
modified, re-triggering `Created` via a save) picks it up.

Cover thumbnail generation runs for newly-added issues after the flush's `SaveChanges`, same as
the existing Scan Now → cover-generation pass (`docs/alpha-todo.md`'s 2026-08-09 bugfix note),
so live-imported issues get thumbnails without a separate manual step.

**Deleted, on flush.** Find the `Issue` whose `FilePath` matches the deleted path and set
`FileIsMissing = true`, then `SaveChanges`. This needed checking rather than assuming: I initially
expected "Missing Files" to be a live `File.Exists` check needing no DB write at all, but
`SmartListCatalog.cs:219` (`[SmartListField.IsMissing] = i => i.FileIsMissing`) and
`Issue.FileIsMissing`'s own declaration confirm it's a **stored** column, filtered on directly —
not computed at query time. Grepping the App layer for every place that sets it turned up only CE
migration import and reading-list placeholder creation; **there is currently no code path at all
that marks a natively-scanned issue missing after its file is deleted from disk** — the Needs
Review "Missing Files" section and the "Missing Files" system Smart List both silently miss any
file deleted outside of a relink/migration flow today. This spec's `Deleted` handling is the first
fix for that gap, not just a live-UI nicety. After the flag flip, the watcher still fires its
lightweight refresh event so already-open Needs Review/Library UI updates without waiting for the
next navigation.

### 4. UI

**Preferences → Libraries.** Each row in the `WatchedFolders` list gets a "Watch for changes"
checkbox bound to the new `Watch` column. Toggling it updates the DB and calls
`LiveFolderWatchService.Reload()` (same pattern `AddFolder`/`RemoveFolder` already use for
`RefreshWatchedFolders`).

**Toast on import.** After a flush that added one or more issues: a quiet, auto-dismissing toast
("3 new issues added") via the existing `MainViewModel.ToastRequested` event (`ShowToast`) — the
same plain-message toast completion messages already use, not `ProgressToastRequested` (that one's
for attended, blocking-feeling progress bars like Scan Now, which doesn't fit an unattended
background action). No toast fires for a flush that only contained deletes/renames/nothing
importable.

### 5. Testing

Same fixture pattern as `LibraryFolderScannerTests` (real sqlite-file `DbContext`, real `.cbz`
fixtures via `CbzFixture`, injectable context factory) — but writing files into a real temp
directory that a real `LiveFolderWatchService` is watching, then polling with a bounded timeout
for the debounced flush to complete, instead of calling `ScanAllAsync` directly:

- Dropping a new `.cbz` into a watched folder with `Watch = true` results in a new `Issue` after
  the debounce window, without any explicit scan call.
- A `.cbz` dropped into a `WatchedFolder` with `Watch = false` is *not* picked up (confirms the
  per-folder toggle actually gates watching, not just scanning).
- Renaming a file already in the library updates its `Issue.FilePath` immediately, without
  creating a duplicate `Issue` or a metadata proposal.
- Renaming a folder containing several already-migrated issues prefix-updates all of their
  `FilePath`s.
- Deleting a watched file sets that issue's `FileIsMissing` to `true` (and leaves
  `MissingAcknowledged` untouched), and it then appears in the "Missing Files" Needs Review section
  on the next `Refresh()` — closing the pre-existing gap where a deleted native-scan file was never
  marked missing at all.
- Renaming a file that was already flagged `FileIsMissing = true` (e.g. a prior manual edit) clears
  the flag once the rename lands it back on a real path.
- Rapid-fire creation of several files within the debounce window results in exactly one flush /
  one toast, not one per file.
- A file that's still open/locked when `Created` fires but closes within the retry window is
  successfully imported on the same flush.

## Explicitly out of scope

- `Changed` events (content-modification watching) — CE never watched these either; re-importing
  on every save of a large archive is its own can of worms (partial-write races, no clear "what
  changed" semantics) and isn't part of this ask.
- A settings knob for the debounce window — hardcoded constant (2s), revisit only if it proves
  wrong in practice.
- Network-share-specific watcher hardening beyond the basic try/catch-and-retry CE itself has —
  `FileSystemWatcher` on network drives has known OS-level quirks (buffer overflow under heavy
  activity, drives that don't support it at all); handling every one of those is a bigger,
  separate effort than this slice.
- Any change to `SyncMetadataAsync` — unaffected by this feature.
