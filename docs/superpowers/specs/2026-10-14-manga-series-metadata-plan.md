# Manga series-centric metadata (Slices 1–3) — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md*

Working location: main checkout, `master` (D12). The tree has ~214 uncommitted files from other work — touch only the files named here, never stage or revert others. Never start a second `dotnet build/test` while one runs; kill build servers afterwards. Targeted test classes only (no wide App runs).

## Step 1: Entities, columns, migration
**Files:** `Paperbunkr.Data/Entities/{SeriesCredit,SeriesTag,IssueFieldOverride,IssueMetadataSnapshot,EmbeddedSyncState}.cs` (new); `Series.cs`, `Issue.cs`, `MetadataProposalField.cs` (edit: append `Publisher, Imprint, AgeRating`), `PaperbunkrDbContext.cs` (DbSets + config); `Migrations/<ts>_AddMangaSeriesMetadata.cs` + Designer + snapshot (generated); `Paperbunkr.Data.Tests/AddMangaSeriesMetadataMigrationTests.cs` (new).
**What:** `Series`: `Imprint`, `AgeRating`, `EmbeddedSyncEnabled` (default true), `LockedFields` (CSV), `CanonicalReviewVersion`. `Issue`: `EmbeddedDirty`, `EmbeddedFingerprint`, `EmbeddedFileStamp`, `EmbeddedSyncState` (default None), `EmbeddedSyncMessage`. New tables per spec §4; unique `(IssueId, FieldKey)`; FK cascades. Generate the migration with `dotnet ef`, then check the Designer for the stale-nullability trap (memory: `IsFinalIssue`) and run the migration test.
**Depends on:** none
**Verify:** `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~AddMangaSeriesMetadataMigration"` plus `~Migration` neighbours.

## Step 2: Field registry
**Files:** `Paperbunkr.Data/Metadata/Manga/MangaInheritedFields.cs` (new), `Paperbunkr.Data.Tests/MangaInheritedFieldsTests.cs` (new).
**What:** One typed registry of the D10 fields (Publisher, Imprint, Writer, Penciller, Inker, Colorist, Letterer, CoverArtist, Editor, AgeRating, Genre tags, Tags): key, how to read the series value, how to read/write the issue value (string column vs `IssueTag` set), whether it is a credit role. Used by the editor, fan-out, overrides, markers, seeder.
**Depends on:** Step 1
**Verify:** unit tests (round-trip each field on a stub issue).

## Step 3: Canonical save + fan-out service
**Files:** `Paperbunkr.Data/Metadata/Manga/MangaSeriesMetadataService.cs`, `MangaSeriesMetadataEdit.cs` (new); tests `MangaSeriesMetadataServiceTests.cs`.
**What:** `Load(seriesId)` (canonical values + seeder results), `PreviewExceptions(edit)` (count of chapters that differ per field), `Save(edit, exceptionChoice)` in one transaction: update series fields, replace `SeriesCredit`/`SeriesTag`, lock changed fields, take `IssueMetadataSnapshot` once per issue, fan out to non-overridden issues (set-based `ExecuteUpdate` for string columns; tag rows per issue), Keep-as-exceptions writes override rows, `CreatorResolver.SyncFromIssue` for changed issues, record manual `MetadataProposal`s (Publisher/Imprint/AgeRating/Summary...), set `EmbeddedDirty` on changed issues of series with `EmbeddedSyncEnabled`. Returns changed issue ids. Unchanged save writes nothing.
**Depends on:** Step 2
**Verify:** tests for spec §8 (100-chapter fan-out, untouched fields, exceptions Keep/Replace, unchanged save, credits and tags, snapshot written once).

## Step 4: Overrides, seeder, new-chapter inheritance, reclassify
**Files:** `Paperbunkr.Data/Metadata/Manga/{MangaChapterOverrideService,SeriesCanonicalSeeder,MangaInheritanceApplier,MangaReclassifySnapshot}.cs` (new); `Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit: new issue in manga-family series gets series values; rescan never overwrites a non-overridden inherited field); `Paperbunkr.Data/Library/SeriesContentTypeEditor.cs` (edit: Manga→Comic restores from snapshot; Undo intact); tests.
**What:** `SetOverride/ResetOverride(issueId, key)`; seeder (agree / disagree → candidates + Needs Review, writes nothing); applier used by scanner; restore-from-snapshot on reclassify.
**Depends on:** Step 3
**Verify:** `MangaChapterOverrideServiceTests`, `SeriesCanonicalSeederTests`, scanner test for new chapter + rescan, reclassify round-trip test.

## Step 5: Issue editors create/clear overrides
**Files:** `Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs`, `BulkIssuePropertiesScreenViewModel.cs` (edit); tests in the existing editor test classes.
**What:** On Save for a manga-family series, each registry field whose saved value differs from the series value gets an override row; equal removes it. Fields show "from series" / "overridden" state and a **Reset to series** command (single editor only; bulk sets overrides silently).
**Depends on:** Steps 3–4
**Verify:** `IssuePropertiesScreenViewModelTests`-style tests; Comic path unchanged (regression).

## Step 6: Slice 2 — fingerprint, durable queue, sync states
**Files:** `Paperbunkr.App/Services/{EmbeddedMetadataFingerprint,EmbeddedSyncSweeper}.cs` (new); `MetadataFileWriteBackService.cs`, `MetadataWriteBackQueue.cs` (edit); `MainViewModel.cs` (start sweep after startup); tests `EmbeddedMetadataFingerprintTests`, `MetadataWriteBackQueueTests` additions, `EmbeddedSyncSweeperTests`.
**What:** Fingerprint = SHA-256 of mapper output (+ sidecar/MetronInfo flags, + series-Summary fallback for manga-family). Writer: skip when fingerprint+stamp current (clears dirty), set state/message/fingerprint/stamp/dirty on every outcome, detect external modification (stamp mismatch → `ConflictExternalModification`, no overwrite unless `manual`), honour `Series.EmbeddedSyncEnabled`. Queue: retry with 5 s/30 s/5 min then `NeedsAttention`; one `IActivityService` job per batch with progress; failure alerts with Retry. Sweeper: enqueue only `EmbeddedDirty` issues at startup; delete leftover `.pbwrite-*.tmp`. Actions: Retry failed, Write anyway, Verify embedded metadata (read-only).
**Depends on:** Step 1 (Step 3 sets dirty)
**Verify:** spec §12 sync tests on temp CBZ files and throwaway DBs.

## Step 7: Edit Series overlay (Slice 1 UI) + Edit button replaced
**Files:** `Paperbunkr.App/ViewModels/MangaSeriesEditorViewModel.cs`, `Views/MangaSeriesEditorOverlay.axaml` + `.axaml.cs` (new); `MangaDetailScreenViewModel.cs` (Edit → `EditSeries`, label "Edit Series", always enabled for local series; remove `_continueIssueId` coupling), `MangaDetailContextMenuBuilder.cs` (add Edit Series), `MainViewModel.cs` + `MainWindow.axaml` (overlay open flag, `OverlayShell`, reload after save, unsaved-changes guard like Issue Properties), `Services/Input` registration for Save/Escape through `AttachedInputRegistration`; tests.
**What:** Sections Identity & Titles, Creators, Publication, Classification, Description & Artwork, Sources (Step 9), Advanced (Step 6 actions + per-series sync switch). Save shows the exceptions prompt when needed. Overlay code-behind ships in the same step as the `.axaml` (AVLN2000 rule). Deferred collection changes use `Dispatcher.UIThread.Post`. Load the `avalonia` skill + subskills and run `avalonia-pro-max/review-checklist` before calling UI done.
**Depends on:** Steps 3–6
**Verify:** VM tests (command works with no continue chapter, hidden/disabled for remote, Comic Edit unchanged); keyboard test in a real `MainWindow` for Escape/Save; build then launch check of XAML weave.

## Step 8: Manga hero/band/details read series values + Needs Review chip
**Files:** `MangaDetailScreenViewModel.cs`, `DetailBandViewModel.cs` (edit), `DetailTabsViewModel.cs` (edit: manga host takes credits/details from series, not issue aggregates), `Views/DetailBand.axaml`, chapter-row "from series" marker where cheap.
**Depends on:** Steps 3, 4
**Verify:** `DetailScreenViewModelTests`/manga tests; Comic regression.

## Step 9: Slice 3 — locks, provider candidates, Sources section
**Files:** `Paperbunkr.Data/Metadata/MetadataLinkResolver.cs` (edit `ProposeAndApply`: locked field → `Pending`, not applied), `ExternalTagImportResolver.cs` (edit: manga-family merges into `SeriesTag` + non-overridden issues), `Paperbunkr.App/ViewModels/MangaSeriesSourcesViewModel.cs` (new), Sources section in the overlay; tests.
**What:** Linked providers list, Refresh from source (existing fetch), comparison rows with Accept / Reject, Creator accept with chosen role, lock markers/Unlock.
**Depends on:** Steps 3, 7
**Verify:** spec §12 sources/locks tests, including non-manga auto-apply regression.

## Step 10: Docs and close-out
**Files:** `docs/paperbunkr-todo.md`, `docs/onboarding.md` (short section), memory note.
**What:** Record status with what was verified (build, test classes run, what was not run on screen). Kill build servers.
**Depends on:** all
