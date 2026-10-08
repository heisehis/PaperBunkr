# MetronInfo.xml write-back and ID import — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-05-metroninfo-write-back-design.md*

**Status:** all steps built 2026-10-05, uncommitted. Results are in `docs/paperbunkr-todo.md` under the same date.

## Deviations from the design found while surveying

- **Snapshot always includes MetronInfo content** (built with an empty id context) rather than taking an
  `includeMetronInfo` flag: the four trigger view models call `MetadataFileFieldSnapshot.Capture(issue)` with no
  settings in hand. Cost: an edit to a MetronInfo-only field (UPC/ISBN) enqueues a write even when the setting is
  off; that write is a harmless same-content ComicInfo rewrite.
- **`Prices` and `Universes` are not written** (design D3 listed prices): `Issue.BookPrice` is the user's purchase
  price, not the cover price, and universes live on `Series` continuity memberships. Both are carried over from an
  existing document (D10).
- **ID import reads `MetronInfo.xml` from `.cbz` and image folders only** — the engine has no generic "read this
  entry as type T" path for other archive kinds. Same format set the write side supports.
- **An id the file already carries is kept when we hold none for that source**, and with no primary of ours the
  file's own `id=` attributes stand. Design D5 said "no primary: omit every `id=`"; applied literally, writing a
  book we never scraped would have stripped the ids Metron-Tagger put there.
- **URLs are rebuilt from `Issue.Web` when it has a value** (the design listed URLs both as written and as carried
  over); with no `Web`, the file's own URLs are kept.
- **ID import runs on the new-file scan only**, not on Sync Metadata or a single-issue rescan.
- **`GtinType.Isbn`/`Upc` become `string`** (CE's generated copy types them `object`, which `XmlSerializer` writes
  with an `xsi:type` attribute).
- **Arcs are written from `Issue.StoryArc`**, which is what other tools expect. CE's reader (kept as-is) maps
  `Arcs[0]` to `AlternateSeries`; that only matters for a file with no `ComicInfo.xml`, which we never produce.

## Step 1: Model v1.1 + reader
**Files:** `src/Paperbunkr.Engine/MetronInfo.cs` (edit), `src/Paperbunkr.Engine/IO/Provider/XmlInfo/MetronInfoProvider.cs` (edit)
**What:** add `AlternativeNumber`, `CommunityRating` (`CommunityRatingType`), string GTIN fields, `ToArray()`/`Read`.
Reader maps `CommunityRating` and prefers `AlternativeNumber` for `AlternateNumber`.
**Verify:** parse of the schema repo's v1.1 `Sample.xml` (Data.Tests fixture string).

## Step 2: Mapper
**Files:** `src/Paperbunkr.Data/CeMigration/IssueToMetronInfoMapper.cs` (new), `MetronIdContext.cs` (new)
**What:** `Apply(Issue, MetronInfo, MetronIdContext)`; credits grouped per creator; D4–D6, D8 rules.
**Verify:** `IssueToMetronInfoMapperTests` (Data.Tests).

## Step 3: Setting + migration
**Files:** `AppSettings.cs` (edit), new migration `AddWriteMetronInfo` (no-op `Down`), `PreferencesScreenViewModel.cs`,
`AdvancedSection.axaml`
**Depends on:** none
**Verify:** Preferences persistence test; migration round-trip suite.

## Step 4: Write-back service, snapshot, queue
**Files:** `MetadataFileWriteBackService.cs`, `MetadataFileFieldSnapshot.cs`, `MetadataWriteBackQueue.cs`,
`LibraryScreenViewModel`/Preferences manual-write call sites if they pass `includeSidecar`
**Depends on:** 1, 2, 3
**Verify:** `MetadataFileWriteBackServiceTests` additions (off → untouched, on → written, carry-over, folder).

## Step 5: ID import
**Files:** `src/Paperbunkr.Data/Metadata/EmbeddedMetronIds.cs` (new), `LibraryFolderScanner.cs` (edit, additive —
the file has another session's uncommitted edits)
**Depends on:** 1
**Verify:** `EmbeddedMetronIdsTests`; a scanner test with a MetronInfo-only CBZ.

## Step 6: Wrap-up
Run Data.Tests + targeted App.Tests classes; `avalonia-pro-max/review-checklist` for the new row; update
`docs/paperbunkr-todo.md`, the design's status line, and memory.
