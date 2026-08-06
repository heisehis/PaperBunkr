# Smart Lists — Design Spec

*Date: 2026-08-06. Scope: wire the Smart Lists screen (`SmartScreenViewModel`/`SmartScreen.axaml`) and its sidebar to real, persisted data, with full field parity against ComicRackCE's smart-list matcher system. Currently both are hardcoded sample content (`ListName`/`Subtitle`/`MatchCountLabel` static strings; conditions hand-written in `SmartScreen.axaml`; sidebar entries hand-written in `MainWindow.axaml`).*

**Verification practice for this feature (and going forward per user instruction):** every field, default value, threshold, and behavior below was checked against ComicRackCE's actual source (`_reference/ComicRackCE`) before being added — not invented. Sources cited inline. Where CE's own code disagreed with itself, or where Paperbunkr's schema (which CE never had — e.g. `Series` as a first-class entity) forces a different answer than CE's literal behavior, that's called out explicitly as a deliberate deviation, not an oversight.

## 1. Scope

Covers:
- Custom, user-created smart lists (the rule-builder screen: Genre/Read/Date-Added-style conditions, AND-only, "Currently matches N issues" live count).
- System (built-in) smart lists shown in the sidebar (My Favorites, Recently Added, Recently Read, Never Read, Reading, Read, Missing Files, Duplicate Candidates) — implemented via the *same* rule engine as custom lists, seeded as `IsSystem = true` rows, except Duplicate Candidates which needs a dedicated query (see §6).
- Full field-catalog parity with CE's ~70 `ComicBook*Matcher` classes (see §4), including new schema for fields CE has that Paperbunkr's `Issue`/`Series` entities don't yet carry ("book collection" fields: condition, price, owner, location, store, ISBN, notes, age, collection status; plus Checked, Rating, CommunityRating, FileSize, file timestamps, BlackAndWhite, custom values).

Explicitly deferred (not part of this pass):
- **`AllProperties`/global free-text search** (CE's `ComicBookAllPropertiesMatcher`) — this is really "wire the library search box," a separate small feature, not a rule-builder condition. Revisit as its own pass.
- **"Files to Update"** built-in (CE's `ComicBookModifiedInfoMatcher`, backed by `ComicInfoIsDirty`) — depends on a metadata-writeback/save-to-file feature that doesn't exist in Paperbunkr yet. No `IsDirty`-style flag is added in this pass.
- **A "Book Collection" editor UI** for the new condition/price/owner/location/etc. fields — this pass adds the schema and makes those fields usable in smart-list rules; a dedicated editor panel (e.g. in the Detail screen) for *setting* those values by hand is a natural, separate follow-up.
- **Bookmarks feature** — `BookmarkCount` is included in the field catalog for parity but has no backing data yet (no bookmark entity/UI exists), so it always evaluates to `0` until that feature is built.
- **Duplicate Finder plugin-demo screen** (`PluginScreenViewModel`) — unrelated; stays on its existing sample data.

## 2. Schema changes

```csharp
// Issue.cs — additions
public bool Checked { get; set; }                 // CE: ComicBook.Checked (ComicBookCheckedMatcher)
public string? MainCharacterOrTeam { get; set; }   // CE: ComicInfo.MainCharacterOrTeam — a real ComicInfo.xml
                                                    // field missed in the original Issue.cs port; not new-for-CE.
public float? Rating { get; set; }                 // CE: ComicBook.Rating ("My Rating")
public float? CommunityRating { get; set; }        // CE: ComicBook.CommunityRating
public string? ISBN { get; set; }                  // CE: ComicBook.ISBN
public string? ScanInformation { get; set; }       // CE: ComicBook.ScanInformation
public bool BlackAndWhite { get; set; }             // CE: ComicBook.BlackAndWhite (YesNo in CE; collapsed to bool — see §3)
public long? FileSize { get; set; }                // CE: ComicBook.FileSize (bytes)
public DateTime? FileModifiedTime { get; set; }    // CE: ComicBook.FileModifiedTime
public DateTime? FileCreationTime { get; set; }    // CE: ComicBook.FileCreationTime
public int? NewPages { get; set; }                 // CE: ComicBook.NewPages — count of pages added since last read

// "Book collection" fields — CE: ComicBook.BookAge/BookCollectionStatus/BookCondition/
// BookLocation/BookNotes/BookOwner/BookPrice/BookStore. No editor UI yet (see §1 deferred).
public string? BookAge { get; set; }
public string? BookCollectionStatus { get; set; }
public string? BookCondition { get; set; }
public string? BookLocation { get; set; }
public string? BookNotes { get; set; }
public string? BookOwner { get; set; }
public float? BookPrice { get; set; }
public string? BookStore { get; set; }
```

```csharp
// New entity — fresh design, not a port of CE's packed CustomValuesStore string blob
// (CE's ComicBookCustomValuesMatcher parses a delimited string on every match; a real
// child table queries cleanly in SQL instead, per the provenance practice in
// docs/onboarding.md §2 of writing fresh where it costs nothing).
public class IssueCustomValue
{
    public int Id { get; set; }
    public int IssueId { get; set; }
    public Issue? Issue { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
```

```csharp
// New entities — smart list itself
public class SmartList
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }   // seeded built-ins; read-only in the rule-builder UI
    public int SortOrder { get; set; }
    public List<SmartListCondition> Conditions { get; set; } = new();
}

public class SmartListCondition
{
    public int Id { get; set; }
    public int SmartListId { get; set; }
    public SmartList? SmartList { get; set; }
    public SmartListField Field { get; set; }
    public SmartListOperator Operator { get; set; }
    public string Value { get; set; } = string.Empty;   // parsed per field's SmartListDataType
    public string? Value2 { get; set; }                  // populated only for InRange/DateInRange
    public string? CustomValueName { get; set; }          // populated only when Field == CustomValue
    public int SortOrder { get; set; }
}
```

Conditions are always AND-ed together — no OR/grouping in v1, matching the wireframe (only "AND" pills shown) and simplifying the query builder considerably. `Series.IsFavorite` is **not** added — see §5, Favorites is defined via `Issue.Rating` instead, matching CE exactly (an earlier draft of this spec had it as a per-series bool; corrected after checking CE's actual `ComicLibrary.InitializeDefaultLists`, which favorites are `Rating > 3` per-issue).

## 3. Operator model

```csharp
public enum SmartListDataType { Text, Number, Toggle, Date }

public enum SmartListOperator
{
    Is, IsNot,                                      // Text, Toggle
    Contains, ContainsAny, ContainsAll,              // Text
    StartsWith, EndsWith,                            // Text
    GreaterThan, LessThan, InRange,                  // Number
    IsAfter, IsBefore, WithinLastDays, DateInRange,  // Date
}
```

Mirrors CE's four base-matcher families (`ComicBookStringMatcher`/`NumericMatcher`/`YesNoMatcher`/`DateMatcher`) exactly, collapsed from CE's tri-state `YesNo` (Yes/No/Unknown) to a plain two-state `Toggle` — Paperbunkr's schema uses `bool`/`bool?` rather than porting the `YesNo` enum for these fields (matches how `Series.IsComplete`, `Issue.FileIsMissing`, etc. are already modeled). The rule-builder UI only offers the operators valid for the selected field's `SmartListDataType`.

## 4. Field catalog

A data-driven table, not one hand-written case per field — each entry is `(SmartListField, Label, SmartListDataType, Expression<Func<Issue, object?>> Selector)`. The query builder (§5) is generic per `SmartListDataType`, so adding a field later is a one-line catalog entry, not new query logic.

**Series-level** (via `Issue.Series` navigation):
`SeriesName`, `Genre`, `Publisher`, `ContentType`, `ReadingMode`, `SeriesComplete` (→ `Series.IsComplete`)

**ComicInfo text fields** (already on `Issue`):
`Title`, `Number`, `Volume`, `Count`, `AlternateSeries`, `AlternateNumber`, `StoryArc`, `StoryArcNumber`, `SeriesGroup`, `Summary`, `Notes`, `Review`, `Writer`, `Penciller`, `Inker`, `Colorist`, `Letterer`, `CoverArtist`, `Editor`, `Translator`, `Imprint`, `Web`, `LanguageISO`, `Format`, `AgeRating`, `Characters`, `Teams`, `Locations`, `MainCharacterOrTeam`, `Tags`

**Numeric:**
`Year`, `Month`, `Day`, `PageCount`, `Rating`, `CommunityRating`, `BookPrice`, `FileSize` (MB-converted, matching CE's `ComicBookFileSizeMatcher`), `ReadPercentage` (computed: `PageCount > 0 ? (LastPageRead ?? 0) * 100 / PageCount : 0`, matches CE's `ComicBook.ReadPercentage`), `BookmarkCount` (always `0` — deferred, §1), `NewPages` (§2)

**Toggle:**
`Checked`, `IsMissing` (→ `FileIsMissing`), `IsLinked` (computed: `!string.IsNullOrEmpty(FilePath)`, matches CE), `BlackAndWhite`, `HasCustomValues` (computed: `CustomValues.Any()`)

**Date:**
`Added` (→ `AddedTime`), `Opened` (→ `OpenedTime`), `Released` (→ `ReleasedTime`), `Modified` (→ `FileModifiedTime`), `Created` (→ `FileCreationTime`)

**File-derived text** (all parsed from `Issue.FilePath`, matching CE's `ComicBookFile/FullPath/Directory/FileFormatMatcher` — no new columns):
`File` (filename), `FullPath`, `Directory`, `FileFormat` (extension)

**Book-collection text** (§2, no editor UI yet):
`BookAge`, `BookCollectionStatus`, `BookCondition`, `BookLocation`, `BookNotes`, `BookOwner`, `BookStore`, `ISBN`, `ScanInformation`

**Special-cased (don't fit the flat catalog):**
- `CustomValue` — 2-argument in CE (`ComicBookCustomValuesMatcher`: name + value). Evaluated as `i.CustomValues.Any(cv => cv.Name == condition.CustomValueName && <Text operator against cv.Value>)`.
- `Duplicate` — see §6, handled outside the `Where()` chain entirely.
- **Not included:** `AllProperties` (deferred, §1), `Manga` (superseded by `ContentType`/`ReadingMode`, already covered — CE's own migration mapping in onboarding.md §6 already collapses this).

`Week` (CE: `ComicBookWeekMatcher`, ISO week computed from `Year`/`Month`/`Day`, which are free-standing nullable ints rather than a real date) is **omitted from v1** — computing an ISO week from three loose nullable ints isn't SQL-translatable via EF Core, and would require full client-side (in-memory) evaluation for that one condition. Low value relative to the complexity; can revisit if wanted later.

## 5. Built-in (system) smart lists

Seeded once (same pattern as `PaperbunkrDb.EnsureCreatedAndSeeded`), `IsSystem = true`. All values pulled from `ComicLibrary.cs`/`EngineConfiguration.cs`, not invented:

| List | Rule | CE source |
|---|---|---|
| My Favorites | `Rating > 3` | `ComicLibrary.InitializeDefaultLists` (`ComicBookRatingMatcher`, operator `Greater`, value `"3"`) |
| Recently Added | `Added` within last 14 days | `ComicLibrary.DefaultRecentlyAddedList` (`IsRecentInDays = 14`) |
| Recently Read | `Opened` within last 14 days | `ComicLibrary.DefaultRecentlyReadList` |
| Never Read | `ReadPercentage < 10` | `InitializeDefaultLists` (`IsNotReadCompletionPercentage = 10`) |
| Reading | `ReadPercentage` in `[10, 95]` inclusive | `ComicLibrary.DefaultReadingList` |
| Read | `ReadPercentage > 95` | `InitializeDefaultLists` (`IsReadCompletionPercentage = 95`) |
| Missing Files | `IsMissing = true` | `ComicBookIsMissingMatcher` (Maintenance group in the wireframe; not part of CE's default folder, but a real matcher) |
| Duplicate Candidates | see §6 | `ComicBookDuplicateMatcher` (Maintenance group) |

Note: CE's own two code paths for Reading/Read/Never-Read boundaries disagree with each other (`DefaultReadingList` uses `[10,95]` inclusive; the search-panel convenience path in `ComicBookAllPropertiesMatcher.Create` shifts to `[11,94]` to avoid overlap). Paperbunkr uses `[10,95]` inclusive — the actual shipped default smart list, confirmed as the more authoritative of the two — meaning `ReadPercentage` values of exactly `10` or `95` match two lists simultaneously, same as real CE.

`IsSystem` lists render in the sidebar and the rule-builder screen shows their conditions read-only (no Save/Duplicate/Delete chrome) — custom (`IsSystem = false`) lists get full CRUD.

## 6. Duplicate detection

Confirmed via `ComicBookDuplicateMatcher`: CE unions two independently-grouped duplicate sets — (a) metadata key: `Series + Format + Count + Number + Volume + LanguageISO + Year + Month + Day`, and (b) `FilePath`. Both grouped with `Count() > 1`, concatenated, de-duplicated.

Implemented as a dedicated method, `DuplicateIssueIds(DbContext) : HashSet<int>`, computed once per query (not a per-row `Where()` predicate — grouping doesn't compose with the AND-chain the way other conditions do). Used both by the sidebar's "Duplicate Candidates" system list and — if referenced inside a custom rule as the `Duplicate` field — intersected into that list's result set. One implementation, two call sites, exactly matching CE's own dual-purpose use of this matcher.

## 7. Query engine

```csharp
public static class SmartListQueryBuilder
{
    public static IQueryable<Issue> Build(PaperbunkrDbContext ctx, SmartList list)
    {
        var query = ctx.Issues.Include(i => i.Series).AsQueryable();
        foreach (var condition in list.Conditions.OrderBy(c => c.SortOrder))
        {
            query = condition.Field == SmartListField.Duplicate
                ? ApplyDuplicateFilter(ctx, query)
                : ApplyCondition(query, condition);
        }
        return query;
    }
    // ApplyCondition switches on the field's SmartListDataType (4 cases, not 70),
    // looks up the field's Selector from SmartListCatalog, and delegates to one of
    // four typed evaluators (Text/Number/Toggle/Date) that build the .Where() clause
    // from Operator + Value(+Value2).
}
```

This one builder powers the live match-count badge, the actual filtered issue list, and every system smart list — no special-casing except `Duplicate` (§6).

## 8. UI wiring

- **Sidebar** (`MainWindow.axaml` "CUSTOM" section + built-ins section): binds to `ObservableCollection<SmartListSummary>` (`Name`, `MatchCount`), queried live via `SmartListQueryBuilder` when the Smart screen is shown. `+ New Smart List` creates a blank custom `SmartList` and navigates to it.
- **`SmartScreenViewModel(int smartListId)`**: loads the `SmartList` and its `Conditions` for binding; `MatchCountLabel` recomputes reactively as conditions change. `+ Add condition` appends a blank `SmartListCondition` (field picker defaults to `SeriesName`); `✕` removes one; each row's operator dropdown filters to the selected field's `SmartListDataType`. `Save` persists via EF Core; `Cancel` reverts; `Duplicate` clones into a new custom list. System lists render conditions read-only.

## 9. Testing

`Paperbunkr.Data.Tests`:
- One test per `SmartListDataType` evaluator (Text: `Is`/`Contains`/`ContainsAny`/`ContainsAll`/`StartsWith`/`EndsWith`; Number: `GreaterThan`/`LessThan`/`InRange`; Toggle: `Is`; Date: `WithinLastDays`/`InRange`) against seeded in-memory data.
- One test per built-in system list, confirming it matches the same rows CE's formula would (including the `[10,95]` boundary-overlap behavior between Reading/Read/Never-Read).
- Duplicate detection: metadata-key match, file-path match, union/de-dup, confirming no double-counting when both keys match.
- Multi-condition AND composition (2-3 conditions combined, confirming narrowing behavior).
