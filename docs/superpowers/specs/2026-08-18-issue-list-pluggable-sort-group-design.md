# Issue List: Pluggable Sort/Group Strategies (Slice 1)

**Date:** 2026-08-18
**Status:** Approved, pending implementation

## Context

Resumes "pluggable sort/group strategies," paused 2026-08-17 pending user input on 5 unmapped
CE comparer/grouper concepts (see [[project_paperbunkr_session_2026-08-16_handoff]]). This session's
metadata-model work already unblocked 3 of those 5 with real data *and* UI (OpenCount, Series.Status,
IssueBookmark); `AlternateCount`/variant tracking remains a genuine gap (no `IssueEdition` model -
out of scope here); the 5th (a "Proposed-metadata workflow" sort concept) doesn't map cleanly to a
sort/group axis at all and stays out of scope too.

Research this session (confirmed against `_reference/ComicRackCE` directly, not assumed) settled
three things that shape this spec:

1. **CE's real per-issue "Comic List" is a view-mode toggle, not a separate app**:
   `ComicBrowserControl.ItemViewMode` (Thumbnail/Tile/**Detail**) on the same control that also does
   cover browsing - `Detail` mode is CE's sortable/groupable column list. Grouping in Detail mode
   uses a distinct mechanism (`IColumn.ColumnGrouper`) from Thumbnail/Tile's visual "stacking."
2. **Paperbunkr's existing `LibraryViewMode.Details`/`List`/`Tiles` options are a false cognate** -
   confirmed by reading `LibraryScreen.axaml`: they're alternate row/tile *layouts* of the same
   `SeriesCardSample` series-card data (Name/Content Type/Issues/Unread/Publisher columns), sharing
   `LibrarySortField`. Not per-issue data at all, despite the suggestive name. Nothing about them
   changes; this feature doesn't touch `LibraryViewMode`.
3. **No per-issue flat/tabular browsing surface exists anywhere in Paperbunkr today** - confirmed by
   repo-wide search. This is genuinely new UI, not an extension of something partial.

**Placement, per explicit user direction**: a new top-level rail-nav destination ("Comic List"),
alongside Library/Smart Lists/Reading Lists/Events - not folded into Library's already series-shaped
Display dropdown, and not repurposing the misleadingly-named existing "Details" mode.

### CE comparer/grouper architecture (research findings, informing the design below)

`src/Paperbunkr.Engine/Metadata/ComicBook/Comparer/*.cs` has 73 already-ported, byte-identical,
dormant CE comparer classes (`ComicBookXComparer : Comparer<ComicBook>`, mostly one-line `string.
Compare`/`.CompareTo` bodies). CE's own `Group/` folder (not yet ported at all) has exactly 65
`ComicBookGroupXxx` classes, one per groupable field - confirmed by direct count, not the 5 base/
infrastructure files Paperbunkr already has. **Two confirmed upstream CE bugs** in the ported
comparers (`ComicBookNewPagesComparer` compares `NewPages` against the *other* book's
`BookmarkCount`; `ComicBookWeekComparer` compares `Day` against the *other* book's `Week`) - neither
field is in this slice, but noted for whenever they are: fix, don't replicate (§0's own "preserve
semantics where useful, not architecture/bugs merely for compatibility"). CE's own column table
marks 7 of 73 fields sortable-but-not-groupable (Status, Tags, FilePath, ISBN, BookmarkCount,
NewPages, Review) - precedent this slice follows for `Tags`.

### Reusable strategies vs. named delegates - a real precedent conflict, resolved

§39-42 of the source doc calls for named, reusable "SortStrategy"/"GroupStrategy" types (NaturalString,
Numeric, Date, Rating, Boolean, etc.). But `LibraryFieldCatalog`'s own doc comment (Phase 2c,
already shipped) explicitly *rejected* a generic strategy/key-selector shape in favor of one
`Comparison<T>` delegate per field, reasoning that a generic shape can't carry each field's exact
comparison semantics (ordinal case-insensitive vs. natural-number vs. CE's article-aware string sort)
without reinventing them behind an abstraction. **Resolution**: keep `Comparison<T>`/group-key
delegates as the load-bearing per-field shape (consistent with the shipped precedent), but back them
with **genuinely reusable static helper functions** (`SortStrategies.CaseInsensitiveString(selector)`
returning a `Comparison<IssueListRow>`, etc.) - real reuse of comparison *logic* across many fields,
without the generic indirection layer Phase 2c already found wanting for this codebase.

## Scope: Slice 1 - infrastructure + 18 fields

Deliberately not all 73/65 - same "right-sized slice, defer the rest explicitly" discipline as every
other phase this session. Slice 1 covers the most broadly useful fields; the long tail (collector
fields like BookPrice/BookCondition, less-common creator roles, `AlternateCount`/`Manga`/
`SeriesComplete`/`SeriesEnableProposed` - all confirmed real gaps or cross-entity-mapping decisions
during research, not implementable as a clean 1:1 port) is out of scope, tracked for future slices.

### `SortStrategies` / `GroupStrategies`

New file `src/Paperbunkr.App/Models/SortGroupStrategies.cs` - static factory methods, each returning
a `Comparison<IssueListRow>` (sort) or `(Func<IssueListRow,string> Key, Comparison<string> Order)`
(group):

```csharp
public static class SortStrategies
{
    public static Comparison<IssueListRow> CaseInsensitiveString(Func<IssueListRow, string?> get);
    public static Comparison<IssueListRow> Numeric<T>(Func<IssueListRow, T?> get) where T : struct, IComparable<T>;
    public static Comparison<IssueListRow> Date(Func<IssueListRow, DateTime?> get);
    public static Comparison<IssueListRow> Boolean(Func<IssueListRow, bool> get);
    public static Comparison<IssueListRow> IssueNumber(); // wraps the existing Issue.NumberSortKey path
}

public static class GroupStrategies
{
    public static (Func<IssueListRow,string>, Comparison<string>) Alphabetical(Func<IssueListRow, string?> get, string fallback = "Unknown");
    public static (Func<IssueListRow,string>, Comparison<string>) NumericBucket(Func<IssueListRow, int?> get);
    public static (Func<IssueListRow,string>, Comparison<string>) Boolean(Func<IssueListRow, bool> get, string trueLabel, string falseLabel);
}
```

### `IssueListRow`

New file `src/Paperbunkr.App/Models/IssueListRow.cs` - projection DTO, same rationale as
`SeriesCardSample` (avoid holding live EF-tracked entities for a potentially-library-wide list;
precompute what needs a join once, not per sort-click): `Id`, `SeriesId`, `SeriesName`, `Number`,
`NumberSortKey` (decimal?), `Title` (effective), `Writer`, `Publisher`, `Genre`, `Format`, `Tags`,
`AddedTime`, `ReleasedTime`, `Year`, `PageCount`, `FileSize`, `Rating`, `CommunityRating`,
`ReadPercentage` (computed), `OpenCount`, `IsMissing`, `CoverBrush`.

### `IssueListSortField` / `IssueListGroupField`

New enums, `src/Paperbunkr.Data/Entities/` (matching `LibrarySortField`/`LibraryGroupField`'s own
location, for the same future reason - `AppSettings` persistence):

```csharp
public enum IssueListSortField
{
    Number, Series, Title, Writer, Publisher, Genre, Format,
    Added, Released, Year, PageCount, FileSize, Rating, CommunityRating,
    ReadPercentage, OpenCount, Tags, Status,
}

public enum IssueListGroupField
{
    None, Series, Publisher, Genre, Format, Year, Status,
}
```

`Tags`/`Status`(as a sort field)/`Number`/`Title`/`Writer`/`PageCount`/`FileSize`/`Rating`/
`CommunityRating`/`ReadPercentage`/`OpenCount`/`Added`/`Released` are sort-only (no group entry) -
matches CE's own "not everything sortable is groupable" precedent. `Series` sorts by name with
`NumberSortKey` as tie-break (simplified from CE's real Format→Volume→Number three-level tie-break -
noted as a deliberate simplification, not an oversight). `Year` sorts by the raw `Issue.Year` int,
**not** replicating CE's real `ComicBookYearComparer` bug (which actually sorts by full `Published`
date) - the sane fix, per the "don't replicate CE bugs" rule above.

### `IssueListFieldCatalog`

New file `src/Paperbunkr.App/Models/IssueListFieldCatalog.cs` - same data-driven dictionary shape as
`LibraryFieldCatalog`, built on the `SortStrategies`/`GroupStrategies` helpers above.

### New screen: Comic List

`IssueListScreenViewModel`/`IssueListScreen.axaml`, new rail-nav entry (mirrors how Smart Lists/
Reading Lists/Events already work as siblings, not children, of Library). Columnar list (Cover
thumbnail / Number / Title / Series / Writer / Publisher / Added, the most broadly useful columns
visible by default - not all 18 fields as columns simultaneously, matching CE's own column-visibility
precedent that not every sortable field needs a permanently-visible column). Click a column header to
sort (ascending, click again for descending - same toggle convention `LibraryScreenViewModel`'s
existing sort already uses); a "Group by" picker (pill + flyout, same idiom as Library's own Sort/
Group pills) for the 6 groupable fields. Clicking a row opens that issue directly in the Reader
(reuses the existing Reader navigation callback shape already threaded through `LibraryScreenViewModel`
today). No filtering/search in slice 1 (Library already has its own; this screen's own filter/search
is a natural follow-up slice, not blocking a useful first ship).

Sort/group state is session-only in slice 1 (not yet persisted to `AppSettings`) - Saved List
Layouts' own precedent could extend here later, but isn't required for a working first slice.

## Explicitly out of scope (tracked for future slices, not dropped)

The remaining ~55 of 73 comparer fields (mostly collector/Book* fields, less-common creator roles,
`ISBN`/`ScanInformation`/`Web`/`Review`/etc.) and the remaining ~59 of 65 CE groupers. The 4 fields
confirmed during research as genuine gaps or ambiguous cross-entity mappings needing their own design
decision before porting: `AlternateCount` (no data model at all), `Manga`/CE's per-issue bool (only
`Series.ContentType` remains, needs a join + bool-vs-enum decision), `SeriesComplete` (splits across
`Issue.IsFinalIssue` and `Series.Status`, no clean 1:1), `SeriesEnableProposed` (no Paperbunkr
equivalent - proposal review is per-field/per-proposal, not one on/off toggle). Search/filter on the
new screen. Persisting sort/group/column state. Column visibility customization. `NewPages`/`Week`/
`BookmarkCount` (all valid future additions, deliberately held back so the two confirmed CE
comparer bugs affecting two of them can be fixed then, not now, keeping this slice's diff focused).

## Testing

- `SortGroupStrategiesTests`: each strategy helper's comparison semantics in isolation (case-
  insensitivity, nulls-sort-first-or-last consistently, numeric vs. string fallback).
- `IssueListFieldCatalogTests`: every `IssueListSortField`/`IssueListGroupField` has a catalog entry;
  sorting/grouping a small known set of `IssueListRow`s produces the expected order/buckets per field.
- `IssueListScreenViewModelTests`: loads rows across multiple series (confirms it's genuinely
  cross-series, not scoped to one), sort-field/direction toggling reorders correctly, group-by
  produces the right buckets, clicking a row navigates to the right issue in the Reader.
