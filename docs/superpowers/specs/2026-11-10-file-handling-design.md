# File handling — design

Date: 2026-11-10. Source: "Smart features pitch 2" in `docs/Paperbunkr-Roadmap.md`, items #8 (filename pattern learning), #9 ("you already
own this" at import) and #10 (upgrade in place), plus one ComicRack CE parity item found during research (moved-file reconnect). Grilled over
two rounds with the user; every decision below was confirmed. The three dialogs were confirmed from a mockup in the visual companion.

## 1. Scope

| Part | Item | Surface |
|---|---|---|
| F1 | #9 You already own this | Import path; "N files look like issues you own" dialog; Activity Center alert |
| F2 | #10 Upgrade in place | Upgrade service + page-remap job; confirmation dialog; Library Health duplicates; Compare |
| F3 | #8 Filename rules | Rule learning on corrections; "Apply to similar files" toast + dialog; Preferences › Libraries |
| F4 | CE parity: moved-file reconnect | Import path |

F1's Upgrade choice depends on F2. F3 and F4 are independent of the others.

**ComicRack CE** (checked in `_reference/ComicRackCE`):
- No learned or user-defined filename patterns; only `EngineConfiguration.OfValues`, `LegacyFilenameParser` and the `ParseComicPath` script
  hook. F3 is a deviation.
- No duplicate check when adding (`ComicBookFactory.Create`, `ComicScanner.OnProcessScannedFile` dedupe by path only); duplicates are a view
  filter afterwards. F1 is a deviation.
- No "replace book file". `ComicBookDialog.LinkFile` only attaches a file to a fileless book. F2 is a deviation.
- **Parity gap F4 closes:** at scan time CE repoints a book whose file is missing to a new file with the same file name and size, keeping its
  data (`ComicScanner.cs:214-218`, `ComicBookCollection.FindItemByFileNameSize`). Paperbunkr has only the manual relink suggestions.

## 2. Foundations

**Code placement.** Matching, remapping and rule logic are pure functions in `Paperbunkr.Data` (next to `MissingFileMatchResolver`,
`DuplicateKeeperRanker`); file operations in `Paperbunkr.App/Services`; dialogs in `Paperbunkr.App`.

**The one import path.** Every comic import goes through `LibraryFolderScanner.ImportFiles` (`LibraryFolderScanner.cs:357`): Scan Now,
scheduled scans, plugin `ScanFolders`, live folder watch, the acquisition daemon, drag-and-drop and open-on-launch. The issue row is built at
:493-498, file stats applied at :499, added at :605, saved at :625. F1 and F4 hook in after :499 and before the add. Callers are told
apart by an import mode passed in: `Interactive` (drag-and-drop), `Unattended` (everything else), `NoChecks` (open-on-launch, which blocks
the UI thread).

**Schema** (one migration each, migration tests; hand-edit any generated `Down()` that drops an `AppSettings`/`Issues` column):

| Part | Change |
|---|---|
| F1 | None. Cover fingerprints are `PageHash` rows with `PageNumber` 0 (`Data/Entities/PageHash.cs`; `ContentStamp` "size:mtime" already invalidates them). |
| F3 | New table `FilenameRule` (`Id`, `FolderPath`, `Kind` {NumberToken, FolderSeries}, `Parameter` (token index or series id), `CreatedAt`, `FilesChanged`). |

**Files are never hard-deleted.** Every removal goes to the Recycle Bin (`RecycleBinHelper.SendToRecycleBin`), and only after the database
change is saved.

## 3. F1 — You already own this (#9)

### 3.1 Cover fingerprints

- At import, for new files only: decode page 0 at a small size and hash it with `PageHasher` (64-bit dHash, `Services/AdDetection/PageHasher.cs`).
  Budget 150 ms per file; on timeout or decode failure the step is skipped (no fingerprint, no check), so a scan is never held up.
- Stored as a `PageHash` row (IssueId, PageNumber 0, Hash, ContentStamp).
- A one-off scheduled task "Fingerprint covers" (`ScheduledTaskCatalog`, runs once then idles, re-runnable from the Automation tab)
  fingerprints existing issues without a page-0 row, with the same per-file budget, as an Activity Center job.

### 3.2 Match rule

`OwnedDuplicateMatcher.Find(newFile, candidates)` returns the owned issues that are likely the same, when either holds:
- Cover hash Hamming distance ≤ 10 and page counts within 2 (page count from the archive listing, available at import).
- The existing metadata duplicate rule (`SmartListQueryBuilder.BuildDuplicateGroups` tuple: series, format, count, number, volume, language,
  cover date) matches.

Issues marked `DuplicateAcknowledged` against each other are not matched again. **Tune during build:** check the threshold of 10 against real
pairs from the user's library and record the final value in §8.

### 3.3 What happens

- **Interactive (drag-and-drop):** files with a match are held back; the rest import normally. When the import finishes, the "N files look
  like issues you own" dialog opens on the UI thread (the drag import runs on `Task.Run`; marshal back with `Dispatcher.UIThread.Post`).
- **Unattended:** files import normally; one Activity Center alert "N new files look like issues you own" (DedupeKey per import batch) opens
  the same dialog for the newly added issues.
- **NoChecks:** nothing.

### 3.4 The dialog

`DialogCard`, Size Large. Title "N files look like issues you own", subtitle "Choose for each one. Nothing is added until you continue." (the
unattended variant: "These were added. Choose what to keep."). One row per file: new file (cover, name, pages, longest side in px, size)
and owned issue (cover, label, pages, px, size, read state and bookmark count), a verdict line from `DuplicateKeeperRanker` ("New file is the
better scan" / "Looks identical" / "Your copy is the better scan"), and a `segGroup` Skip / Keep both / Upgrade starting on the ranker's pick
(Upgrade when the new file ranks higher, Skip otherwise). Footer: Cancel, Continue (`primary`).
- **Skip:** interactive — the file is not imported; unattended — the new issue is removed from the library (file left on disk) and its path is added to the
  scanner's removed-files list (`RemovedFilePath`, checked at `LibraryFolderScanner.cs:374-414`) so later scans do not add it back.
- **Keep both:** import (or keep) it; set `DuplicateAcknowledged` on both.
- **Upgrade:** F2 with the owned issue as the survivor; interactive files are never imported as a separate issue.

## 4. F2 — Upgrade in place (#10)

### 4.1 The swap

`IssueFileUpgradeService.Upgrade(survivorIssueId, newFilePath, absorbedIssueId?)`, modelled on `MissingFileRelinker.RelinkToCandidate`
(`Services/MissingFileRelinker.cs:18-64`):
1. The survivor keeps its id, so everything keyed by IssueId stays: reading lists, collections, event memberships, ratings, tags, custom
   values, credits and character index rows, external ids, Wanted links, proposals, `ReadingEvent` history.
2. It is repointed at the new file. File-derived fields reset from the new file (`IssueFileStats.TryApply`, `PageCount`, `CoverAspectRatio`);
   cover caches invalidated (`CoverImageCache.Invalidate`); `EmbeddedDirty` set so write-back puts library metadata into the new file when
   write-back is on.
3. If the new file was already an issue (`absorbedIssueId`): its `ReadingEvent.ItemId` rows move to the survivor, the larger read position
   wins (after the remap), `OpenCount` adds up, and the absorbed issue is removed with `LibraryDeletionHelper` without deleting its file.
4. The page remap (4.2) runs; then the old file goes to the Recycle Bin.

### 4.2 Page remap

Runs as one Activity Center job after the confirmation:
1. Fingerprint every page of both files (`PageDecodeCore.DecodeSinglePage` + `PageHasher`).
2. Build a whole-book map old page → new page with `PageAligner.FindMatch` (`Services/Compare/PageAligner.cs`, window 8, max distance 12),
   walking old pages in order and carrying the running offset, as Compare does one page at a time.
3. Move page-indexed data through the map: `IssueBookmark.PageNumber`, `PageNote`, `PageClip` (page and rect; the PNG is kept), `IssuePage`
   (page type, rotation, spread position), `PageCropOverride`, `Issue.PreferredCoverPageNumber`, `Issue.LastPageRead`.
4. An item on an unmatched page moves to the nearest matched page before it (page 0 if none); the job summary lists those ("2 bookmarks
   moved to nearby pages").
5. Dropped: `PageReport`, `AdPageProposal`, the survivor's old `PageHash` rows (the new file's are rebuilt by the usual paths).

### 4.3 Entry points and confirmation

- F1's dialog (Upgrade).
- Library Health duplicate groups (`DuplicateGroupResolver`): a new action "Replace with this file, keep history" on the non-surviving member,
  next to Remove and Acknowledge.
- Compare: "Keep A" / "Keep B" (`CompareScreenViewModel.cs:538-557`) now upgrade in place instead of deleting the other issue's row and
  history: the survivor is the record added first (earliest `AddedTime`, so the most links stay attached), pointed at the file the user
  kept; the other record is absorbed (4.1 step 3). **Behaviour change; note it in the changelog.**

Each opens the confirmation first: `DialogCard`, Size Small, "Replace <label> with the new file?", body "Your reading history, rating and N
bookmarks carry over, matched to the new file's pages. The old file goes to the Recycle Bin. Your library metadata is kept and written into
the new file." (counts from the survivor), Cancel / Replace (`primary`). The Recycle Bin is the undo for the file; the database change has no
undo.

## 5. F3 — Filename rules (#8)

### 5.1 Learning

A correction is one of:
- **Number:** the issue editor (`IssuePropertiesScreenViewModel.Save`) or bulk edit saves a `Number` that differs from the issue's
  FilenameParser proposal and equals a *different* numeric token of its filename. Learns `NumberToken` = that token's index (1-based among
  the filename's numeric tokens).
- **Series:** an issue is moved to another series through `SeriesReassignmentResolver.Apply` (Needs Review, merge, reassign), and the other
  files in its folder had the same parsed series as it. Learns `FolderSeries` = the target series id.

Nothing else counts. A rule is not saved until the user accepts the offer.

### 5.2 The offer

After a correction, `FilenameRuleEngine.Preview(folder, rule)` computes which other files in the folder would change. When at least 2 would,
a toast "Use this for other files in the folder?" with a Review action opens the dialog: `DialogCard`, Size Medium, title "Apply this to N
files in the folder?", subtitle folder path and the rule in words, a table File / Number now (struck through) / Becomes (first 3 rows, then
"… N more"), a muted note about future imports and where rules are managed, footer "Only this file" (ghost) / "Apply to N files" (primary).
Apply saves the rule and applies it to those files in one save; "Only this file" saves nothing.

### 5.3 Applying at import

In `ImportFiles`, right after `ComicNameInfo.FromFilePath` (:418) and before the plugin override (`ApplyParseComicPathOverride`, :952-984):
the rule for the file's folder (exact folder match) adjusts the parsed values. Order of precedence, highest first: embedded ComicInfo (per
field, unchanged), plugin override, folder rule, the built-in parser. A rule's number becomes the same FilenameParser `MetadataProposal` the
parser makes today (`AddFilenameProposal`, :1030), so the data model gains nothing new. A `FolderSeries` rule sets the series the issue is
filed under.

### 5.4 Managing rules

Preferences › Libraries: a "Filename rules" list (folder, rule in words, files changed, Delete as `Button.iconButton` named "Delete rule").
Deleting a rule never reverts changes already made. Indexed in `PreferenceIndex`.

## 6. F4 — Moved-file reconnect (CE parity)

At the same import step, before a new issue is built: find issues flagged missing whose file name (without folder) and file size equal the
new file's. Exactly one → reconnect it with `MissingFileRelinker` semantics (keep the row and its data, repoint, clear the missing flags), no
prompt, and count it in the scan summary ("2 moved files reconnected"). None or several → import as new; the missing issues stay in Library
Health's relink suggestions.

## 7. Tests

- F1: match rule (threshold edges 10/11, page-count edges 2/3, metadata rule, acknowledged pairs excluded); cover step honours the 150 ms
  budget and skips on failure; interactive files held back and the rest imported; unattended raises one alert per batch; NoChecks does
  nothing; Skip/Keep both/Upgrade outcomes in both modes.
- F2: page map on known pairs (an extra ad page, a missing page, reordered pages); each page-indexed type moved; unmatched items moved to the
  nearest earlier page and listed; history folded in from an absorbed issue; old file recycled only after save; Compare Keep keeps history.
- F3: each correction kind learns the right rule; corrections that do not fit learn nothing; offer only when 2+ files change; precedence order
  at import; Delete keeps past changes.
- F4: exactly one match reconnects; none or several import as new.
- Dialogs: headless render in default, daylight and matrix; lint set and `avalonia-pro-max/review-checklist`.
- Migration test for `FilenameRule`.

## 8. Implementation notes

(Filled in while building: the tuned cover threshold, deviations from this spec, premises found wrong, and what was verified on screen.)
