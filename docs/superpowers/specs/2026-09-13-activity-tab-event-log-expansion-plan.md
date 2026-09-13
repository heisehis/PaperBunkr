# Activity tab event log expansion — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-13-activity-tab-event-log-expansion-design.md*

## Step 1: `SeriesActivityEvent` entity + `SeriesActivityLog` helper
**Files:** `src/Paperbunkr.Data/Entities/SeriesActivityEvent.cs` (new), `src/Paperbunkr.Data/SeriesActivityLog.cs` (new)
**What:** Entity + enum exactly as in the design doc (no FK, denormalized `SeriesId`/`IssueId`, matching `ReadingEvent`'s own "survive deletion of the thing it describes" convention). `SeriesActivityLog.Record(context, seriesId, kind, detail, issueId = null)` adds the row, does not call `SaveChanges()`.
**Depends on:** none
**Verify:** compiles.

## Step 2: Wire into `PaperbunkrDbContext` + migration
**Files:** `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit)
**What:**
1. Add `public DbSet<SeriesActivityEvent> SeriesActivityEvents => Set<SeriesActivityEvent>();` next to `ReadingEvents` ([PaperbunkrDbContext.cs:111](../../../src/Paperbunkr.Data/PaperbunkrDbContext.cs#L111)).
2. Add fluent config in `OnModelCreating`, mirroring `ReadingEvent`'s own block ([:1159-1168](../../../src/Paperbunkr.Data/PaperbunkrDbContext.cs#L1159-L1168)):
```csharp
modelBuilder.Entity<SeriesActivityEvent>(builder =>
{
    builder.HasKey(e => e.Id);
    builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(24);
    builder.Property(e => e.Detail).HasMaxLength(512);
    builder.HasIndex(e => e.TimestampUtc);
    builder.HasIndex(e => e.SeriesId);
});
```
3. Generate the migration: `dotnet ef migrations add AddSeriesActivityEvents --project src/Paperbunkr.Data`. Per this repo's CLAUDE.md migration-verification convention, confirm the generated `Up` creates exactly the one new table with no unrelated diffs (the working tree has another session's `AddCosmeticThumbnailToggles` migration already in place, uncommitted, ahead of this one — expect it to appear as the immediately-preceding migration in the chain, not something this migration touches).
**Depends on:** Step 1
**Verify:** `dotnet ef database update --project src/Paperbunkr.Data` applies cleanly against a scratch copy of the dev DB (don't run against the real per-user dev DB directly - copy `%LOCALAPPDATA%`'s Paperbunkr db file first, per this session's own caution about the shared dev DB), then `dotnet ef database update <PreviousMigration> --project src/Paperbunkr.Data` rolls back cleanly.

## Step 3: `ActivitySample` icon mapping + `BuildFromActivityEvent`
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit)
**What:** No changes to `ActivitySample.cs` itself (already generic: `Label`/`TimestampUtc`/`Icon`). Add a private static method next to the existing per-`ReadingEvent` label logic:
```csharp
private static ActivitySample BuildFromActivityEvent(SeriesActivityEvent e, IReadOnlyDictionary<int, string> issueTitles)
{
    string issueLabel = e.IssueId is int id && issueTitles.TryGetValue(id, out var title) ? $" (Issue {title})" : string.Empty;
    (string label, Symbol icon) = e.Kind switch
    {
        SeriesActivityEventKind.MetadataLinked => ($"Linked {e.Detail} metadata", Symbol.Link),
        SeriesActivityEventKind.MetadataUnlinked => ($"Unlinked {e.Detail} metadata", Symbol.Link),
        SeriesActivityEventKind.TrackerLinked => ($"Linked {e.Detail} tracker", Symbol.CloudSync),
        SeriesActivityEventKind.TrackerUnlinked => ($"Unlinked {e.Detail} tracker", Symbol.CloudSync),
        SeriesActivityEventKind.TrackerSynced => (e.Detail, Symbol.CloudSync),
        SeriesActivityEventKind.RatingChanged => ($"{e.Detail}{issueLabel}", Symbol.Star),
        _ => (e.Detail, Symbol.Info),
    };
    return new ActivitySample { Label = label, TimestampUtc = e.TimestampUtc, Icon = icon };
}
```
Also extract the existing inline `ReadingEvent → ActivitySample` logic (currently inline in `RefreshActivity`, [DetailTabsViewModel.cs:949-964](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L949-L964)) into its own `BuildFromReadingEvent(ReadingEvent e, IReadOnlyDictionary<int, string> issueTitles)` returning `ActivitySample`, unchanged logic, just extracted so `RefreshActivity` (Step 4) can call both builders symmetrically.
**Depends on:** Step 1 (needs `SeriesActivityEvent`/`SeriesActivityEventKind`)
**Verify:** compiles; covered by Step 7's tests.

## Step 4: Merge `SeriesActivityEvent` into `RefreshActivity`
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit, [:938-968](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L938-L968))
**What:** Replace the body with the two-source-materialize-then-merge shape from the design doc's "Reading the log" section - each source queried and capped at 20 independently, converted to `ActivitySample` via the Step 3 builders, concatenated, re-sorted by `TimestampUtc` descending, capped at 20 again.
**Depends on:** Step 3
**Verify:** unit tests in Step 7.

## Step 5: Call sites — metadata + tracker link/unlink/sync
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit)
**What:**
- `LinkMetadataAsync` ([:1179](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1179)): after the `RefreshExternalLinks(context, currentSeriesId);` call (line 1205) and before the `Dispatcher.UIThread.Post`, add `SeriesActivityLog.Record(context, currentSeriesId, SeriesActivityEventKind.MetadataLinked, SelectedMetadataProvider.Label); context.SaveChanges();` (a fresh `SaveChanges()` here since `RefreshExternalLinks` re-reads from the DB rather than tracking pending changes across the earlier `LinkAsync` call - check whether `MetadataLinkResolver.LinkAsync` already called `SaveChanges()` on this same context before reusing it silently; if so, this second call is a no-op, harmless either way).
- `UnlinkMetadata` ([:1222](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1222)): capture `link!.ProviderLabel` before the existing removal, and after the existing `context.SaveChanges()` inside the `if (existing is not null)` block, add a second `SeriesActivityLog.Record(...)` + `SaveChanges()` for `MetadataUnlinked` with `link.ProviderLabel` as detail - only inside that `if`, matching "only log when something was actually unlinked."
- `LinkTracker` ([:1422](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1422)): after `TrackerLinkResolver.Link(...)` (line 1430), before `RefreshTrackerLinks`, add `SeriesActivityLog.Record(context, currentSeriesId, SeriesActivityEventKind.TrackerLinked, SelectedTrackerService.ToString()); context.SaveChanges();`.
- `UnlinkTracker` ([:1450](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1450)): after `TrackerLinkResolver.Unlink(...)` (line 1458), same pattern with `TrackerUnlinked` and `link.Service.ToString()`.
- `SyncToTrackersAsync` ([:1472](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1472)): inside the `else` branch that builds `pulledSummary`/`pushedSummary`/`failureSummary` ([:1542-1547](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1542-L1547)), right after `TrackerSyncStatus = (pulledSummary + pushedSummary + failureSummary).Trim();`, add:
```csharp
if (pulled.Count > 0 || pushed.Count > 0)
{
    SeriesActivityLog.Record(context, currentSeriesId, SeriesActivityEventKind.TrackerSynced, (pulledSummary + pushedSummary).Trim());
    context.SaveChanges();
}
```
(the `failed`-only case is deliberately not logged - a failed sync attempt changed nothing about the series worth recording in its history).
**Depends on:** Step 1
**Verify:** unit tests in Step 7.

## Step 6: Call site — `QuickRateScreenViewModel.Save`
**Files:** `src/Paperbunkr.App/ViewModels/QuickRateScreenViewModel.cs` (edit, [:85-112](../../../src/Paperbunkr.App/ViewModels/QuickRateScreenViewModel.cs#L85-L112))
**What:** Capture `float? oldRating = issue.Rating;` immediately before the `issue.Rating = ...` assignment (line 99). After `context.SaveChanges();` (line 101), if `oldRating != issue.Rating`:
```csharp
if (oldRating != issue.Rating)
{
    string detail = issue.Rating is float r ? $"Rating set to {(int)r}" : "Rating cleared";
    SeriesActivityLog.Record(context, issue.SeriesId, SeriesActivityEventKind.RatingChanged, detail, issue.Id);
    context.SaveChanges();
}
```
Needs `using Paperbunkr.Data.Entities;` (for `SeriesActivityEventKind`) and `using Paperbunkr.Data;` (for `SeriesActivityLog`, if it isn't already imported - check current usings, [:1-8](../../../src/Paperbunkr.App/ViewModels/QuickRateScreenViewModel.cs#L1-L8) already has `using Paperbunkr.Data;`).
**Depends on:** Step 1
**Verify:** unit tests in Step 7.

## Step 7: Tests
**Files:** `src/Paperbunkr.Data.Tests/SeriesActivityLogTests.cs` (new), `src/Paperbunkr.App.Tests/DetailTabsViewModelTests.cs` (edit), `src/Paperbunkr.App.Tests/QuickRateScreenViewModelTests.cs` (edit if it exists - check first; create if this ViewModel has no test file yet)
**What:**
- `SeriesActivityLogTests`: `Record` inserts a row with the given `SeriesId`/`Kind`/`Detail`/`IssueId`, `TimestampUtc` is set to (approximately) now.
- `DetailTabsViewModelTests`: new cases seeding a `SeriesActivityEvent` row directly via the test's own `PaperbunkrDbContext` (same pattern this file already uses for seeding `Issue`s in its constructor) and asserting it appears in `vm.Activity` after `LoadSeries`; a case mixing one `ReadingEvent` and one `SeriesActivityEvent` at different timestamps asserting correct merged order; a case seeding 25 total rows across both tables asserting the cap is 20 combined, not 20-per-source. Plus one test per new call site: `LinkMetadataAsync`/`UnlinkMetadata`/`LinkTracker`/`UnlinkTracker` each produce exactly one `SeriesActivityEvent` of the right `Kind`; `SyncToTrackersAsync` produces one only when something pulled/pushed (not on a no-op sync with nothing connected).
- `QuickRateScreenViewModel` test: `Save()` with a changed rating produces a `SeriesActivityEvent` (`RatingChanged`, correct `IssueId`); `Save()` called with the *same* rating as already stored produces none.
**Depends on:** Steps 1-6
**Verify:** `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter "FullyQualifiedName~DetailTabsViewModelTests|FullyQualifiedName~QuickRateScreenViewModel"`, `dotnet test src/Paperbunkr.Data.Tests/Paperbunkr.Data.Tests.csproj --filter "FullyQualifiedName~SeriesActivityLogTests"`.

## Step 8: On-screen verification
**What:** Same caveat as the Details-tab work this session: skip a live app launch if the shared working tree still has concurrent uncommitted activity at implementation time - re-check before deciding. If clear, open a series, link/unlink external metadata, link/unlink a tracker, quick-rate an issue, and confirm each shows up in the Activity tab merged with existing reading events in the right order.
**Depends on:** Steps 1-6
**Verify:** manual, no automated substitute for the on-screen click-through.
