# CBL Smart Match healing and safe refresh — Implementation Plan

Implements: [2026-10-25-cbl-smart-match-healing-refresh-design.md](2026-10-25-cbl-smart-match-healing-refresh-design.md)

## As-built record — 2026-10-25

- Implemented all three steps in the working tree: arc-specific refresh planning, atomic refresh integration, and identity-change healing hooks.
- Main Debug app build passed: `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj -c Debug` (0 errors; existing ImageSharp license warning).
- `git diff --check` passed. Focused tests were not run for this delivery.
- The implementation adds no views or XAML; no native visual review applies.

## Step 1: Arc-specific refresh planner

**Files:** `src/Paperbunkr.Data/ReadingLists/ArcReadingListRefreshPlanner.cs` (new), `src/Paperbunkr.Data/ReadingLists/ArcReadingListBuilder.cs`, `src/Paperbunkr.Data.Tests/ArcReadingListBuilderTests.cs`.

**What:** Pair old rows to current provider entries by stable source-entry ID or strict source reference; plan new targets while preserving matching row IDs and annotations; retain hand-added and removed-but-user-linked rows at the end; collapse source targets that duplicate a hand-added issue; remove only source-owned rows that disappeared. Clean up unreferenced placeholders safely. Do not alter the shared Continuity reconciler.

**Depends on:** none.

**Verify:** Implemented. Focused arc-refresh tests were not run for this delivery.

## Step 2: Atomic refresh integration and failure reporting

**Files:** `src/Paperbunkr.Data/ReadingLists/ArcReadingListBuilder.cs`, `src/Paperbunkr.Data/ReadingLists/ArcReadingListRefreshPlanner.cs`, `src/Paperbunkr.Data.Tests/ArcReadingListBuilderTests.cs`.

**What:** Fetch source data before the transaction; reject skipped or count-incomplete responses without database writes. Reload and validate the current list after the fetch. Wrap healing, resolution/materialization, planning, row changes, event recording and final save in one transaction. Return counts for retained local rows and refresh changes through `ArcRefreshResult` and the existing status message.

**Depends on:** Step 1.

**Verify:** Implemented. These focused tests and Continuity reconciliation tests were not run for this delivery.

## Step 3: Identity-change healing hook and operation wiring

**Files:** `src/Paperbunkr.App/Services/ReadingListHealingCoordinator.cs` (new), `src/Paperbunkr.Data/ReadingLists/Matching/ReadingListHealer.cs` as needed, `src/Paperbunkr.App/Scraper/ScrapeCoordinator.cs`, `src/Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs`, `src/Paperbunkr.App/ViewModels/BulkIssuePropertiesScreenViewModel.cs`, `src/Paperbunkr.App/ViewModels/NeedsReviewViewModel.cs`, `src/Paperbunkr.App/Services/MetadataEditHistoryService.cs`, `src/Paperbunkr.App/ViewModels/MainViewModel.cs`, plus corresponding focused App tests.

**What:** Capture/compare matching identity around persisted operations and invoke `ReadingListHealer.RecheckFor` once per committed operation for the affected issue IDs (including all affected issues for a series rename). Cover applied scrapes, direct/bulk edits, accepted series/identity proposals, and issue metadata undo/redo. Keep failed/no-op operations from triggering rechecks. Add one healing summary to the existing operation feedback.

**Depends on:** none; can be developed after Step 2 to keep test runs serial and the shared database code isolated.

**Verify:** Implemented. Focused identity-trigger tests were not run for this delivery.

## Step 4: Integrated verification and roadmap update

**Files:** `docs/paperbunkr-todo.md`, approved design and plan documents; any files exposed by focused failures.

**What:** Run the focused Data and App suites serially; run the main app Debug build after the final code change; inspect compiled XAML only if the implementation changes views; run `git diff --check`; update item 1's roadmap note with the actual verified scope and leave items 2–6 queued.

**Depends on:** Steps 1–3.

**Verify:** Main Debug app build passed with 0 errors; `git diff --check` passed. No focused tests, full suite, or native accessibility review are claimed.
