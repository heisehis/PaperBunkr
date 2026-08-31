# CE migration: prefer embedded file metadata over CE's cached fields — design

2026-08-31

## Problem

Confirmed via a real user library: `CeLibraryMigrator` groups/maps every book using
`ComicBook.Series` (and every other `ComicInfo`-shared field) straight from CE's own
`ComicDb.xml` export — i.e. whatever CE's database happened to have cached for that book the
last time CE scanned it. That cached value can be stale relative to what's actually embedded in
the file today.

Real reproduction: the user's CE database had `Series="Warhammer 40"` cached for ~51 books across
what are actually ~15 distinct mini-series (each with its own embedded `ComicInfo.xml` `Series`
value like "Warhammer 40,000: Will of Iron", confirmed directly from CE's own file-properties
dialog, which re-reads the file rather than trusting CE's cached row). Migration inherited the
stale generic value; a later real folder scan of the *same files* (`LibraryFolderScanner`, which
always reads embedded metadata fresh) correctly split them apart — proving the files were never
the problem, only the migration path's blind trust in CE's cached `Series` string.

This isn't Series-specific — every field `CeLibraryMigrator.MapStoryFields` maps (Title, Number,
Writer, Publisher, ...) has the same exposure: CE's cached row, not the file's current embedded
truth.

## Fix

**Principle:** during migration, when a book's file is reachable on disk, re-read its embedded
`ComicInfo.xml` and treat it as authoritative for every `ComicInfo`-shared field — the exact same
"embedded wins" rule `LibraryFolderScanner` already applies to fresh scans. Fall back to CE's
cached `ComicBook` fields only when the file can't be read (moved, deleted, or migration is
running without access to the actual comic files — a real, supported scenario, not an edge case
to break on).

**Shared reader, not duplicated logic.** `LibraryFolderScanner.TryReadEmbeddedInfo` already does
exactly this file-open-and-read (`Providers.Readers.CreateSourceProvider` → cast to
`IInfoStorage` → `LoadInfo(InfoLoadingMethod.Complete)`, swallowing failures to `null`) but it's
`private` in `Paperbunkr.App`, unreachable from `Paperbunkr.Data` (where `CeLibraryMigrator`
lives — `Data` already references `Paperbunkr.Engine` for `ComicBook`/`ComicInfo` themselves, so
this isn't a new dependency). It's extracted into a new
`Paperbunkr.Data.Metadata.EmbeddedComicInfoReader.TryRead(string filePath) -> ComicInfo?` —
`Data.Metadata` because it's a small, pure, side-effect-free resolver alongside
`SeriesReassignmentResolver`/`CharacterResolver`, not because it's migration-specific (App's
scanner uses it too). `LibraryFolderScanner`'s two call sites become thin calls into this shared
method; its own `TryReadEmbeddedInfo` is deleted, not kept as a redundant wrapper.

**Per-book effective info, computed once.** `ComicBook` already *is* a `ComicInfo` (`ComicInfo`
is its base class — confirmed by `MapStoryFields(ComicInfo info, ...)` already accepting a `book`
argument directly). For each book, migration computes an "effective info source":

```
EffectiveInfo = (book.FilePath reachable && EmbeddedComicInfoReader.TryRead(book.FilePath) succeeds)
    ? that freshly-read ComicInfo
    : book itself (today's behavior, unchanged)
```

This is computed **once per book**, then used for both the series-grouping key
(`EffectiveInfo.Series`, same "blank → Unknown" fallback `GroupBySeries` already applies) and the
actual field mapping (`MapStoryFields(EffectiveInfo, issue)` instead of always
`MapStoryFields(book, issue)`). The seven CE-database-only runtime fields `MapIssue` sets after
`MapStoryFields` (`FilePath`, `AddedTime`, `ReleasedTime`, `OpenedTime`, `LastPageRead`,
`FileIsMissing`, `CustomThumbnailKey`, `IsFinalIssue`) keep coming from `book` unconditionally —
they have no embedded-file equivalent (reading progress and added-time aren't comic metadata),
so there's nothing to prefer between.

**Both `Preview` and `Migrate` get the fix**, not just `Migrate` — `GroupBySeries` is shared
between them, and letting `Preview`'s series/issue counts diverge from what `Migrate` actually
produces would be a new, confusing inconsistency of its own.

**Performance/UX consequence, accepted deliberately:** this makes migration do real per-book file
I/O (opening each archive far enough to read its `ComicInfo.xml` entry — not a full extract, but
not free either) where it previously did none. `Migrate` is already invoked via `Task.Run` with
an `IsBusy` flag (`MigrationViewModel.Commit`). `Preview` currently is **not** — `Scan()`
(`MigrationViewModel.cs:158`) calls it synchronously on the UI thread. `Scan()` is changed to the
same `Task.Run`-wrapped async-command shape `Commit()`/`ScanNow` already use elsewhere in this
codebase, so a large library doesn't freeze the UI during Preview either.

## Testing

- `EmbeddedComicInfoReaderTests` (new, mirrors the existing `TryReadEmbeddedInfo` coverage
  implicit in `LibraryFolderScannerTests`): reads a real `CbzFixture`-built file with embedded
  `ComicInfo.xml` correctly; returns `null` for a missing file, an unsupported format, and a
  malformed embedded XML (no throw).
- `CeLibraryMigratorTests` (extend the existing file): a book whose `ComicBook.Series` disagrees
  with its *file's* embedded `Series` migrates under the **embedded** name, not CE's cached one —
  the direct regression test for the Warhammer case. A book whose `FilePath` doesn't exist on
  disk falls back to `ComicBook.Series` unchanged (today's behavior preserved). A book with no
  `FilePath` at all (CE allows unlinked entries) also falls back cleanly, no throw. `Preview`'s
  reported series/issue counts match what a subsequent `Migrate` on the same database actually
  creates, for a case where embedded metadata disagrees with CE's cache.
- `LibraryFolderScannerTests`: existing suite must stay green unchanged — confirms the
  `TryReadEmbeddedInfo` → `EmbeddedComicInfoReader.TryRead` extraction was behavior-preserving.
- No existing `MigrationViewModelTests` file today; `Scan()`'s async-command conversion is a
  behavior-preserving mechanical change (same body, wrapped in `Task.Run`) so it rides on the
  existing `CeLibraryMigratorTests` coverage of `Preview` itself rather than needing new
  ViewModel-level tests just for the threading change.

## Out of scope

- No change to how a *live* Paperbunkr scan (`LibraryFolderScanner`) already handles embedded
  metadata — it already does this correctly; this fix only closes the gap on the migration path.
- No UI to preview/confirm per-book embedded-vs-cached differences before migrating — matching
  this migrator's existing philosophy (conflicts are surfaced via the existing fuzzy-match
  `SeriesConflict` flow, not a new per-field diff review).
- `SeriesSplitDetector`/"Split Mismatched Series" (this session's earlier attempt) is dropped
  entirely — this fix addresses the actual root cause instead of compensating for it after the
  fact with title-based heuristics.
