# Plugin-driven grouped review, bulk delete, and scan alerts — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-alerts-design.md*

One implementation note beyond the design doc: §3 says the "Resolve All" button uses "the same
`TwoStepConfirm` pattern already used for destructive actions elsewhere." `TwoStepConfirm`
(`src/Paperbunkr.App/ViewModels/TwoStepConfirm.cs`) takes fixed `idleLabel`/`armedLabel` strings at
construction time, but this button's label needs a *live* delete count baked in
("Delete 7 duplicate files" → changes as radios/checkboxes move). Reusing the class literally would
mean reconstructing it on every selection change just to update its label - reasonable, but simpler
to inline the same arm/3-second-revert timer logic directly on `SmartScreenViewModel` with a
computed label property instead. Same UX, no behavior change from the design.

## Step 1: `PluginBookGroup` type
**Files:** `src/Paperbunkr.Plugins/Hooks/PluginGlobals.cs` (edit)
**What:** Add `public sealed record PluginBookGroup(string Label, IReadOnlyList<Issue> Books, int? SuggestedKeepIssueId = null);` near the other hook-result types (`ParsedComicPath`, `NetSearchResult`).
**Depends on:** none
**Verify:** builds (`dotnet build src/Paperbunkr.Plugins/Paperbunkr.Plugins.csproj`).

## Step 2: `ActivityLinkKind.PluginGroupedReview`
**Files:** `src/Paperbunkr.App/Models/ActivityLinkKind.cs` (edit)
**What:** Add a new enum member `PluginGroupedReview` with a doc comment: "Open Smart Lists and the Grouped Review overlay for a specific plugin command (payload = `\"pluginKey|commandKey\"`)."
**Depends on:** none
**Verify:** builds.

## Step 3: `SmartScreenViewModel` — grouped-review state + dispatch
**Files:** `src/Paperbunkr.App/ViewModels/SmartScreenViewModel.cs` (edit)
**What:**
- New model type `PluginGroupRowViewModel` (can live in this same file, matching how `SmartListSummary` lives in `Models/` but simpler review-row types have lived alongside their owning VM elsewhere in this codebase - check `MissingFileRowViewModel`/`SeriesConflictRowViewModel`'s actual file location first and mirror it exactly) - properties: `Label`, `Books` (`IReadOnlyList<IssueCardSample>`), `SelectedKeepIssueId` (`ObservableProperty`, settable via radio binding), `IsSkipped` (`ObservableProperty`, settable via checkbox binding), raising a `SelectionChanged` event (or taking an `onChanged` callback, matching `MissingFileRowViewModel`'s constructor-callback convention) so the owning VM can recompute the live delete count.
- New members on `SmartScreenViewModel`: `ObservableCollection<PluginGroupRowViewModel> GroupedReviewRows`, `[ObservableProperty] bool _isGroupedReviewOpen`, `int GroupedReviewDeleteCount` (computed: sum of `group.Books.Count - 1` for each non-skipped group with a valid keep selection... more precisely, count of books in non-skipped groups minus 1 per non-skipped group, since exactly one book per group is kept), `[ObservableProperty] bool _isResolveArmed`, `string ResolveAllLabel` (computed from `IsResolveArmed`/`GroupedReviewDeleteCount`), `CloseGroupedReviewCommand` (closes the overlay, resets `_isResolveArmed`), `ResolveAllCommand` (arms on first click with a 3-second `DispatcherTimer` revert exactly like `TwoStepConfirm.Trigger`'s own timer, same `ConfirmWindow = TimeSpan.FromSeconds(3)`; on the second click while armed, deletes).
- `LoadPluginListAsync`: after the existing `if (result.ReturnValue is IEnumerable<Issue> issues)` block, add an `else if (result.ReturnValue is IEnumerable<PluginBookGroup> groups)` branch that builds `GroupedReviewRows` (one `PluginGroupRowViewModel` per group, `Books` projected to `IssueCardSample` the same way the flat branch already does) and sets `IsGroupedReviewOpen = true` instead of touching `Results`. The existing flat branch is otherwise untouched.
- `ResolveAllCommand`'s delete step: for each non-skipped row in `GroupedReviewRows`, for each book in `row.Books` whose `Id != row.SelectedKeepIssueId`, delete via `LibraryDeletionHelper.RemoveIssue(context, issue)` (one shared `PaperbunkrDbContext`, one `SaveChanges()` at the end). After deleting, close the overlay, call `LoadPluginListAsync` again for the same command (re-run it fresh, matching the design's "reflects the post-delete state") and `RefreshSidebar()`/`RefreshPluginLists()`.
- New public method `OpenPluginListByKey(string pluginKeyAndCommandKey)` - splits on `'|'`, finds the matching entry in `_pluginListCommands` by `Command.PluginKey`/`Command.Key`, and calls the same path `SelectList` already uses for that synthetic id (`LoadPluginListAsync` or `LoadSmartList` as appropriate - here always `LoadPluginListAsync` since this is only ever called for a plugin-backed entry).
**Depends on:** Step 1 (`PluginBookGroup`).
**Verify:** unit tests in Step 8.

## Step 4: `PluginGroupedReviewOverlay` view
**Files:** `src/Paperbunkr.App/Views/SmartScreen.axaml` (edit)
**What:** Add a backdrop `Border` + `floatingPanel`-classed `Border` pair at the end of the root `Grid`, `IsVisible="{Binding IsGroupedReviewOpen}"` - same structural pattern as `LibraryScreen.axaml`'s Add-issue overlay (read that block first and mirror its backdrop/PointerPressed-to-close/floatingPanel shape exactly). Content: an `ItemsControl` over `GroupedReviewRows`, each row showing its `Label`, an `ItemsControl` of `Books` with a `RadioButton` per book (`GroupName` bound per-row so each group's radios are mutually exclusive, `IsChecked` two-way bound to whether that book's id equals `SelectedKeepIssueId`) and a small cover thumbnail (`AsyncCoverImage`, matching the Library grid's own usage), plus a "Skip this group" `CheckBox` bound to `IsSkipped`. Footer: a `Button` bound to `ResolveAllCommand`/`ResolveAllLabel`, and a "Close" button bound to `CloseGroupedReviewCommand`.
**Depends on:** Step 3.
**Verify:** manual/on-screen only for the actual rendering - out of scope per the design doc's own testing section.

## Step 5: `PluginScanAlertService`
**Files:** `src/Paperbunkr.App/Plugins/PluginScanAlertService.cs` (new)
**What:** Constructor takes `PluginHostService`/`IActivityService`. Private `Dictionary<string, int> _lastKnownGroupCounts`. `public async Task CheckForNewGroupsAsync()`: for each `command` in `pluginHost.Engine.GetCommands(PluginHooks.CreateBookList)`, skip if `command.Environment is null`; run it via `pluginHost.RunCommandAsync(command, new CreateBookListHookGlobals { Environment = command.Environment })`; if `result.ReturnValue is IEnumerable<PluginBookGroup> groups`, let `count = groups.Count()`, `key = $"{command.PluginKey}:{command.Key}"`, `last = _lastKnownGroupCounts.GetValueOrDefault(key)`; if `count > last`, call `activityService.RaiseAlert(new ActivityAlert { Severity = ActivityAlertSeverity.Info, Title = $"{count} possible duplicate{(count == 1 ? "" : "s")} found", ActionLabel = "Review", ActionLink = new(ActivityLinkKind.PluginGroupedReview, $"{command.PluginKey}|{command.Key}"), DedupeKey = $"plugin-grouped:{key}" })`; always update `_lastKnownGroupCounts[key] = count` (whether or not it alerted, so a shrink is remembered too - matches the design's "still detected correctly next time").
**Depends on:** Step 1.
**Verify:** unit tests in Step 8.

## Step 6: Wire `PluginScanAlertService` into `LibraryFolderScanner` + `App.axaml.cs`
**Files:** `src/Paperbunkr.App/Services/LibraryFolderScanner.cs` (edit), `src/Paperbunkr.App/App.axaml.cs` (edit)
**What:**
- `LibraryFolderScanner`: add `public static PluginScanAlertService? ScanAlertService { get; set; }` next to the existing `PluginHost` static, with a doc comment cross-referencing it the same way. At the end of `ImportFiles`, immediately before `return new LibraryFolderScanResult(issuesAdded, seriesTouched.Count);`, add `if (issuesAdded > 0) { ScanAlertService?.CheckForNewGroupsAsync().GetAwaiter().GetResult(); }` (blocking is safe here for the same reason `ApplyParseComicPathOverride`'s blocking call already is - `ImportFiles` runs on a background thread).
- `App.axaml.cs`: after the existing `LibraryFolderScanner.PluginHost = pluginHost;` line, add `LibraryFolderScanner.ScanAlertService = new PluginScanAlertService(pluginHost, mainViewModel.Activity);`.
**Depends on:** Step 5.
**Verify:** builds; behavior covered by Step 5's unit tests (this wiring itself is exercised only by a real scan, out of scope for automated testing the same way the rest of `App.axaml.cs`'s wiring is).

## Step 7: `MainViewModel.ResolveActivityLink` — new case
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:** Add `case ActivityLinkKind.PluginGroupedReview: GoSmartCommand.Execute(null); Smart.OpenPluginListByKey(link.Payload); break;` to the existing switch.
**Depends on:** Step 2, Step 3.
**Verify:** unit test in Step 8.

## Step 8: Tests
**Files:**
- `src/Paperbunkr.Plugins.Tests/HookCoveragePluginTests.cs` (edit) - the existing `CreateBookList`-hook fixture command still returns a flat list (proves that path still works unchanged); add one new command (new `.csx` under `src/Paperbunkr.Plugins.Tests/SamplePlugins/HookCoverage/`, e.g. `create-book-list-grouped.csx`, registered in that plugin's `plugin.xml`) returning `new[] { new PluginBookGroup("g1", new[] { Book }, Book.Id) }` and a test asserting the round-trip.
- `sample-plugins/DuplicateFinder/possible-duplicates.csx` (edit) - change to build one `PluginBookGroup` per series+number cluster instead of a flat list; `SuggestedKeepIssueId` prefers a book with `FilePath is not null && !IsPlaceholder`, else the group's first book.
- `src/Paperbunkr.Plugins.Tests/DuplicateFinderPluginTests.cs` (edit) - update the existing `CreateBookList_hook_returns_every_library_book_that_shares_series_and_number` test's assertions for the new grouped return shape (assert on `IReadOnlyList<PluginBookGroup>`/group contents instead of a flat `List<Issue>`).
- `src/Paperbunkr.App.Tests/SmartScreenViewModelTests.cs` (new, or edit if this file already exists - check first) - `LoadPluginListAsync` opens `IsGroupedReviewOpen` (not populating `Results`) when the command returns grouped data, using a fake `PluginHostService`/`Command` seam matching this test class's existing conventions (check `SmartScreenViewModelTests`' current fixture pattern if the file exists, otherwise mirror `LibraryScreenViewModelTests`' plugin-related test setup); toggling a row's `SelectedKeepIssueId`/`IsSkipped` changes `GroupedReviewDeleteCount`; `ResolveAllCommand` needs two invocations (arm, then confirm) and deletes exactly the non-kept/non-skipped books, leaving skipped groups' books untouched.
- `src/Paperbunkr.App.Tests/PluginScanAlertServiceTests.cs` (new) - a command whose group count grows between two `CheckForNewGroupsAsync()` calls raises exactly one alert via a fake `IActivityService` (or a real `ActivityService` instance, checking `Alerts`); a repeated call with the same count doesn't raise again; a command returning flat `Issue[]` never raises; a count that shrinks then regrows past the original high-water mark alerts again.
- `src/Paperbunkr.App.Tests/MainViewModelTests.cs` (edit, if it exists - check first) - `ResolveActivityLink` with `ActivityLinkKind.PluginGroupedReview` calls into `Smart.OpenPluginListByKey` with the right payload (may need a small seam on `SmartScreenViewModel` or verifying via `GoSmartCommand`'s resulting `CurrentScreen` plus `IsGroupedReviewOpen`, whichever this test class's existing pattern supports).
**Depends on:** Steps 1, 3, 5, 7.
**Verify:** `dotnet test src/Paperbunkr.Plugins.Tests/Paperbunkr.Plugins.Tests.csproj` (full suite) and `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter "FullyQualifiedName~Smart|FullyQualifiedName~PluginScanAlert|FullyQualifiedName~MainViewModel|FullyQualifiedName~DuplicateFinder"`, plus a full solution build (`dotnet build Paperbunkr.sln`).

## Step 9: `wiki/Plugins.md` update
**Files:** `wiki/Plugins.md` (edit)
**What:** Update the Duplicate Finder example section to describe "Possible Duplicates" as opening a grouped review overlay with bulk delete, and mention the proactive Activity Center alert after a scan finds new duplicates.
**Depends on:** Step 8 (so the description matches shipped behavior).
**Verify:** none (docs-only).
