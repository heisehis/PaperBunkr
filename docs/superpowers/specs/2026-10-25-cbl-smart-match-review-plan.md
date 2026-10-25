# CBL Smart Match review — implementation plan

Implements: [approved design](2026-10-25-cbl-smart-match-review-design.md).

1. Add Data `ReadingLists/Matching/ReadingListReviewService.cs` and persistent batch entities/configuration/migration. Read-only review snapshots, strict equivalence, transactional guarded Apply and partial Undo. Verify with new Data review tests using real SQLite.
2. Extract read-only preparation from `CblReadingListIO`, `CsvReadingListIO`, `ArcReadingListBuilder`, `ReadingListFolderIO` into manifests with explicit commit. Preserve existing import APIs. Verify existing import suites plus no-write/atomic manifest tests.
3. Add retained `ReadingListReviewViewModel` and `ReadingListReviewView.axaml`/code-behind together; route through `ReadingListsScreenViewModel`. Implement queue/comparison/search, selector, import mode, cross-list preview, history, drafts and compact behavior. Governed by avalonia-mvvm, avalonia-xaml, data-binding, layout-patterns, accessibility and project design language. Targeted App tests and headless theme renders.
4. Wire single link, current-list/gallery review, CBL/CSV/arc/folder preview, history and exit guard through existing owners. Preserve smart-list route. Verify entry points, cancellation, draft retention and app input paths.
5. Run serial focused Data/App suites, shared lint, build/XAML weave and Avalonia review checklist. Fix regressions; update authoritative todo with actual evidence and remaining manual checks. Commit spec/plan separately; implementation remains reviewable in working tree unless requested otherwise.
