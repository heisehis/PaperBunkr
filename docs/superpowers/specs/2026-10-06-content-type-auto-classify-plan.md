# Content-type auto-classify pipeline - implementation plan
*Implements: docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md*

Tests: `Paperbunkr.Data.Tests` for everything pure/DB (in-memory SQLite like the neighbouring tests), `Paperbunkr.App.Tests --filter "Speed!=Slow"` for
view models; never a live provider call. Edit shared files surgically - the working tree carries another session's uncommitted Metron work in
`AppSettings.cs`, `PaperbunkrDbContext.cs`, the model snapshot, `LibraryFolderScanner.cs`, `ScheduledTaskCatalog.cs`, `PreferencesScreenViewModel.cs`.

## Stage 1 - core
1. **Entities + migration.** `Entities/ContentTypeSource.cs`, `ContentTypeCheck.cs` (new); `Series.cs`, `AppSettings.cs` (edit); `PaperbunkrDbContext.cs` (enum string conversions);
   `dotnet ef migrations add AddContentTypeProvenance` + backfill SQL (lock non-Unknown rows) + no-op `Down()`. Verify: a migration test that the backfill locks existing rows.
2. **Provider signals.** `ExternalMediaMetadata.CountryOfOrigin` / `OriginalLanguage`; AniList query + DTO + normalizer; MangaDex DTO + normalizer. Verify: normalizer tests on literal DTOs.
3. **Pure core.** `Metadata/ProviderContentTypeMapper.cs`, `Metadata/ContentTypeDecision.cs`. Verify: table-driven tests (every mapping row, agree/conflict/corroborate/queue/no-match cases).
4. **Editor + service.** `Metadata/SeriesContentTypeEditor.cs` (`SetManual`, `ApplyAuto`, `Accept`, `Keep`, `Skip`, `Undo`, `RecheckPublisherClassified`),
   `Metadata/ContentTypeClassificationService.cs` (skip gate, provider loop through a small `IContentTypeEvidenceSource` seam, budgeted batch). Verify: DB-backed tests with fake sources.
5. **Wire existing writers.** `LibraryScreenViewModel.cs`, `.SelectionActions.cs`, `DetailScreenViewModel.cs`, `MangaDetailScreenViewModel.cs` -> `SetManual`;
   `LibraryFolderScanner.cs` stamps `Embedded/Language/Publisher`; `RunContentTypeSweepCore` skips locked rows and stamps `Publisher`. Verify: scanner + sweep tests (a locked Unknown survives a publisher match).

## Stage 2 - entry points
6. `ScheduledTaskCatalog.cs` new `ContentTypeTrackerClassify` task (Network, off by default, 30 d, budget 150); `PreferencesScreenViewModel` + `AutomationSection`/behavior toggle for
   `AskBeforeClassifying` + "Classify library" button; "Classify selected" via `LibraryActionCatalog`. Verify: catalog/preferences view-model tests.

## Stage 3 - queue UI
7. `NeedsReviewViewModel.cs` + `SeriesReviewItem.cs` + `Views/Preferences/LibraryHealth*` Content-type section: table rows that expand to cards, four row actions, Accept-all-high-confidence
   (TwoStepConfirm), Recently auto-classified + Undo, Re-check button, Activity Center summary alert; Detail header Undo. Verify: `NeedsReviewViewModelTests`, headless render, `avalonia-pro-max/review-checklist`.
8. Docs: `paperbunkr-todo.md` entry (status + what was verified), roadmap line.
