# Library Search Suggestions

*Date: 2026-08-31.*

## Problem

The Library search box (`LibraryToolbar.axaml`, bound to `LibraryScreenViewModel.SearchQuery`) is a
plain `TextBox` with no assistance: no memory of past queries, no help finding a value that exists
in the library, no shortcut into an existing Smart List, and no visibility into the 7-mode field
scope (`SearchMode`) beyond a separate popup button.

CE's own search box has exactly one form of "suggestion": a WinForms `AutoCompleteMode.Suggest` box
fed from the user's own past typed queries (`SearchTextBox.cs`, persisted as
`Program.Settings.LibraryQuickSearchList`). CE has **no field-value autocomplete on the search box**
— its only field-value autocomplete lives inside the metadata *editor* dialogs
(`ComicBookDialog.cs`), pulling distinct values via `Program.Lists.GetComicFieldList`.

This spec adds four suggestion types to the Library search box, one of which (recent searches) is a
legitimate CE-parity feature Paperbunkr is missing, and three of which
(value autocomplete, saved-search shortcuts, field-prefix hints) are deliberate deviations from CE,
scoped to fit the app's existing search architecture rather than inventing a new one.

## Scope

**In scope:**

1. Recent searches — a persisted, capped list of past committed queries, shown/filtered in a
   suggestions dropdown.
2. Value autocomplete — suggest matching Series/Writer/Artist/Descriptive/Catalog field values as
   the user types, scoped to the active `SearchMode`.
3. Saved-search shortcuts — surface matching Smart Lists (via their `Collection.IsSmart` wrapper) by
   name, selecting one navigates to that collection.
4. Field-prefix hints — typing `<mode>:` at the start of the query switches `SearchMode` inline;
   hint rows offer to insert a mode prefix.
5. A single unified suggestions `Popup` on the search `TextBox`, keyboard-operable (Up/Down/Enter/Escape).

**Explicitly out of scope:**

- Any multi-field query grammar (e.g. `writer:miller tag:action` combined) — `SearchMode` remains a
  single active scope, matching the existing `MatchesSearch` architecture. Field prefixes are
  shorthand for picking a mode, not a new grammar.
- A Preferences toggle for suggestions on/off — CE has no such surface for its own equivalent either.
- Suggesting File-mode values (file paths) — no sensible autocomplete source; File mode still gets
  recent-search and field-hint rows, just no value matches.
- Reading Lists as a "saved search" source — they're manually curated, not rule-based; only
  `IsSmart` collections qualify.

## Data model

### `AppSettings.LibraryRecentSearches` (new)

```csharp
/// <summary>JSON-serialized List&lt;string&gt;, most-recent-first, capped at 8, case-insensitive
/// dedup. Null/empty means no history yet. See docs/superpowers/specs/
/// 2026-08-31-library-search-suggestions-design.md.</summary>
public string? LibraryRecentSearches { get; set; }
```

No new migration table — a single nullable string column on the existing `AppSettings` singleton
row, same pattern as `LibraryDetailsColumns`. JSON (not comma-join) because a query can legally
contain a comma; `LibraryDetailsColumns`'s comma-join is safe only because enum names never contain
one.

### `SearchSuggestion` (new, `Paperbunkr.App.Models`)

```csharp
public enum SearchSuggestionKind { Recent, Value, SavedSearch, FieldHint }

public record SearchSuggestion(
    SearchSuggestionKind Kind,
    string DisplayText,      // shown in the row
    string? InsertText,      // what SearchQuery becomes on select (Recent/Value/FieldHint); null for SavedSearch
    int? CollectionId);      // set only for SavedSearch
```

One flat list (`ObservableCollection<SearchSuggestion>` on `LibraryScreenViewModel`), rendered with
section headers derived from `Kind` grouping in the `ItemsControl`'s `DataTemplate` — no separate
header model needed, matching how `IssueList`'s group headers are already derived at render time
rather than materialized as list entries.

## Suggestion computation

Recomputed synchronously on every `SearchQuery` change (the existing `OnSearchQueryChanged` already
calls `RebuildView()` on every keystroke with no debounce, because filtering is in-memory-only —
suggestion computation joins that same handler, same constraint).

Total capped at 12 rows across sections (Recent ≤5, Value ≤6, SavedSearch ≤4, FieldHint ≤3 — soft
caps, first-fit until the 12 total is reached, in that priority order) so the popup never grows
unbounded.

1. **Field hints** — if `SearchQuery` is empty, or doesn't yet contain `:`, and its text up to the
   cursor is a case-insensitive prefix of a mode keyword (`all`, `series`, `writer`, `artists`,
   `descriptive`, `file`, `catalog`), add one hint row per matching keyword,
   `InsertText = "<keyword>: "`. If `SearchQuery` already matches `^(\w+):` against a known keyword,
   parse it immediately instead (see below) rather than showing a hint for it.

2. **Recent searches** — from `AppSettings.LibraryRecentSearches`, filtered to entries containing
   the typed text (case-insensitive, substring match — not startswith-only, since a remembered
   query is often longer than what's retyped), most-recent-first. Full list shown when
   `SearchQuery` is empty.

3. **Value matches** — from `_suggestionIndex` (see below), scoped to the *current* `SearchMode`
   (or the mode implied by an already-parsed field prefix). Ranked startswith-before-contains,
   case-insensitive, deduped, capped 6. Skipped entirely for `SearchMode.File`.

4. **Saved searches** — `Collections.Where(c => c.IsSmart)` (already loaded in-memory on the
   ViewModel) whose `Name` contains the typed text, capped 4.

### Field-prefix parsing

On every `SearchQuery` change, before anything else: if the text matches
`^(all|series|writer|artists|descriptive|file|catalog):\s*(.*)$` (case-insensitive), set
`SearchMode` to the matched enum value and treat group 2 as the effective query for suggestion
purposes and for `MatchesSearch`. This mirrors clicking the existing `SearchModeButton` popup — same
persisted-mode side effect — so a typed prefix and a clicked mode selection stay behaviorally
identical. `SearchQuery` itself is **not** rewritten to strip the prefix (the user keeps seeing what
they typed); the parser is purely a read-time transform feeding `MatchesSearch` and the suggestion
list.

### `_suggestionIndex` (new field on `LibraryScreenViewModel`)

```csharp
private Dictionary<SearchMode, List<string>> _suggestionIndex = new();
```

Built once per library (re)load, in the same pass that already produces `_allSeries` (`RebuildView`
already snapshots what it needs from the DB-loaded `context`; this index is derived from that same
in-memory snapshot, not a fresh query). Sources per mode, using
`SearchFieldBundleCatalog`'s existing field groupings so this stays in lockstep with what
`MatchesSearch` itself actually searches:

- `Series` → `Series.Name`, `Series.Titles[].Value`, `AlternateSeries`, `SeriesGroup`, `StoryArc`.
- `Writer` → distinct `Issue.Writer`.
- `Artists` → distinct values across `Writer, Penciller, Inker, Colorist, Editor, Translator, Letterer, CoverArtist`.
- `Descriptive` → `IssueTag` names (first-class entity, no scan needed) + `Character.Name`
  (first-class entity, already indexed per the Phase 4g work) + distinct `Locations`.
- `Catalog` → distinct `BookOwner, BookStore, BookLocation`.
- `All` → union of all of the above.

Rebuilt on the same trigger as `_allSeries` (library reload/import/edit events already invalidate
and rebuild that snapshot) — never per-keystroke.

## Recording a completed search

The existing `_searchHistoryDebounceTimer` (`SearchHistoryDebounce`, 800ms, already used to push a
Back/Forward navigation-history step once typing settles) gains one more responsibility on the same
tick: if `SearchQuery` is non-empty and non-whitespace, prepend it to `LibraryRecentSearches`
(case-insensitive dedup — remove any existing equal entry first), cap the list at 8, persist via the
existing `AppSettings` save path. No new timer, no new trigger point.

A **Clear recent searches** link renders inside the Recent section of the dropdown itself (visible
only when that section is non-empty) rather than a Preferences toggle, per Scope.

## Selecting a suggestion

- **Recent / Value / FieldHint** → `SearchQuery = suggestion.InsertText`, close popup, refocus the
  `TextBox` with the caret at the end (so a FieldHint selection like `"writer: "` leaves the user
  ready to keep typing the value).
- **SavedSearch** → `SearchQuery = string.Empty`, close popup, call the existing
  `SelectCollectionById(suggestion.CollectionId!.Value)` — reusing the sidebar-collection selection
  path already on this ViewModel; no new navigation/query code.

Click and Enter (on the currently `SelectedSuggestionIndex`) both invoke the same selection method.
Enter with no suggestion selected commits the raw typed text as a normal search (today's existing
behavior, unchanged) and still feeds the recent-searches debounce above. Escape closes the popup
without changing `SearchQuery`.

## UI

New `Popup` in `LibraryToolbar.axaml`, `PlacementTarget` = the search `TextBox`'s parent `Border`
(the same element the box already lives in), `Placement="Bottom"`, `IsLightDismissEnabled="True"` —
following the exact `Border.dropdown` → `StackPanel`/`ItemsControl` shape already used for the
SearchMode/Filter/ViewSort/AddToList popups in the same file (`LibraryToolbar.axaml:274-466`), not
`AutoCompleteBox`. This app already hand-rolls every dropdown this way, and a different built-in
popup-based control (`ContextMenu`) is on record in this codebase as not rendering in this Avalonia
build (`project_paperbunkr_context_menu_rebuild` — a real prior defect, not a hypothetical) —
reusing the proven pattern avoids risking a repeat.

`IsOpen` bound to a new `IsSuggestionsOpen` (true when the `TextBox` has focus **and**
`SearchSuggestions.Count > 0`). Rows use a `DataTemplate` keyed on `Kind` for icon choice (clock =
Recent, magnifier = Value, bookmark = SavedSearch, tag = FieldHint — `FluentIcons`, matching the
rest of the toolbar's icon usage), with a muted `TextBlock` section header (reusing the existing
`dropdownRow` style class) inserted before the first row of each `Kind` group present.

Keyboard: `TextBox.KeyDown` handles `Down`/`Up` (move `SelectedSuggestionIndex`, clamped not
wrapped, matching how the existing `View & Sort` popup's tab strip behaves — no established wrap
convention elsewhere in this toolbar to deviate from), `Enter` (commit, see above), `Escape`
(close). Click on a row commits that row directly regardless of `SelectedSuggestionIndex`.

## Error handling

- `LibraryRecentSearches` JSON fails to deserialize (corrupted/manually-edited settings row): treated
  as empty history, not a startup crash — same defensive posture as every other nullable
  `AppSettings` string field in this codebase.
- A `Collection` referenced by a stale `SavedSearch` suggestion (deleted between suggestion list
  build and click — a multi-second window, low likelihood but not impossible with the popup open):
  `SelectCollectionById` already falls back to "All Series" for a missing id (existing behavior,
  documented on `AppSettings.LibraryActiveCollectionId`), so this is a pre-solved case, not new
  handling.
- An unrecognized or partially-typed field prefix (e.g. `wri:` or `writer` with no colon) is simply
  not parsed — falls through to a normal free-text search against the current `SearchMode`, same as
  today's behavior for any other text.

## Testing

- **`LibraryScreenViewModelTests`** (extend existing file):
  - Recent-search debounce: append on settle, cap at 8, case-insensitive dedup moves an existing
    entry to the front rather than duplicating it, empty/whitespace query never recorded.
  - Field-prefix parsing: each of the 7 keywords recognized case-insensitively, `SearchMode` set,
    unrecognized prefix falls through to free-text search, prefix with no colon ignored.
  - Value-match ranking: startswith beats contains, capped at 6, `SearchMode.File` yields none,
    `SearchMode.All` unions every field group.
  - Saved-search filtering: only `IsSmart == true` collections appear, name-substring match.
  - Suggestion cap: total rows never exceed 12 with all four sources populated.
  - Keyboard nav: `SelectedSuggestionIndex` clamps at both ends; Enter with no selection commits raw
    text; Escape leaves `SearchQuery` untouched.
  - Selection side effects: Recent/Value/FieldHint set `SearchQuery` only; SavedSearch clears
    `SearchQuery` and calls `SelectCollectionById` with the right id.
- **UI automation (FlaUI)** smoke test, extending the existing Library harness: type a partial
  series name, arrow down to a Value suggestion, Enter, assert `SearchQuery` updated and the grid
  filtered; separately, select a SavedSearch suggestion and assert the active collection changed.

## Roadmap

Note in `docs/alpha-todo.md` once landed — this isn't tracked there today (checked; no prior mention
of search suggestions/autocomplete in that doc or `docs/ce-feature-inventory.md`).
