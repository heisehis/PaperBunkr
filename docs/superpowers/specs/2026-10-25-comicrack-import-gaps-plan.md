# ComicRack Import Gaps — Implementation Plan

*Implements: docs/superpowers/specs/2026-10-25-comicrack-import-gaps-design.md*

## Step 1: CE series dates and date operators
**Files:** `src/Paperbunkr.Data/Entities/SmartListCondition.cs` (enum), `SmartLists/SmartListCatalog.cs`, `SmartLists/SeriesStatistics.cs`, `SmartLists/SmartListQueryBuilder.cs`, `SmartLists/SmartListLeafEvaluator.cs`, corresponding Data tests.
**What:** Append four date fields, calculate full-library series/volume maxima, expose them to evaluation, and make all date operators calendar-day based.
**Depends on:** none.
**Verify:** focused Smart List catalog, evaluator, series-statistics, and reader tests.

## Step 2: ComicDb smart-list preview and migration
**Files:** `src/Paperbunkr.Data/CeMigration/CeLibraryMigrator.cs`, migration preview/result records, `src/Paperbunkr.App/ViewModels/MigrationViewModel.cs`, existing migration view, corresponding Data/App tests.
**What:** Extract `<ComicLists>` separately, recursively import only smart-list rules with folder-qualified names, surface counts/warnings/collisions in preview, then save imported lists in the same migration commit.
**Depends on:** Step 1 for imported rules using new series-date fields.
**Verify:** migration fixtures/tests covering folders, unsupported matchers, collisions, preview, and persisted commit.

## Step 3: Faithful legacy `.lop` conversion
**Files:** `src/Paperbunkr.App/Scraper/Import/LopProfileReader.cs`, `LopProfileReaderTests.cs`.
**What:** Convert pre-2.0 roots and legacy metadata/token names before parsing. Preserve the plugin's Test-to-Simulate conversion and report version limitations or unsupported behavior-changing settings.
**Depends on:** none.
**Verify:** reader tests for each legacy conversion, representable settings, and rejected/annotated limits.

## Step 4: Multi-file `.lop` flow and combined result
**Files:** `src/Paperbunkr.App/Services/IFilePickerService.cs`, `FilePickerService.cs`, `Scraper/Import/LopProfileImporter.cs`, profile manager view model/view, corresponding App tests.
**What:** Add a backward-compatible multi-open picker API, import selected files independently, and present one combined, file-specific result while retaining existing suffix collision behavior.
**Depends on:** Step 3.
**Verify:** importer/view-model tests for mixed valid and invalid files and one picker invocation; build and inspect the combined-result UI.

## Step 5: Roadmap and final verification
**Files:** `docs/paperbunkr-todo.md` and any implementation files corrected by verification.
**What:** Record verified completion and update relevant test evidence.
**Depends on:** Steps 1–4.
**Verify:** focused test suites, `git diff --check`, and main-checkout Debug app build. Review Avalonia checklist items applicable to the changed migration/profile UI.
