# Same-X-different-Y, Complete Count, first-page-is-an-ad — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md*

Facts read from the code: `SmartListQueryBuilder.LoadSnapshot(ctx, conditions)` computes per-condition derived data only when a condition needs it and `EvalContext` carries it (see `DuplicateIds`, `SeriesStats`); `SmartListCatalog.TextSelectors/NumberSelectors` give the per-field values;
`SmartListConditionViewModel.ShowValue2` decides the second value box; `EditIssuesWithHistory` loads issues **without** proposals (so a count computed from numbers that exist only as proposals must be read in a separate query, as `CommitProposedValues` does); `IssuePageTagger.SetPageType(context, issueId, pageNumber, type)` tags a page (0-based `ImageIndex`);
`LibraryActionCatalog` builds entries from `ContextMenuEntry.Item(label, command, parameter, symbol)`; `Series.IsComplete` is `Status == Completed`.

## Step 1: `SameXDifferentY` helper
**Files:** `src/Paperbunkr.Data/SmartLists/SameXDifferentY.cs` (new), `src/Paperbunkr.Data.Tests/SameXDifferentYTests.cs` (new)
**What:** `Compute(IReadOnlyList<Issue> library, string x, string y) -> HashSet<int>`; attribute resolution (field name or label, ignoring case; text/number selectors as text; else custom value; else ignored).
**Verify:** duplicates (no Y), a Y that differs / that agrees, a group of one dropped, custom values on both sides, a missing custom value as "None" on the Y side only, unknown names ignored, empty X returns nothing.

## Step 2: The field, evaluator, registry, editor, import warning
**Files:** `SmartListField.cs` (append), `SmartListQueryBuilder.cs` (snapshot + evaluate), `PluginMatcherRegistry.cs`, `CeSmartListReader.cs` (+ `CeSmartListDraft.cs`: `CeWarningEffect.Approximated`), `SmartListConditionViewModel.cs`, `SmartScreen.axaml`, tests (`SameXDifferentYFieldTests.cs`, reader/import tests)
**Depends on:** Step 1
**Verify:** evaluation; the 9 real lists import with the condition and (all being first) no warning; a script placed second gets the approximation warning; the corpus run.

## Step 3: `CompleteCount`
**Files:** `src/Paperbunkr.Data/Metadata/CompleteCount.cs` (new), `CompleteCountTests.cs` (new), `LibraryScreenViewModel.DataTransfer.cs` (command), `LibraryActionCatalog.cs` (entry), `CompleteCountActionTests.cs` (new, App.Tests), `LibraryContextMenuBuilderTests.cs` (expected order)
**What:** pure `Compute(library issues of the series, targets) -> Dictionary<issueId, count>`; the command reads the target series' issues with proposals, computes, applies through `EditIssuesWithHistory`.
**Verify:** plain series, a completed vs ongoing series, volumes kept apart, the numbers-below-1000 guard, a jump, number above Count+1, non-numeric number skipped, undo.

## Step 4: "First page is an ad"
**Files:** `src/Paperbunkr.Data/Metadata/FirstPageAd.cs` (new: apply to one issue), tests, `LibraryScreenViewModel.DataTransfer.cs` (command with confirm), `LibraryActionCatalog.cs`, App tests
**Verify:** page 0 Advertisement and page 1 Cover; an issue with one page skipped; declined confirm changes nothing; existing rotation on a page is kept.

## Step 5: Docs (done together with D and F).
