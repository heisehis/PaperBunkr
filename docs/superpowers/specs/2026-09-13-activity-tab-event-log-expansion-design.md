# Activity tab: event log expansion

Date: 2026-09-13
Status: Approved for implementation

## Problem

The Series Detail screen's Activity tab (`DetailTabsViewModel.RefreshActivity`, [DetailTabsViewModel.cs:838](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L838)) shows only the last 20 `ReadingEvent` rows (issue opened/finished) for the series. Nothing else that happens to a series shows up here: linking/unlinking external metadata, linking/unlinking/syncing a tracker, rating an issue.

CE has no per-series activity/history feature to port (already checked `_reference/ComicRackCE` when `RefreshActivity` was first built — see that method's own doc comment) — this is Paperbunkr-original, so scope is a judgment call, not a parity question.

## Scope decided during grilling

Two categories were explicitly considered and **rejected** for this pass, both for the same reason: no existing timestamp source, and no way to backfill for anyone with an existing library.

- **Added-to-library**: `Issue` has no `CreatedAt`/`DateAdded` column at all.
- **Missing-file transitions**: `Issue.FileIsMissing` ([Issue.cs:194](../../../src/Paperbunkr.Data/Entities/Issue.cs#L194)) is a bare bool with a strike counter (`Issue.cs:303` area), no `MissingSince` timestamp.

Adding either means inventing history for existing libraries (2,801 comics in the reference library used during this session) that never happened — misleading. Out of scope; a future spec if wanted, with its own decision on how to handle existing data.

Also rejected: Collection/Continuity/Event membership changes — four separate resolver classes to touch (`ContinuityResolver`, `CollectionResolver`, `EventMembershipResolver`, plus the media-relation resolver) for lower payoff than the three kinds below.

**v1 scope**: Metadata linked, Metadata unlinked, Tracker linked, Tracker unlinked, Tracker synced, Rating changed.

## Design

### 1. Schema — new table, not a repurposed `ReadingEvent`

`ReadingEvent` already feeds Insights-dashboard pace/streak/totals calculations ([ReadingEvent.cs:1-20](../../../src/Paperbunkr.Data/Entities/ReadingEvent.cs#L1-L20) doc comment) — mixing unrelated kinds into that enum risks that numeric aggregation breaking or needing filtering it doesn't have today. New table instead, same "no FK, denormalized, survives deletion of the thing it describes" convention `ReadingEvent` already established:

```csharp
// src/Paperbunkr.Data/Entities/SeriesActivityEvent.cs
public class SeriesActivityEvent
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public int? IssueId { get; set; } // set for RatingChanged, null for the series-level kinds
    public SeriesActivityEventKind Kind { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Detail { get; set; } = string.Empty; // provider/service name, or "Rating set to 4"
}

public enum SeriesActivityEventKind
{
    MetadataLinked = 0,
    MetadataUnlinked = 1,
    TrackerLinked = 2,
    TrackerUnlinked = 3,
    TrackerSynced = 4,
    RatingChanged = 5,
}
```

New `DbSet<SeriesActivityEvent> SeriesActivityEvents => Set<SeriesActivityEvent>();` on `PaperbunkrDbContext`, alongside `ReadingEvents` ([PaperbunkrDbContext.cs:111](../../../src/Paperbunkr.Data/PaperbunkrDbContext.cs#L111)). New EF migration (see "Migration" below).

### 2. Write helper

`SeriesActivityLog` (new static class, `Paperbunkr.Data` — same layer/pattern as `ContinuityResolver`/`CollectionResolver`/`MediaRelationResolver`, all static resolver classes taking a `PaperbunkrDbContext`):

```csharp
public static class SeriesActivityLog
{
    public static void Record(PaperbunkrDbContext context, int seriesId, SeriesActivityEventKind kind, string detail, int? issueId = null)
    {
        context.SeriesActivityEvents.Add(new SeriesActivityEvent
        {
            SeriesId = seriesId,
            IssueId = issueId,
            Kind = kind,
            TimestampUtc = DateTime.UtcNow,
            Detail = detail,
        });
    }
}
```

Does not call `SaveChanges()` itself — every call site already calls `context.SaveChanges()` once after its own mutations; `Record` just adds to the same pending changeset (matches how e.g. `TrackerLinkResolver.Link` doesn't save either — the caller's existing `SaveChanges()` covers it).

### 3. Call sites (all in `DetailTabsViewModel.cs` except the last)

| Kind | Call site | Detail text |
|---|---|---|
| `MetadataLinked` | `LinkMetadataAsync`, right after `RefreshExternalLinks` ([:1105](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1105)) | provider label, e.g. `"AniList"` |
| `MetadataUnlinked` | `UnlinkMetadata` ([:1122](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1122)) | provider label |
| `TrackerLinked` | `LinkTracker` ([:1422](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1422)) | service name |
| `TrackerUnlinked` | `UnlinkTracker` ([:1450](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1450)) | service name |
| `TrackerSynced` | `SyncToTrackersAsync` ([:1472](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L1472)), once per sync action (not once per tracker) | the same aggregated summary text already built there (`pulledSummary + pushedSummary`), only when `pulled.Count > 0 \|\| pushed.Count > 0` |
| `RatingChanged` | `QuickRateScreenViewModel.Save()` ([QuickRateScreenViewModel.cs:99](../../../src/Paperbunkr.App/ViewModels/QuickRateScreenViewModel.cs#L99)), only when the rating actually changed | `"Rating set to {value}"`, or `"Rating cleared"` when set to null |

`QuickRateScreenViewModel` is a separate overlay ViewModel (opened from `MainViewModel.OpenQuickRateOverlay`, [MainViewModel.cs:1261](../../../src/Paperbunkr.App/ViewModels/MainViewModel.cs#L1261)) with no reference to `DetailTabsViewModel` — it writes `issue.Rating` directly against its own context. This is the one call site not inside `DetailTabsViewModel`; needs `issue.SeriesId` (already loaded via `.Include(i => i.Series)`) and comparing the pre-mutation rating (`before` snapshot already captured at [:97](../../../src/Paperbunkr.App/ViewModels/QuickRateScreenViewModel.cs#L97) for the existing write-back diff) against the new value before deciding to log.

Tracker sync logs one aggregate event, not one per tracker, matching the existing `TrackerSyncStatus` UI text which already aggregates into a single sentence ("Pulled from X. Synced to Y.") — consistent with how the feature already presents itself, not a new convention.

### 4. Reading the log — merged feed

`RefreshActivity` ([DetailTabsViewModel.cs:838](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L838)) changes from a single `ReadingEvents.Where(...)` query to unioning both tables in memory, ordering by `TimestampUtc` descending, taking 20 overall (not 20 from each):

```csharp
private void RefreshActivity(PaperbunkrDbContext context, int seriesId)
{
    Activity.Clear();
    var issueTitles = Issues.ToDictionary(i => i.Id, i => i.Title);

    // Each source capped at 20 independently, then merged and capped again - the true
    // combined top-20-by-timestamp can never need more than 20 rows from either single
    // source, so this two-step cap is exact, not an approximation.
    var readingEvents = context.ReadingEvents.Where(e => e.SeriesId == seriesId)
        .OrderByDescending(e => e.TimestampUtc).Take(20).ToList();
    var activityEvents = context.SeriesActivityEvents.Where(e => e.SeriesId == seriesId)
        .OrderByDescending(e => e.TimestampUtc).Take(20).ToList();

    var merged = readingEvents.Select(e => (e.TimestampUtc, Sample: BuildFromReadingEvent(e, issueTitles)))
        .Concat(activityEvents.Select(e => (e.TimestampUtc, Sample: BuildFromActivityEvent(e, issueTitles))))
        .OrderByDescending(x => x.TimestampUtc)
        .Take(20);

    foreach (var (_, sample) in merged)
    {
        Activity.Add(sample);
    }

    OnPropertyChanged(nameof(HasActivity));
}
```

Deliberately **not** one LINQ query projecting both entity types into a shared anonymous type before materializing (the obvious-looking alternative) - that shape needs a typed `null` literal for whichever entity type isn't present in a given row (e.g. `ReadingEvent = (ReadingEvent?)null` on the `SeriesActivityEvent` side), which most EF Core providers refuse to translate to SQL. Materializing each query to a `List` first, then converting to `ActivitySample` and merging in plain C#, sidesteps that entirely. `BuildFromReadingEvent` is the existing label-building logic already in `RefreshActivity` today, extracted into its own method unchanged; `BuildFromActivityEvent` is new, mapping each `SeriesActivityEventKind` to a label/icon pair, using `issueTitles` for `RatingChanged`'s issue label the same way `ReadingEvent` rows already do.

### 5. Icons

Extend `ActivitySample`'s icon mapping (currently `Finished → CheckmarkCircle`, else `Play`) with: `MetadataLinked/Unlinked → Link`, `TrackerLinked/Unlinked/Synced → CloudSync`, `RatingChanged → Star` — reusing `FluentIcons.Common.Symbol` values already used elsewhere in this same file (`Link` for the Related tab's tab icon, `CloudSync`/`Star` already used in `DetailTabs.axaml`'s Trackers/Specials sections).

### 6. Migration

New EF Core migration adding the `SeriesActivityEvents` table. Per this repo's CLAUDE.md migration gotchas (the rollback-chain orphan-column bug and the stale-Designer.cs-snapshot nullability bug), verify the generated `Up`/`Down` pair round-trips cleanly (`dotnet ef database update` then roll back one step) before treating it as done - this table has no rollback-fragile column type changes (a single new table, no `AlterColumn`/`DropColumn` on existing tables), so the specific bugs those notes describe don't apply here, but the general "run it both ways" verification still does.

## Testing

- `SeriesActivityLogTests` (new, `Paperbunkr.Data.Tests`): `Record` inserts a row with the given fields; `TimestampUtc` is set.
- `DetailTabsViewModelTests`: extend `RefreshActivity`'s existing tests (`LoadSeries_PopulatesActivity_FromReadingEventsForSeriesIssues` etc.) with cases seeding a `SeriesActivityEvent` row directly and asserting it appears in `vm.Activity` merged with `ReadingEvent` rows in correct time order, and that the 20-row cap applies across both sources combined, not per-source.
- Each new call site gets a test asserting one `SeriesActivityEvent` row of the right `Kind`/`Detail` appears after the action: `LinkMetadataAsync`, `UnlinkMetadata`, `LinkTracker`, `UnlinkTracker`, `SyncToTrackersAsync` (only when something actually pulled/pushed), and `QuickRateScreenViewModel.Save` (only when the rating value actually changed - a no-op Save with the same rating must not log anything).

## Out of scope (explicit)

- Added-to-library and missing-file-transition events (no timestamp source, see above).
- Collection/Continuity/Event membership change events.
- Any UI for browsing/filtering/exporting this log beyond the existing 20-row Activity tab list.
