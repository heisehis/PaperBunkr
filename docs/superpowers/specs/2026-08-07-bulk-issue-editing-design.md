# Bulk Issue Metadata Editing

*Date: 2026-08-07. Second slice of docs/ce-feature-inventory.md §A, building directly on the
single-book Issue Properties editor (docs/superpowers/specs/2026-08-07-issue-properties-editor-design.md).
Scoped after reading CE's real `Dialogs/MultipleComicBooksDialog.cs` + `.Designer.cs` source
directly (not the audit summary) - a separate class from the single-book dialog, with its own
mixed-value and list-field diff/merge machinery. A follow-on spec (selection-driven Detail-screen
metadata display + wiring the currently-dead "Edit" button at `DetailScreen.axaml:113`) is
deliberately out of scope here but will reuse this spec's field registry as its data source.*

## 1. Selection UX

The Issues tab's tiles are inert today - no click behavior at all beyond the single-issue
right-click menu just shipped. This spec adds real multi-select:

- `IssueCardSample` becomes an `ObservableObject` (was a plain init-only POCO) and gains
  `IsSelected` (bool). `Id`/`Title`/`CoverBrush`/`CoverImage`/`IsUnread` stay as they are.
- Plain click on a tile toggles its own `IsSelected` - no modifier needed, matches the user's
  explicit choice over requiring Ctrl. Shift-click additionally selects the contiguous range from
  the last-clicked tile to the current one (adds to, doesn't replace, the existing selection) -
  the user's explicit choice to combine both mechanisms.
- Implemented via a `PointerPressed` handler in `DetailTabs.axaml.cs` code-behind (checking
  `e.KeyModifiers` and `IsLeftButtonPressed`), the same kind of direct pointer-event handling
  `PageCanvas` already uses elsewhere in this codebase - not Avalonia's `ListBox`/`SelectedItems`
  machinery, which doesn't bind cleanly enough in this MVVM setup for shift-range semantics.
- `DetailTabsViewModel` tracks selection as `HashSet<int> SelectedIssueIds` plus an
  `int? _lastToggledIndex` for shift-range math. Visual: `Border.issueTile.selected` gets an
  accent border, same convention as `ReaderScreen.axaml`'s `Border.thumb.selected`.
- Selection auto-clears on its own: returning to the Detail screen always goes through
  `ReloadCurrentSeries` → `LoadSeries` → a freshly-constructed `Issues` collection with new
  `IssueCardSample` instances (`IsSelected` defaults to `false`) - no explicit "clear selection"
  step needed anywhere.

## 2. Entry point

Right-click keeps working exactly as it does today, but becomes selection-aware: it always
operates on **the current selection ∪ the right-clicked tile** (so right-clicking a lone,
unselected tile with nothing else selected - today's entire behavior - still just edits that one
issue, unchanged). `DetailTabsViewModel.EditIssueProperties(IssueCardSample issue)` becomes:

```
var ids = (SelectedIssueIds.Count > 0 ? SelectedIssueIds.Append(issue.Id) : new[] { issue.Id }).Distinct().ToList();
if (ids.Count == 1) _goToProperties(ids[0]);   // existing single-book editor
else _goToBulkProperties(ids);                  // this spec's new bulk editor
```

`DetailTabsViewModel` gains a second constructor parameter, `Action<IReadOnlyList<int>>
goToBulkProperties`, threaded the same way `goToProperties` already is (`DetailScreenViewModel` →
`MainViewModel`). New full-screen route `"bulkIssueProperties"`, same `MainViewModel` pattern as
every other screen; `goBack` reuses the existing `GoDetailAfterIssueEdit` (already reloads the
series on return - no new fix needed there).

## 3. Field registry (data-driven, per the user's explicit choice of this over flat repeated
properties)

Verified against CE's real `MultipleComicBooksDialog.Designer.cs`/`.cs` `listFields` set and
control layout - field-by-field, not inferred:

```csharp
public sealed record BulkFieldDescriptor(
    string Label, string Group, FieldKind Kind, bool IsListField,
    Func<Issue, string?> Get, Action<Issue, string?> Set);

public enum FieldKind { Text, Boolean, Rating }
```

One static `BulkFieldRegistry.All` list. Every `Get`/`Set` normalizes to `string?`, reusing the
single-book editor's exact conventions: numeric fields (`Count`/`Volume`/`Year`/`Month`/`Day`, all
`int?`) round-trip through `int.TryParse`/`.ToString()`; scalar text fields null-out on
empty/whitespace on write; `BlackAndWhite` (bool) maps to `"true"`/`"false"`; `Rating`/
`CommunityRating` (`float?`) map to `"0"`-`"5"`.

**Group "Main"** (`FieldKind.Text` unless noted): Number, Count, Volume, Year, Month, Day, Title,
AlternateSeries, AlternateNumber, SeriesGroup, StoryArc, Genre *(list)*, Tags *(list)*, Publisher,
Imprint, Format, AgeRating, LanguageISO, BlackAndWhite *(Boolean)*, Rating *(Rating)*,
CommunityRating *(Rating)*.

**Group "Artists"** (all `IsListField: true`, per CE's own `listFields` set - every one of these
is a comma-separated credit list): Writer, Penciller, Inker, Colorist, Editor, CoverArtist,
Translator, Letterer.

**Group "Plot & Notes"**: MainCharacterOrTeam *(list)*, Characters *(list)*, Teams *(list)*,
Locations *(list)*, Web, ScanInformation, Summary, Notes, Review (last three multiline, same as
the single-book editor).

**Deliberately excluded**, all verified against CE's real field list rather than assumed:
`StoryArcNumber` isn't in CE's bulk dialog at all (not a cut, CE genuinely omits it - matches the
single-book editor's own field set which does include it, an existing minor asymmetry between the
two dialogs that CE itself has). `Series`/`SeriesComplete` (structural series-move/Series-entity
concepts, same reason they're excluded from the single-book editor), `Manga` (no Paperbunkr
equivalent - `ReadingMode` replaces it, see docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md),
`EnableProposed` (scraper automation, no scraper exists), and `AlternateCount` (a real CE field
that was simply never ported to Paperbunkr's `Issue` schema - a pre-existing gap, not something
this spec introduces or needs to fix).

## 4. Field row runtime state and mixed-value / diff-merge mechanics

```csharp
public partial class BulkFieldViewModel : ObservableObject
{
    public BulkFieldDescriptor Descriptor { get; }
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private bool _isStaged;
    internal HashSet<string> OriginalTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase); // list fields only

    partial void OnValueChanged(string value) => IsStaged = true; // auto-stage on edit, matches CE
    public bool BoolValue { get => Value == "true"; set => Value = value ? "true" : "false"; }
    // Star1..Star5 + SetStar1..5Commands: same toggle-to-clear pattern as the single-book editor,
    // meaningful only when Descriptor.Kind == Rating.
}
```

**Load** (given the selected `Issue`s): for each descriptor,
- scalar: if every selected issue's `Get` value agrees, that's the shown `Value`; otherwise blank
  (CE's exact mixed-value convention - blank means "these disagree, touch it to override").
- list field: tokenize every issue's value (comma-split, trimmed, `OrdinalIgnoreCase` `HashSet`)
  and take the **intersection** across all selected issues. That intersection becomes both the
  shown `Value` (rejoined with `", "`) and `OriginalTokens`, to diff against at Save.

Every field starts `IsStaged = false`. `OnValueChanged` (fired by typing, toggling `BoolValue`, or
clicking a rating star) sets it `true` automatically - matches CE's `TextChanged`-driven
auto-check. A field can also be checked/unchecked directly without touching its value, exactly like
CE's generated checkboxes.

**Save**: only `IsStaged` fields do anything, on every selected `Issue`, re-fetched fresh from a
new context (not the buffer):
- scalar: `Descriptor.Set(issue, field.Value)` - a plain overwrite.
- list field: `Added = tokens(field.Value) − OriginalTokens`, `Removed = OriginalTokens −
  tokens(field.Value)`. For each issue, re-tokenize its *own current* value (not the shared
  intersection), `ExceptWith(Removed)`, `UnionWith(Added)`, rejoin, `Descriptor.Set`. This is what
  preserves each book's own credit-list members that never showed up in the shared intersection -
  the exact behavior CE's `Store()` implements, and the reason list fields can't just be a plain
  overwrite like scalar fields.

One shared `ListFieldTokens.Parse`/`.Join` helper backs every list-field descriptor - the payoff of
the data-driven approach over 14 near-duplicate hand-written diff blocks.

## 5. UI

`BulkIssuePropertiesScreen` (new full-screen route), header mirrors the single-book editor's
(`HeaderLabel` = "Editing {N} issues in {Series Name}", Save/Cancel buttons, same `headerAction`
style classes) but with no tab strip - CE's own bulk dialog is one scrolling panel, not tabs, so
this spec matches that rather than inventing a tab split CE itself doesn't have. Three `groupBox`
sections (Main / Artists / Plot & Notes), each an `ItemsControl` bound to a filtered
`ObservableCollection<BulkFieldViewModel>`, sharing **one reusable row `DataTemplate`**:

```
[Staged checkbox] [Label] [ Text/Numeric → TextBox | Boolean → CheckBox | Rating → 5-star row ],
   the three input controls all present but IsVisible-gated on Descriptor.Kind - same
   IsVisible-per-mode convention used everywhere else in this codebase (tab-strip panels, etc.),
   not a new templating mechanism.
```

## 6. Testing

- `IssueCardSampleTests`/`DetailTabsViewModelTests`: click toggles `IsSelected`; shift-click range-
  selects from last-toggled to current; `EditIssuePropertiesCommand` routes to
  `_goToProperties`/`_goToBulkProperties` correctly for 1 vs. 2+ resolved ids (including the
  "right-click an unselected tile with others already selected" union case).
- `BulkFieldRegistryTests` (new): every descriptor's `Get`/`Set` round-trips correctly against a
  real `Issue` for at least one representative field per `FieldKind`/list-vs-scalar combination.
- `ListFieldTokensTests` (new): parse/join round-trip, case-insensitive dedupe, whitespace
  trimming.
- `BulkIssuePropertiesScreenViewModelTests` (new, same edit-buffer/context-factory-seam
  conventions as `IssuePropertiesScreenViewModelTests`):
  - Load computes correct mixed-value blanks vs. agreed values for scalar fields, and correct
    intersections for list fields, across a 2-3-issue seeded selection.
  - Save with a field left unstaged leaves every selected issue's value for that field completely
    untouched.
  - Save with a staged scalar field overwrites it identically on every selected issue.
  - Save with a staged list field applies the correct per-issue delta - explicitly assert that a
    value present on only one issue (outside the shown intersection) survives the diff-merge
    untouched, since that's the entire reason list fields aren't a plain overwrite.
  - Cancel never calls `SaveChanges` (same assertion style as the single-book editor's tests).
- Manual verification: same no-GUI-automation approach as every prior spec - build + run real
  tests, then ask the user to select 2+ issues via click/shift-click, right-click → Edit
  Properties, stage a scalar field and a list field (e.g. add one Writer, leave the rest of the
  Writer list alone), Save, and confirm both the overwritten field and the preserved per-issue
  list members look right back on the Detail screen.
