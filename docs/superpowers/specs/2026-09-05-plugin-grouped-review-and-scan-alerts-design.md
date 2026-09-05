# Plugin-driven grouped review, bulk delete, and scan alerts — Design

2026-09-05

## Background

`CreateBookList` (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md, wired to a real Smart
Lists sidebar section in docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md
§6) lets a plugin command supply a dynamic list of books, shown as a flat cover-card grid — the
same treatment a real stored Smart List gets. That's enough for "here's a computed list of books"
but not for a genuine cleanup workflow: picking which of several duplicate copies to keep, bulk
deleting the rest, and finding out proactively when a new duplicate shows up after a scan.

This closes that gap as a **general Plugin API capability**, not a one-off. `sample-plugins/
DuplicateFinder/`'s "Possible Duplicates" command becomes its first real user, but nothing here is
duplicate-specific — any `CreateBookList` plugin can opt into it by returning a different shape.

## Goal

A plugin command can:
1. Return its results as **groups** instead of a flat list, each with a suggested "keep this one."
2. Have those groups reviewed in a dedicated overlay with per-group keep/skip choices and one bulk
   delete action, instead of just browsing a flat grid.
3. Get a **proactive Activity Center alert** when a library scan/import finds more groups than it
   found last time — without needing a new hook, since the host just re-runs the same
   `CreateBookList` command it already knows how to invoke.

## 1. `PluginBookGroup` — the new return shape

```csharp
// Paperbunkr.Plugins.Hooks
public sealed record PluginBookGroup(string Label, IReadOnlyList<Issue> Books, int? SuggestedKeepIssueId = null);
```

`CreateBookList`'s contract is unchanged and non-breaking: a script returning `IEnumerable<Issue>`
still gets today's flat grid. A script returning `IEnumerable<PluginBookGroup>` instead is the
signal to open the Grouped Review overlay. No manifest change (`plugin.xml` stays exactly as it
is) and no new hook constant — this is a richer answer to a question that already exists, resolved
entirely by the return type at dispatch time, the same way `PluginInvocationResult.ReturnValue`'s
type is already pattern-matched everywhere a hook result is consumed (e.g. `ParseComicPathHookGlobals`
returning `ParsedComicPath?` vs. nothing).

`SuggestedKeepIssueId` is optional — a script with no real signal for "which copy is best" can leave
it null; the overlay defaults to the first book in the group in that case.

## 2. Smart Lists dispatch change

`SmartScreenViewModel.LoadPluginListAsync` (added in the 2026-09-05 remaining-hooks session) currently
always populates the flat `Results` collection from `IEnumerable<Issue>`. It gains a second branch:

```csharp
if (result.ReturnValue is IEnumerable<PluginBookGroup> groups)
{
    OpenGroupedReview(command.PluginKey, command.Key, groups.ToList());
    return;
}
// existing flat-Issue[] path unchanged
```

`OpenGroupedReview` populates a new `ObservableCollection<PluginGroupRowViewModel> GroupedReviewRows`
and sets `IsGroupedReviewOpen = true`. The Smart Lists sidebar selection and `Results` grid are left
untouched underneath — closing the overlay returns to whatever was showing before, exactly like
every other overlay in this app (Migration, Bulk Editing) layers on top without disturbing the
screen behind it.

## 3. The Grouped Review overlay

New, generic (not Duplicate-Finder-specific) files, following this app's existing overlay
conventions (`IssuePropertiesScreen`/`BulkIssuePropertiesScreen`/`MigrationOverlay` — a
`floatingPanel`-classed `Border` over a backdrop, opened/closed via a bool on the owning screen's
ViewModel):

- **`PluginGroupRowViewModel`** — one row per `PluginBookGroup`: `Label`, the group's books as
  cover-card samples, a `SelectedKeepIssueId` (bound to a radio button per book, initialized from
  `SuggestedKeepIssueId` or the first book), and `IsSkipped` (a checkbox — skipping a group excludes
  every book in it from the bulk delete, the group is left untouched).
- **`SmartScreenViewModel`** gains `GroupedReviewRows`, `IsGroupedReviewOpen`,
  `GroupedReviewDeleteCount` (computed: sum of non-kept books across non-skipped groups, live-updated
  as radios/checkboxes change — drives the confirm button's label, e.g. "Delete 7 duplicate files"),
  `CloseGroupedReviewCommand`, and `ResolveAllCommand`.
- **`ResolveAllCommand`** — a two-step confirm (same `TwoStepConfirm` pattern already used for
  destructive actions elsewhere in this app, e.g. `SmartListSummary.DeleteConfirm`): first click arms
  it, second click actually deletes. Deletes go through the existing
  `LibraryDeletionHelper.RemoveIssue` — the same path Needs Review's Missing Files section already
  uses — once per non-kept, non-skipped `Issue`, inside one `PaperbunkrDbContext`/`SaveChanges` pass.
  This is host-side UI code executing a host-side deletion helper; the plugin script is never
  re-invoked to perform the delete, it only ever supplied the grouped data that led here.
- After resolving, `SmartScreenViewModel` re-runs the command (`LoadPluginListAsync` again) so the
  overlay's own list reflects the post-delete state, and calls `RefreshSidebar()`/`RefreshPluginLists()`
  so the sidebar's row (if it still shows a count) is current too.

New view: `PluginGroupedReviewOverlay.axaml`, added as a sibling overlay inside `SmartScreen.axaml`
(same `IsVisible="{Binding IsGroupedReviewOpen}"` pattern the Add-issue overlay in `LibraryScreen.axaml`
already uses).

## 4. Proactive Activity Center alerts

No new plugin hook — `CreateBookList` is already invocable by `PluginHostService.RunCommandAsync`.
What's new is a host-side trigger re-running it automatically.

- **Single hook point, not four**: `LibraryFolderScanner.ScanAllAsync`, `ImportNewFilesAsync`, drag
  import (`DragImportService`), and the live folder watch (`LiveFolderWatchService`) all funnel
  through `LibraryFolderScanner`'s one private `ImportFiles` method (confirmed by reading the four
  real call sites) — the check is added once, at the end of `ImportFiles`, gated on
  `issuesAdded > 0` (skip the check entirely on a no-op scan).
- **New `PluginScanAlertService`** (`Paperbunkr.App/Plugins/`), constructed once in `App.axaml.cs`
  alongside `PluginHostService`, wired onto `LibraryFolderScanner` the same way `PluginHost` and
  `AsyncPluginOverlayImage.PluginHost` already are — a settable static
  (`LibraryFolderScanner.ScanAlertService`), since this service is constructed in many places before
  any of these singletons exist.
- **Tracking is in-memory, not persisted.** `ActivityAlert`'s own doc comment states alerts are
  "session-scoped... not persisted to history in v1" — a new EF table + migration to track "last
  known group count" across app restarts would be a real inconsistency with that design, tracking
  something more durably than the alert it drives. `PluginScanAlertService` keeps a private
  `Dictionary<string, int>` keyed by `$"{PluginKey}:{CommandKey}"`, reset every session, defaulting
  to 0 for a command it hasn't seen yet this session.
- **`CheckForNewGroupsAsync()`**: for every enabled `CreateBookList` command, run it; if the result
  is `IEnumerable<PluginBookGroup>` and its count is greater than the last-known count for that
  command this session, call `IActivityService.RaiseAlert` with `Severity = Info`, a title like
  `"3 possible duplicates found"`, `DedupeKey = $"plugin-grouped:{pluginKey}:{commandKey}"` (so a
  second scan before the first alert is dismissed refreshes the one row instead of stacking), and
  `ActionLink = new(ActivityLinkKind.PluginGroupedReview, $"{pluginKey}|{commandKey}")`. Update the
  in-memory count regardless of whether the alert fired (so a shrink-then-regrow is still detected
  correctly next time).
- A command that returns flat `Issue[]` (not grouped) is never touched by this — only grouped
  results participate in alerting.

**New `ActivityLinkKind.PluginGroupedReview`** (payload `"pluginKey|commandKey"`).
`MainViewModel.ResolveActivityLink` gets a new case: navigate to Smart Lists
(`GoSmartCommand.Execute(null)`), then a new `SmartScreenViewModel.OpenPluginListByKey(pluginKey,
commandKey)` looks up the matching synthetic sidebar entry and calls the same `LoadPluginListAsync`
path a sidebar click already uses — clicking the alert lands the user directly in the Grouped
Review overlay, not just the Smart Lists screen.

## 5. Duplicate Finder becomes the real demonstration

`sample-plugins/DuplicateFinder/possible-duplicates.csx` changes from returning a flat
`IEnumerable<Issue>` to `IEnumerable<PluginBookGroup>` — one group per series+number cluster (same
`GroupBy` it already does), `SuggestedKeepIssueId` preferring a book with a real `FilePath` and
`!IsPlaceholder` over a fileless placeholder, falling back to the first book in the group if every
copy is a placeholder or none is preferred. The Library hook's "Find Duplicates in Selection"
command is untouched — it's a point check via `AskQuestion`, not a `CreateBookList` list, so it has
no reason to participate in the grouped-review shape.

`wiki/Plugins.md`'s Duplicate Finder section gets one more line once this ships, describing the
grouped review + bulk delete + alert behavior as what "Possible Duplicates" now does.

## Testing

- **`Paperbunkr.Plugins.Tests`**: extend `HookCoveragePluginTests`' `CreateBookList` coverage (or a
  new small fixture) with a command returning `IEnumerable<PluginBookGroup>`, proving the type
  round-trips through `PluginEngine.InvokeAsync` correctly. Update `sample-plugins/DuplicateFinder/
  possible-duplicates.csx`'s existing `DuplicateFinderPluginTests` `CreateBookList` test for the new
  return shape (groups instead of a flat issue list).
- **`Paperbunkr.App.Tests`**:
  - `SmartScreenViewModelTests` — `LoadPluginListAsync` opens the Grouped Review overlay (not the
    flat `Results` grid) when a command returns grouped data; `ResolveAllCommand` deletes exactly
    the non-kept, non-skipped books and leaves skipped groups' books untouched; changing a group's
    radio selection changes `GroupedReviewDeleteCount`.
  - New `PluginScanAlertServiceTests` — a command whose group count grows between two
    `CheckForNewGroupsAsync()` calls raises exactly one alert (dedup keeps it to one row across
    repeated growth-free re-scans); a command returning flat `Issue[]` never raises anything; a
    count that shrinks then regrows past the original high-water mark alerts again.
  - `MainViewModelTests` — `ResolveActivityLink` with `ActivityLinkKind.PluginGroupedReview`
    navigates to Smart Lists and opens the right command's overlay.
  - `PluginPackageServiceTests` (added for the "ship Duplicate Finder as a real plugin" work)
    unaffected — this doesn't touch installation, only the plugin's own script content.
- **On-screen verification**: out of scope for the implementation pass itself, flagged same as every
  other backlog item that ships without a live click-through.

## Out of scope

- Grouped review / bulk delete for hooks other than `CreateBookList` (`Library`'s point-check shape
  has no natural "group" concept to extend this to).
- A generic "plugin owns an arbitrary custom window" capability — this stays scoped to the one
  concrete shape (grouped rows + bulk delete) that's actually needed, not a general plugin-UI
  framework.
- Persisting alert-worthy state across app restarts (see the in-memory-tracking rationale in §4).
