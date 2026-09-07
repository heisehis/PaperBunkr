# Scan-Time Missing File Handling — Design

Two new Preferences → Library settings, CE parity for the "Scanning" section of CE's Preferences
dialog (`chkAutoRemoveMissing` / `chkDontAddRemovedFiles`): **automatically remove confirmed-missing
files during a scan**, and **never re-add a file the user manually removed**. Both extend the
existing (uncommitted, this branch) Library Health feature rather than duplicating it.

Date: 2026-09-06. Status: design, approved via grilling round in chat (5 questions, all answered;
2 remaining implementation-level calls made directly and flagged for the user's review below). Not
yet approved for `writing-plans`.

---

## Why now (context)

The [Library Health spec](2026-09-06-missing-files-library-health-design.md) shipped (uncommitted)
a manual "Verify Now" + "Remove All Confirmed Missing" flow, with a deliberate two-strikes grace
window before an issue becomes bulk-removal-eligible — a direct response to a real incident where a
metadata write-back false-positive caused genuine comics to be flagged missing and removed. That
spec explicitly deferred automatic triggering as a v1 non-goal:

> No automatic/scheduled Verify by default. Manual "Verify Now" only. ... defaulting a
> data-affecting sweep to run automatically right after the trust hit this spec exists because of
> would be the wrong call. Follow-up, not v1.

This spec is that follow-up, plus a second, previously-unaddressed gap found while comparing against
CE: **there is currently no mechanism anywhere in Paperbunkr that stops a manually-removed file from
being silently re-imported the next time a scan runs.** `LibraryFolderScanner.ScanAll` builds its
"already known" set purely from `Issue.FilePath` values still in the database
(`src/Paperbunkr.App/Services/LibraryFolderScanner.cs:82-84`) — once an `Issue` row is deleted, its
file is indistinguishable from a brand-new file on the next scan.

**CE-parity check (standing rule):** confirmed directly against `_reference/ComicRackCE`:
- `Settings.RemoveMissingFilesOnFullScan` (`chkAutoRemoveMissing`, default `false`) is passed into
  `ComicScanner.ScanFileOrFolder` as `removeMissing`; after the scan loop, `ComicScanner
  .ScanFolderQueue()` iterates linked `ComicBook`s for any `AutoRemove`-flagged item and removes any
  whose drive is connected (`driveChecker.IsConnected`) but file is gone
  (`!File.Exists`) — an immediate, single-pass removal, protected only by the drive-connected check,
  not a grace window.
- `Settings.DontAddRemoveFiles` (`chkDontAddRemovedFiles`, default `false`) is checked in
  `Program.ScannerCheckFileIgnore` against a persistent blacklist owned by `ComicDatabase`
  (`HashSet<string> blackList`, saved/restored with the database). The blacklist is populated
  **unconditionally** whenever a book is manually removed from the library UI
  (`ComicListLibraryBrowser.cs:169`), regardless of whether `DontAddRemoveFiles` is on — the setting
  only gates whether scanning *consults* the list, not whether it's built.

Paperbunkr deliberately diverges from CE's immediate single-pass removal (see Goals below) — CE's
own protection (drive-connected check) is real but weaker than the two-strikes counter Library
Health already has, and reusing that existing safety net is a straightforward, no-new-risk choice.

## Goals

- **Auto-remove-missing-on-scan** goes through the *same* two-strikes eligibility Library Health's
  manual "Remove All Confirmed Missing" already uses (`FileIsMissing && MissingVerificationCount >=
  2 && !MissingAcknowledged`) — turning the setting on means "run that same flow automatically after
  a scan," not "skip the grace window for CE-literal parity." This is a deliberate deviation from
  CE, justified by the incident the two-strikes window itself was built to prevent.
- Only an explicit **Scan Now** triggers it — never the live `FileSystemWatcher` deletion path
  (`LiveFolderWatchService`). That live-watch path is exactly what produced the original false-
  positive incident; auto-removing from that signal would reopen the same risk this whole area of
  work exists to close.
- **Don't-re-add-manually-removed** applies to *every* removal path in the app, not just Library
  Health's, matching CE's app-wide guarantee. Every removal already funnels through
  `LibraryDeletionHelper.RemoveIssue` / `RemoveSeries`
  (`src/Paperbunkr.App/Services/LibraryDeletionHelper.cs`) — confirmed the only two choke points, so
  the blacklist write lands there once rather than at each of the ~5 call sites individually.
- The blacklist is **permanent** and **always populated on removal**, regardless of whether the
  setting is on — matching CE exactly. Turning the setting on later still protects files removed
  earlier; turning it off doesn't lose data; shipping default-off is a zero-behavior-change no-op.
- Removals made via the auto-remove-on-scan path still land in the existing Recently Removed log
  with Restore available — same safety net as a manual Library Health removal, better than CE (which
  has no undo at all for this).
- The automatic path additionally checks the file's drive/root is currently reachable before acting
  on it (CE's own protection, `driveChecker.IsConnected`) — see the correction under Non-goals below.
  Manual "Remove All Confirmed Missing" doesn't need this (a human reviews the confirmation list
  before confirming); the unattended automatic path does, since nothing else catches a
  disconnected-drive false positive there.

## Non-goals (v1)

- No per-path browse/unblock UI for the removed-files blacklist — a single "Clear removed-files
  list" button (wipes the whole table) is the v1 escape hatch. A full management UI is a reasonable
  future follow-up if it turns out to be needed, not built speculatively now.
- No scoped-to-just-this-scan Verify variant. Scan Now already sweeps every library folder in one
  pass, so reusing the existing full-library `LibraryHealthService.VerifyAsync()` overload gives the
  same effective coverage without new code.
- ~~No equivalent of CE's drive-connected check~~ — **self-review correction:** this was wrong. The
  two-strikes counter does *not* defeat a disconnected drive on its own: an external drive unplugged
  during two *consecutive* Scan Now runs produces two real "missing" strikes with no human ever
  reviewing them, since the automatic path skips the confirmation dialog by design. A drive-connected
  check is therefore back in scope, but scoped narrowly — see Goals and the flow below. It only gates
  the *automatic* path; the manual "Remove All Confirmed Missing" button is unaffected (a human
  already reviews that list before confirming).
- Scheduled/automatic scanning is unrelated and untouched by this spec (CE's separate
  `Settings.ScanStartup`, not in scope here).

## Data model

Two new `AppSettings` fields (same file, same conventions as the rest of the class):

```csharp
/// <summary>
/// Whether a Scan Now automatically runs Library Health's "Remove All Confirmed Missing" (same
/// two-strikes eligibility, no confirmation dialog) once the scan completes. CE:
/// Settings.RemoveMissingFilesOnFullScan, default false. Deliberately goes through the existing
/// two-strikes grace window rather than CE's immediate single-pass removal - see design doc.
/// </summary>
public bool AutoRemoveMissingOnScan { get; set; }

/// <summary>
/// Whether a file path recorded in the removed-files list (see RemovedFilePath) is skipped during
/// import instead of being silently re-added. CE: Settings.DontAddRemoveFiles, default false. The
/// list itself is always populated on removal regardless of this setting - see design doc.
/// </summary>
public bool DontReimportRemovedFiles { get; set; }
```

New entity, permanent (no retention trim, unlike `RemovedLibraryEntry`'s 30-day window):

```csharp
public class RemovedFilePath
{
    public int Id { get; set; }
    public string FilePath { get; set; } = "";      // unique index, case-insensitive comparison
    public DateTime RemovedAtUtc { get; set; }
}
```

Upsert semantics: removing the same path twice updates `RemovedAtUtc` rather than duplicating the
row. New migration `AddRemovedFilePath`.

## Recording removals

`LibraryDeletionHelper.RemoveIssue` (and `RemoveSeries`, which already cascades per-issue) gains an
unconditional upsert into `RemovedFilePath` for the issue's `FilePath` (skipped when null —
placeholder/fileless entries have nothing to blacklist). This is the single choke point: Library
card delete, series delete, Needs Review's duplicate-resolve, Smart List grouped-resolve, the plugin
API's `RemoveBook`, and Library Health's own Remove/bulk-remove all already call through here, so all
of them start populating the blacklist with no per-call-site changes.

## Scan-time enforcement

`LibraryFolderScanner.ScanAll` / `ImportNewFilesAsync`: when a discovered file isn't already in
`existingPaths`, additionally check `DontReimportRemovedFiles`; if on, skip the file when its path
exists in `RemovedFilePath` (case-insensitive `OrdinalIgnoreCase`, matching Windows path semantics).
No behavior change when the setting is off — the table still fills via `LibraryDeletionHelper`
either way, it's just unconsulted here.

## Auto-remove-on-scan flow

After `ScanNowCommand`'s `ScanAllAsync` completes, if `AutoRemoveMissingOnScan` is on:

1. Run the existing unscoped `LibraryHealthService.VerifyAsync()` (full-library sweep — Scan Now
   already covers every library folder, so this is the right granularity without a new overload).
2. For every issue now eligible (`FileIsMissing && MissingVerificationCount >= 2 &&
   !MissingAcknowledged` — same predicate Library Health's manual bulk-remove already uses):
   additionally check the file's drive/root is reachable right now (new
   `LibraryHealthService.IsPathRootReachable(path)` — `Directory.Exists(Path.GetPathRoot(path))`,
   true for a UNC root that resolves, false for an unplugged drive letter or unmounted share). Skip
   auto-removal for that item when the root isn't reachable — it stays sitting in Library Health's
   confirmed-missing list for manual review instead of being silently deleted; it isn't lost, just
   not auto-actioned this pass.
3. For every remaining eligible-and-reachable item: write a `RemovedLibraryEntry`, then
   `LibraryDeletionHelper.RemoveIssue` (which also upserts `RemovedFilePath` per the section above),
   one `SaveChanges` for the batch. No confirmation dialog — the user already opted in via the
   toggle, matching CE's own no-prompt behavior.
4. `ScanStatus` (the existing status text under the Scan Now button) reports how many were
   auto-removed, if any, so it's visible without being a blocking interruption.
5. Everything removed this way still appears in Library Health's Recently Removed list with Restore
   available, identical to a manual removal.

## Preferences UI

New "Scanning" group in [LibrarySection.axaml](../../../src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml),
positioned between the existing "Comic Library Folders" and "Library Health" groups — mirroring CE's
own Book Folders → Scanning ordering:

- Checkbox: "Automatically remove confirmed-missing files during Scan" → `AutoRemoveMissingOnScan`.
- Checkbox: "Don't re-add files that were manually removed" → `DontReimportRemovedFiles`.
- "Clear removed-files list" button (ghost/secondary style) — deletes every `RemovedFilePath` row.
  The v1 escape hatch for the no-management-UI non-goal above; a `TwoStepConfirm` guard (same
  component already used for other destructive Preferences actions) since it's irreversible.

## Testing

- `LibraryFolderScannerTests`: a blacklisted path is skipped when `DontReimportRemovedFiles = true`;
  imported normally when `false`; a `RemovedFilePath` row is created on removal regardless of the
  setting.
- `LibraryDeletionHelperTests`: `RemoveIssue` and `RemoveSeries` both upsert `RemovedFilePath`;
  removing the same path twice updates the timestamp, not a duplicate row; a placeholder/fileless
  issue's removal writes nothing (null `FilePath`).
- A scan-completion test (new, alongside `LibraryHealthServiceTests` or a
  `PreferencesScreenViewModelTests` addition): `AutoRemoveMissingOnScan = true` with an issue already
  at `MissingVerificationCount = 1` before the scan → after Scan Now, the issue is auto-removed and
  a `RemovedLibraryEntry` exists; `false` → the issue is left for manual review, exactly as today.
- `IsPathRootReachable`-gated case: an eligible (two-strikes) issue whose path root doesn't currently
  resolve (simulate with a drive letter that doesn't exist / an unresolvable UNC root) is *not*
  auto-removed and stays in the confirmed-missing list, even though it otherwise meets the two-
  strikes threshold; an eligible issue on a reachable root is removed as normal.
- Only the live-watch path (`LiveFolderWatchService`) never triggers this, confirmed by a test that
  a live-detected missing file does not get auto-removed regardless of the setting.
- Migration test mirroring existing patterns (e.g. `AddMissingVerificationCountAndRemovedLibraryEntryMigrationTests`)
  for the two new `AppSettings` columns + `RemovedFilePath` entity.
- Manual: turn both settings on, manually remove an issue via a Library card, confirm its path is
  blacklisted; re-run Scan Now over its folder with the file still present on disk, confirm it is
  *not* re-imported; click "Clear removed-files list", confirm the same file *is* re-imported on the
  next scan. Separately: unplug/rename a folder, run Scan Now twice with the toggle on, confirm
  auto-removal fires on the second pass (two-strikes) and the item lands in Recently Removed with
  Restore working.
