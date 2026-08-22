# Metadata Model — Phase 2c: Library Field Descriptors

**Date:** 2026-08-17
**Status:** Approved, pending implementation plan
**Source doc:** User-provided `PAPERBUNKR_METADATA_MODEL.md` (79-section architectural spec),
§39-42 ("Metadata Descriptor", "Descriptor Examples", "Sorting", "Grouping") - the final sub-project
of the Phase 2 decomposition (2a [Metadata Proposals](2026-08-17-metadata-model-phase2a-metadata-proposals-design.md),
2b [Series Reassignment](2026-08-17-metadata-model-phase2b-series-reassignment-design.md) both shipped).

## Context

A 2026-08-16 session paused "pluggable sort/group strategies" mid-design after finding
`Paperbunkr.Engine` already has CE's full ~73 `ComicBook` comparer + ~65 grouper classes ported
(byte-identical CE source, still targeting the old `ComicBook` type), unused by
`LibraryScreenViewModel`. That research assumed those ~73/~65 classes were the thing to "wire up."

Verified against the real code before designing this (standing project rule): they're not directly
wireable. Those ~73/~65 comparers/groupers are **Issue**-level (CE's per-book concepts - Writer,
Rating, Genre, per-issue Number). `LibraryScreenViewModel` sorts/groups **Series** - one card per
series, 7 sort fields and 4 group fields, all already series-level aggregates
(`Name`/`DateAdded`/`LastRead`/`Size`/`IssueCount`/`UnreadCount`/`Publisher` for sort;
`None`/`ContentType`/`Publisher`/`Alphabetical` for group). "Sort the library by Writer" doesn't
have an obvious meaning when the grid shows one card per series, not per issue - confirmed by
reading `LibraryScreenViewModel.SortCards`/`GroupCards` and `SeriesCardSample` directly, not
assumed from the paused session's framing.

**Scope decision, discussed and confirmed**: this phase is an **architectural refactor of the
existing 7+4 fields**, not a field-list expansion. User-visible behavior is unchanged. Building real
series-level aggregation for Issue-level concepts (e.g. "sort by average Rating") is explicitly a
separate, larger follow-up with its own aggregation-semantics decisions per field - not this phase.

**A real correctness constraint found while reviewing the current switch, not from the source
doc**: `SortCards` uses `StringComparer.OrdinalIgnoreCase` explicitly for `Name`/`Publisher` -
meaningfully different from .NET's default culture-aware `string.CompareTo`. The source doc's own
suggested descriptor shape (`SortStrategy` implying a generic `IComparable`-returning key selector)
would silently swap in culture-aware comparison for those two fields, changing real sort order for
non-ASCII series names. This spec designs around that instead of reproducing the source doc's
shape literally.

## Scope

### Why two descriptor types, not one unified `MetadataDescriptor`

The source doc's §39 `MetadataDescriptor` bundles `Key`/`DisplayName`/`ValueType`/`StorageKind`/
`Scope`/`IsStored`/`IsDerived`/`IsRelational`/`IsUserEditable`/`SortStrategy`/`GroupStrategy`/
`FilterStrategy`/`ProviderMappings`/`DisplayFormatter`/`ValueResolver` into one type per logical
field. Most of that doesn't apply here: `SeriesCardSample` is an already-materialized, in-memory
display view-model, not a database-backed, editable, provider-sourced entity - `StorageKind`,
`ProviderMappings`, `IsUserEditable` etc. have nothing real to describe. More importantly,
`LibrarySortField` (7 values) and `LibraryGroupField` (4 values) aren't the same set today - `Name`
isn't groupable, `ContentType`/`Alphabetical` aren't sortable - so a single descriptor type implying
"every field can sort AND group" would invent overlap that doesn't exist rather than modeling what's
real. Two small, purpose-fit descriptor types instead:

```csharp
public sealed record LibrarySortFieldDescriptor(LibrarySortField Field, string DisplayName, Comparison<SeriesCardSample> Compare);
public sealed record LibraryGroupFieldDescriptor(LibraryGroupField Field, string DisplayName, Func<SeriesCardSample, string> GroupKey, Comparison<string> GroupOrder);
```

`Comparison<T>`, not `IComparable`, is the load-bearing choice (see the correctness constraint
above) - each field's comparison body becomes a named delegate carrying its *exact* existing logic
verbatim: `StringComparison.OrdinalIgnoreCase` for `Name`/`Publisher`, the "blank Publisher →
Unknown" fallback, the first-letter-with-`#`-bucket for `Alphabetical`, the enum-value ordering
(not alphabetical-by-label) for `ContentType` groups. A generic key-extractor shape couldn't
reproduce these without reinventing per-field special-casing in a shared comparer - the whole
point is a faithful extraction, not a reinterpretation.

### `LibraryFieldCatalog`

New file, `src/Paperbunkr.App/Models/LibraryFieldCatalog.cs` - same data-driven-dictionary shape
this codebase already uses twice (`SmartListCatalog` for Smart Lists' per-Issue field selectors,
`BulkFieldRegistry` for Bulk Edit's per-Issue field descriptors), so this is the third application
of an established idiom, not a new one:

```csharp
public static class LibraryFieldCatalog
{
    public static readonly IReadOnlyDictionary<LibrarySortField, LibrarySortFieldDescriptor> SortFields = new Dictionary<LibrarySortField, LibrarySortFieldDescriptor>
    {
        [LibrarySortField.Name] = new(LibrarySortField.Name, "Name", (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)),
        [LibrarySortField.DateAdded] = new(LibrarySortField.DateAdded, "Date Added", (a, b) => a.LastAddedTime.CompareTo(b.LastAddedTime)),
        [LibrarySortField.LastRead] = new(LibrarySortField.LastRead, "Last Read", (a, b) => a.LastOpenedTime.CompareTo(b.LastOpenedTime)),
        [LibrarySortField.Size] = new(LibrarySortField.Size, "Size", (a, b) => a.TotalFileSize.CompareTo(b.TotalFileSize)),
        [LibrarySortField.IssueCount] = new(LibrarySortField.IssueCount, "Issue Count", (a, b) => a.IssueCount.CompareTo(b.IssueCount)),
        [LibrarySortField.UnreadCount] = new(LibrarySortField.UnreadCount, "Unread Count", (a, b) => a.UnreadCount.CompareTo(b.UnreadCount)),
        [LibrarySortField.Publisher] = new(LibrarySortField.Publisher, "Publisher", (a, b) => string.Compare(a.Publisher, b.Publisher, StringComparison.OrdinalIgnoreCase)),
    };

    public static readonly IReadOnlyDictionary<LibraryGroupField, LibraryGroupFieldDescriptor> GroupFields = new Dictionary<LibraryGroupField, LibraryGroupFieldDescriptor>
    {
        [LibraryGroupField.ContentType] = new(LibraryGroupField.ContentType, "Content Type",
            c => c.ContentTypeLabel,
            (a, b) => Enum.Parse<ContentType>(a).CompareTo(Enum.Parse<ContentType>(b))),
        [LibraryGroupField.Publisher] = new(LibraryGroupField.Publisher, "Publisher",
            c => string.IsNullOrWhiteSpace(c.Publisher) ? "Unknown" : c.Publisher,
            (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)),
        [LibraryGroupField.Alphabetical] = new(LibraryGroupField.Alphabetical, "Alphabetical",
            c => c.Name.Length > 0 && char.IsAsciiLetter(c.Name[0]) ? char.ToUpperInvariant(c.Name[0]).ToString() : "#",
            (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)),
    };
}
```

`LibraryGroupField.None` has no entry (nothing to describe - `GroupCards` keeps its existing
"return empty" behavior for it, same as today's switch's `_ =>` branch).

### `LibraryScreenViewModel` changes

- `SortCards`: the 8-branch switch becomes a catalog lookup (falling back to the `Name` descriptor
  for an unrecognized value, matching the switch's existing `_ =>` default) + `cards.Sort(descriptor.Compare)`,
  with the existing post-sort `Reverse()` for `SortDirection.Descending` unchanged.
- `GroupCards`: the 4-branch switch becomes a lookup (empty result if `GroupField` isn't in the
  catalog, covering `None` and any future gap the same way) + `cards.GroupBy(descriptor.GroupKey).OrderBy(g => g.Key, Comparer<string>.Create(descriptor.GroupOrder))`,
  building `SeriesCardGroup`s exactly as today.
- The separate `SortLabel` display-name switch (a *third* parallel switch, `LibrarySortField.Name => "Name"` etc.)
  collapses into `LibraryFieldCatalog.SortFields[SortField].DisplayName`.

Three parallel switches become one catalog - a real, if modest, win even though user-visible
behavior doesn't change. AXAML is untouched: button `Content=` labels for both Sort and Group are
already hardcoded there, not sourced from any C# switch, and the Group toolbar button already
displays the raw enum name today (`{Binding GroupField, StringFormat='Group: {0} ▾'}` - no
`GroupLabel` switch exists to replace) - a pre-existing minor cosmetic gap, not this phase's to fix.

## Testing

- Existing `LibraryScreenViewModelTests` sort/group assertions must pass **unmodified** - this is
  the regression guard proving behavior is identical, not just that new code compiles.
- New `LibraryFieldCatalogTests`: each sort descriptor's `Compare` produces the same ordering the
  old switch branch did (including descending direction at the `LibraryScreenViewModel` level, not
  just ascending `Compare` calls); each group descriptor's `GroupKey`/`GroupOrder` reproduce the
  old switch's edge cases specifically - blank `Publisher` → "Unknown", a name starting with a digit
  or symbol → `"#"` bucket, `ContentType` groups ordered by enum declaration order (`Comic, Manga,
  Manhua, Manhwa, Unknown` - confirmed from `ContentType.cs`) rather than alphabetically by label,
  which would otherwise put `Comic` after `Manga`/`Manhua`/`Manhwa`.

## Explicitly out of scope

Expanding the Library sort/group field lists using Issue-level aggregates (a separate, larger
follow-up - each new field needs its own aggregation-semantics decision, e.g. what "sort series by
Rating" even means when a series has many issues with different ratings). Wiring the ~73/~65 ported
CE `ComicBook` comparers/groupers to any UI - no Issue-level sortable/groupable surface exists in
this app yet for them to attach to; that's a prerequisite decision for a future phase, not this
one. `FilterStrategy`/filtering - Library's 3 filter checkboxes are a separate, already-shipped
concern (Saved List Layouts), untouched here. Fixing the Group toolbar label's raw-enum-name display
(the pre-existing gap noted above) - unrelated to this refactor's actual goal.
