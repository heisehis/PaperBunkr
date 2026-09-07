# Missing Files — Library Health (Preferences) — Design

A new **Library Health** tab in Preferences (alongside Appearance/Behavior/Libraries/Advanced/
Automation) that becomes the **sole** home for missing-file review — replacing, not duplicating,
Needs Review's current "Missing Files" section — plus the new missing-file cleanup and "Recently
Removed" undo log this whole spec exists for. Built to grow into the "library health manager"
umbrella (Duplicate Files review stays in Needs Review for now; a future spec can move it in
alongside Missing Files once this lands).

Date: 2026-09-06. Status: design, pending user review (grilling round 1, a consolidation follow-up,
and a terminology check against a comparable tool — all answered in chat; this doc reflects all
three). Not yet approved for `writing-plans`.

---

## Why now (context)

Real incident, 2026-09-05→06: editing a comic's metadata triggers a `ComicInfo.xml` write-back that
rewrites the `.cbz` in place. On Windows this briefly renames the real file to
`<name>~RF<hex>.TMP` before deleting the temp copy, and `LiveFolderWatchService` was following that
rename and then losing the file — flagging genuinely-present comics `FileIsMissing = true`. Removal
in Needs Review is manual/double-confirm (`TwoStepConfirm`), not automatic, so what actually
happened is a queue full of false positives got worked through and confirmed away by hand, deleting
real library **entries** (metadata, reading progress, collection membership — never the file itself,
since `LibraryDeletionHelper.RemoveIssue` skips the Recycle-Bin step whenever `FileIsMissing` is
already true).

The root cause is already fixed and regression-tested (`4911682` "stop metadata write-back from
flagging its own comic files missing", `52b20ce` "startup self-heal for comics repointed at
`~RF*.TMP`"). This spec is not that fix — it's the actual ask that surfaced from living through it:
a trustworthy way to find and clear out comics that are *genuinely* gone, without repeating that
experience.

**Confirmed gaps, direct code check (not assumed):**

- `FileIsMissing` is set today only by: CE migration import, reading-list placeholder creation, and
  `LiveFolderWatchService`'s `Deleted` handler — which only runs for folders with `Watch = true`.
  `Watch` defaults to `false` and is opt-in per folder, so most watched folders never get this at
  all.
- Manual "Scan Now" (`LibraryFolderScanner.ScanAllAsync`) only ever discovers *new* files. Neither
  `SyncMetadata` nor `ResyncSeriesFromFile` (the other two full-library sweeps) ever flag a missing
  file either — both just skip silently on `!File.Exists(...)`, no `else` branch.
- **There is currently no code path anywhere that does a full-library `File.Exists` sweep.** This is
  the actual gap this spec closes, not a UI nicety on top of something that already runs.
- `PreferencesScreenViewModel.RemoveFolder` deletes the `WatchedFolder` row only — it never touches
  the `Issue`s that came from that folder and tears down any live watcher. A folder removed from
  Preferences (or later deleted/renamed on disk) leaves its issues silently stale forever, with
  nothing ever re-checking them. This is the "unwatched libraries that have already been scanned"
  case called out in grilling — confirmed real, not hypothetical.
- Missing Files today lives inside `NeedsReviewViewModel`, which is the same "Migration Review"
  overlay originally built for one-time CE migration review (`ActivityLinkKind.MigrationReview`).
  That's the wrong long-term home for an ongoing library-health concern — it's why the missing-files
  alert and Missing Files review both read as "migration leftovers" rather than a first-class
  feature. Per user decision, this spec **moves** Missing Files out of there entirely rather than
  building Library Health as a second, overlapping surface.

**CE-parity check (standing rule):** CE has no equivalent. Its `WatchFolder` only tracked renames to
keep `FilePath` in sync; it never detected deletions or offered any prune/cleanup action beyond the
user manually removing entries themselves. This is a deliberate Paperbunkr enhancement beyond CE,
same category as Duplicate Files review and the live-watch missing-files alert.

**Prior art check — hankscafe/omnibus (a comparable self-hosted comic/manga manager):** checked
directly against their `src/app/api/admin/diagnostics/route.ts` rather than assumed. Two findings
that shaped this spec:

- Omnibus splits this space into **"Ghost Records"** (a `Series` whose folder is gone, or an `Issue`
  with `status === 'MISSING'` — a DB entry with no backing file, i.e. exactly what this spec calls
  "missing") and **"Orphaned Files"** (the *reverse*: physical files on disk with no DB record at
  all, resolved by deleting the file itself via `delete-orphans`). **Paperbunkr does not have, and
  this spec does not build, an equivalent of Omnibus's "Orphaned Files."** That's a real, separate,
  still-unbuilt feature (untracked files in a watched folder Paperbunkr never imported) — not
  something to conflate with this spec's "missing" concept. Deliberately avoiding the word "orphan"
  throughout this doc for that reason, so the term stays free for that future feature.
- Omnibus's `delete-ghosts` and `delete-orphans` are both **permanent, no undo** — `prisma.issue
  .deleteMany` / `fs.remove(p)` outright, with only an audit-log entry (who ran it, when) as a
  paper trail. No grace window before something is eligible, no recycle/restore mechanism. This
  spec's two-strikes Verify threshold and Recently Removed/Restore log are real safety
  improvements over the comparable tool, confirmed here so a future reader doesn't mistake them for
  overbuilt caution.
- One thing Omnibus does that this spec doesn't need: before deleting a physical file, it
  re-resolves the path and rejects anything outside a configured library root — defense-in-depth
  against a corrupted DB row causing a delete outside the library. Not applicable to v1 here (Verify
  never deletes a file, only a DB row), but worth carrying into any future feature that does delete
  files directly (a true "Orphaned Files" scan, per the point above).

---

## Goals

- One authoritative, full-library **Verify** pass — live `File.Exists` over every non-placeholder
  issue, regardless of source folder or `Watch` setting — as the primary detection mechanism.
  Replaces "hope the live watcher caught it" with "we actually checked."
- Reuse `Issue.FileIsMissing` as the single source of truth (per decision: the missing flag is the
  main priority here) — Verify sets and clears it; no parallel flag invented.
- A grace window: an issue must come up missing across **two independent Verify passes**, not one,
  before it's eligible for the *bulk* cleanup action — protects against a disconnected external
  drive, an unmounted network share, or a cloud folder mid-sync.
- **Consolidate all missing-file review into the new Preferences → Library Health tab**, removing it
  from Needs Review entirely — one place to Relink, Remove, or Dismiss a missing file, and one place
  to run Verify and bulk-clean up what's confirmed missing. No duplicated UI across two surfaces.
- A durable **Recently Removed** log (30 days) covering every removal made from Library Health
  (single-item or bulk), with a Restore action — the direct answer to "don't let this nuke my
  library again without a way back."
- Close the unwatched/removed-folder gap: Verify sweeps the whole library unconditionally, and
  removing a `WatchedFolder` triggers a scoped Verify over just that folder's issues first, so they
  land correctly in the pipeline instead of going silently stale.

## Non-goals (v1)

- No automatic/scheduled Verify by default. Manual "Verify Now" only. (The maintenance scheduler
  that shipped today, 2026-09-06, is a natural future home for this — an opt-in `ScheduledTask` —
  but defaulting a data-affecting sweep to run automatically right after the trust hit this spec
  exists because of would be the wrong call. Follow-up, not v1.)
- No content-hash / integrity verification (corrupt-but-present files) — Omnibus calls this
  "Archive Integrity," a separate scan in their diagnostics suite. Reasonable future module for the
  same Library Health tab, not this spec.
- No true "orphaned files" scan (untracked files on disk with no DB record) — see the prior-art
  note above. Separate feature, separate spec, if ever built.
- No special-casing for network paths / removable drives beyond the two-strikes grace window.
- Needs Review's other three sections — Duplicate Files, Content Type, Series Conflicts, Metadata
  Proposals — are untouched. Only Missing Files moves. Duplicate Files joining Library Health later
  is a separate future spec, not this one.

---

## Consolidation: Missing Files moves out of Needs Review

`NeedsReviewViewModel` currently owns `MissingFileItems`, `RefreshMissingFileItems`,
`RelinkMissingFile`, `RemoveMissingFile`, `DismissMissingFile`, and folds `HasMissingFileItems` into
`HasPendingItems`. All of it moves to the new Library Health view model:

- `HasPendingItems` becomes `HasContentTypeItems || HasSeriesConflictItems ||
  HasMetadataProposalItems || HasDuplicateFileItems` (Missing Files term dropped).
- `MissingFileRowViewModel` (Relink / `TwoStepConfirm`-gated Remove / Dismiss, per item) is reused
  as-is inside Library Health — same component, new host. This is **not** gated by the two-strikes
  count: a user who already knows a specific file is gone can still Relink/Remove/Dismiss it
  immediately, exactly like today. Two-strikes only gates the new *bulk* "Remove All Confirmed
  Missing" action below.
- Two alert call sites in `MainViewModel.cs` currently point at `ActivityLinkKind.MigrationReview`
  and need to point at Library Health instead:
  - `LiveFolderWatch`'s `onFilesMissing` alert ("Missing files detected").
  - `LibraryPathRepairService`'s startup self-heal alert, when `NeedsManualReview > 0`.
  Both change to `new ActivityLink(ActivityLinkKind.Preferences, "LibraryHealth")` — the exact
  mechanism `ActivityCenterViewModel.cs:113` already uses to deep-link into the Automation tab
  (`new ActivityLink(ActivityLinkKind.Preferences, "Automation")`), so no new `ActivityLinkKind`
  value is needed, just a new payload string handled the same way Automation's is.
- `DuplicateAlertHelper`'s alert keeps linking to `ActivityLinkKind.MigrationReview` unchanged —
  Duplicate Files isn't moving in this pass.

## Data model

Two additions, same migration-shape conventions as `MissingAcknowledged`/`DuplicateAcknowledged`:

```csharp
// Issue
public int MissingVerificationCount { get; set; }  // default 0
```

Incremented on every Verify pass where the file is still absent; reset to `0` the moment a Verify
pass finds it present again (including via Relink, which already sets `FileIsMissing = false`).
Confirmed-missing, eligible for the **bulk** action = `FileIsMissing && MissingVerificationCount >=
2 && !MissingAcknowledged` — reusing `MissingAcknowledged` means a Dismiss suppresses the item from
both the general missing-files list and the bulk-eligible subset with one flag, not two.

```csharp
public class RemovedLibraryEntry
{
    public int Id { get; set; }
    public string SeriesName { get; set; } = "";
    public int? SeriesId { get; set; }              // still-existing Series, if any, for Restore
    public string? Number { get; set; }
    public string? Volume { get; set; }
    public string? Title { get; set; }
    public string? FilePath { get; set; }
    public DateTime RemovedAtUtc { get; set; }
    public RemovedLibraryEntryReason Reason { get; set; }  // MissingFileCleanup (v1's only value;
                                                            // enum left open for Duplicate/other
                                                            // reasons to reuse this table later)
}
```

Written once per removed issue — for **every** removal Library Health performs, single-item or
bulk — inside the same transaction, immediately before `LibraryDeletionHelper.RemoveIssue`.
Retention: a background trim (same pattern as `ActivityHistoryStore`'s retention and the cover-cache
attic's 14-day sweep) deletes entries older than 30 days. New migration
`AddMissingVerificationCountAndRemovedLibraryEntry`.

**Restore semantics:** recreates the `Issue` row (linking to `SeriesId` if that series still exists,
else recreating it by `SeriesName`) with `FileIsMissing = true` and `MissingVerificationCount = 0` —
it comes back into Library Health's missing-files list, not silently as "fixed," since the file is
still actually absent. If the user later finds/replaces the file, Relink handles it exactly as it
does today. Restore does **not** attempt to recover reading progress / collection membership /
reading-list entries — those cross-references were removed by `LibraryDeletionHelper.RemoveIssue`
and are not snapshotted (kept v1-minimal, per the non-goals above; flagged here as a known
limitation, not hidden).

## Verify algorithm

New `LibraryHealthService.VerifyAsync(progress, ct)` (`src/Paperbunkr.App/Services`), same
`Task.Run` + `IProgress<(int,int)>` + one-bad-file-doesn't-stop-batch contract as
`LibraryFolderScanner.SyncMetadataAsync`:

```csharp
var issues = context.Issues.Where(i => !i.IsPlaceholder && i.FilePath != null).ToList();
foreach (var issue in issues)
{
    bool exists = File.Exists(issue.FilePath);
    issue.FileIsMissing = !exists;
    issue.MissingVerificationCount = exists ? 0 : issue.MissingVerificationCount + 1;
}
context.SaveChanges();
```

Returns `(Checked, MissingNow, ConfirmedMissingCount)` counts for the summary cards. Scoped overload
`VerifyAsync(IReadOnlyCollection<int> issueIds, ...)` for the folder-removal hook below — same body,
filtered `.Where(i => issueIds.Contains(i.Id))`.

**Folder-removal hook.** `PreferencesScreenViewModel.RemoveFolder` currently does nothing to that
folder's issues. Change: before removing the `WatchedFolder` row, look up every `Issue` whose
`FilePath` starts with that folder's path and run the scoped `VerifyAsync` over just those — so if
the folder (and its files) are genuinely gone, they're flagged and counted immediately rather than
waiting for the user to notice and run a full Verify later. If the folder is still present on disk
(user just stopped tracking it, files still there), this is a no-op confirmation pass, which is
correct and cheap.

## Preferences → Library Health (new tab)

Same tab-list pattern as the Automation tab added today (`PreferencesSection.LibraryHealth`,
`LibraryHealthSection.axaml` + `.axaml.cs`, nav entry in `PreferencesScreen.axaml`).

- **Summary row**: issues checked, currently missing, confirmed missing (bulk-eligible), last
  verified timestamp ("Never" until the first run).
- **Verify Now** button — blocking progress bar (`ProgressToastRequested`, same class as Scan Now;
  this is an attended action the user explicitly triggered, unlike the live watcher's quiet toast).
- **Missing files list** — every issue with `FileIsMissing && !MissingAcknowledged`, ported directly
  from today's Needs Review Missing Files section: series/issue label, path, per-item Relink /
  `TwoStepConfirm`-gated Remove / Dismiss. Rows that have hit the two-strikes threshold carry a
  "confirmed missing" badge and are the ones the bulk action below acts on — everything else is
  still fully manually actionable, exactly as it is today.
- **Remove All Confirmed Missing** button, disabled when no row is badge-eligible → opens a
  confirmation dialog (new `ConfirmedMissingCleanupConfirmDialog`) listing every badge-eligible item
  by series/issue/path that will be removed, with counts, and only proceeds on explicit confirm. On
  confirm: for each item, write a `RemovedLibraryEntry` then `LibraryDeletionHelper.RemoveIssue`, one
  `SaveChanges` for the whole batch, then refresh.
- **Recently Removed** section (same tab, below the fold) — list of `RemovedLibraryEntry` rows from
  the last 30 days (series/issue, path, removed-when), each with a **Restore** button per the
  semantics above. Populated by single-item Remove and bulk Remove All Confirmed Missing alike.

## Testing

- `LibraryHealthServiceTests`: `Verify_FlagsMissingFile_IncrementsCount`,
  `Verify_ClearsFlagAndResetsCount_WhenFileReappears`,
  `Verify_RespectsMissingAcknowledged_ExcludesFromEligible`, scoped-overload variant for the
  folder-removal hook.
- Migration test mirroring `MissingAcknowledgedTests` for `MissingVerificationCount` +
  `RemovedLibraryEntry`.
- `PreferencesScreenViewModelTests`: `RemoveFolder_RunsScopedVerify_OnThatFoldersIssues`.
- `LibraryHealthViewModelTests` (new, replacing the Missing Files cases removed from
  `NeedsReviewViewModelTests`): Relink/Remove/Dismiss per item behave identically to the old Needs
  Review tests; bulk-remove writes one `RemovedLibraryEntry` per issue before deleting it and only
  touches badge-eligible rows; Restore recreates the `Issue` with `FileIsMissing = true`.
- `NeedsReviewViewModelTests`: remove the Missing Files test cases (moved), confirm
  `HasPendingItems` no longer references it.
- `MainViewModelTests` (or equivalent alert tests): both relocated alert call sites link to
  `ActivityLink(ActivityLinkKind.Preferences, "LibraryHealth")`, not `MigrationReview`.
- Manual: unplug/rename a folder with issues in it, run Verify twice (confirming the two-strikes
  gate — first run shows "missing" but not yet badge-eligible), confirm the summary dialog lists the
  right items, Remove All Confirmed Missing, confirm they land in Recently Removed, Restore one and
  confirm it reappears in Library Health's missing-files list (not Needs Review). Confirm a
  live-watched file going missing raises an alert that opens Preferences on the Library Health tab.
