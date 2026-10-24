# Series merge — implementation plan

*Implements: [2026-10-15-series-merge-un-numbered-issues-design.md](2026-10-15-series-merge-un-numbered-issues-design.md)*

## Step 1: Share the numbered duplicate-key rule

**Files:** `src/Paperbunkr.App/Services/SeriesMergeHelper.cs`, `src/Paperbunkr.App/ViewModels/MergeSeriesScreenViewModel.cs`

**What:** Add one internal key extractor that rejects null, empty, and whitespace effective issue numbers and otherwise returns the existing `(number, volume)` key. Use it in both the merge operation and the dialog's target/source key sets. Preserve source ordering and current cache invalidation for rows that are true numbered duplicates.

**Depends on:** none.

**Verify:** Build the app project and run the focused bulk-action tests.

## Step 2: Cover merge behavior and scan idempotence

**Files:** `src/Paperbunkr.App.Tests/LibraryBulkActionsTests.cs`

**What:** Add tests for multiple unnumbered issues surviving a merge, null/empty/whitespace number values, numbered duplicate and distinct-volume behavior, multiple source series, and dialog duplicate counts. Include a scan regression with real synthetic comic files to prove a kept unnumbered issue row prevents re-import after merge.

**Depends on:** Step 1.

**Verify:** Run the focused `LibraryBulkActionsTests` class.

## Step 3: Update the approved design status

**Files:** `docs/superpowers/specs/2026-10-15-series-merge-un-numbered-issues-design.md`

**What:** Mark implementation complete and record the commit and verification evidence after the change lands.

**Depends on:** Steps 1–2.

**Verify:** Review the final doc against the implemented behavior and actual test results.
