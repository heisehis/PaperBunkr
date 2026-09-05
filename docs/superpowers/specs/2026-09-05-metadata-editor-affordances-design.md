# Metadata Editor Affordances — Autocomplete, Dropdowns, Numeric Spinners

*Date: 2026-09-05. Polish pass on the shipped metadata editors
(`IssuePropertiesScreen` / `BulkIssuePropertiesScreen`, docs/ce-feature-inventory.md §A). Adds the
three CE editing affordances the original 2026-08-07 editor spec deferred as "a small fast-follow":
smart autocomplete, dropdown pickers for constrained fields, and numeric up/down spinners. Scoped
after a direct audit of CE's real `Dialogs/ComicBookDialog.cs` +
`cYo.Common.Windows/Forms/SpinButton.cs` + `Config/DefaultLists.cs`.*

## 1. Why — and correcting the 2026-08-07 record

The single-book editor spec (docs/superpowers/specs/2026-08-07-issue-properties-editor-design.md §1)
claimed, to justify plain `TextBox`es everywhere:

> "No numeric spinners — CE itself uses plain textboxes with only a `MaxLength` for
> Number/Volume/Count/Year/Month/Day, so Paperbunkr matches that exactly."
> "No autocomplete/suggestion lists … plain text fields for v1, a small deferred fast-follow."

**Both claims are wrong** — verified 2026-09-05 against CE source:

- `ComicBookDialog.cs:225-232` attaches `SpinButton.AddUpDown(...)` to `txVolume`, `txCount`,
  `txNumber`, `txYear`, `txMonth`, `txDay`, `txAlternateCount`, `txAlternateNumber`. The spinner is
  a separate 11px-wide control glued to the textbox's right edge, not a `NumericUpDown`. Its
  increment logic (`SpinButton.cs:398-408`): parse the *entire* text as an int → `clamp(n ±
  increment, min, max)`; if it does **not** parse (e.g. `"1.MU"`) → overwrite the field with the
  `start` default. Per-field params: `txMonth` min 1 / max 12, `txDay` min 1 / max 31, `txCount` /
  `txAlternateCount` min 0 / start 1, `txYear` start = `DateTime.Now.Year`, `txVolume` /
  `txNumber` / `txAlternateNumber` start 1 with `int` bounds.
- `ComicBookDialog.cs:177-200` wires `AutoCompleteMode.SuggestAppend` custom sources on Series,
  Title, Writer, Penciller, Inker, Colorist, Letterer, CoverArtist, Editor, Translator, Genre,
  Tags, AlternateSeries, StoryArc, SeriesGroup, Characters, Teams, Locations,
  MainCharacterOrTeam. Candidates are **distinct values across every book in the library**
  (`DefaultLists.GetComicFieldList`), merged with shipped default lists for Format / AgeRating /
  BookAge / Genre (`GetFormatList` / `GetAgeRatingList` / `GetBookAgeList` — each is
  `library-distinct + DefaultFormats/DefaultAgeRatings/...`). CE matches the *whole* field value
  (`SuggestAppend`), not per-comma-item; multi-value fields additionally get the `<< < > >>`
  transfer-list picker (`ListSelectorControl.Register`, `ComicBookDialog.cs:155-156`) for
  picking individual names.
- CE dropdowns: Format / AgeRating / Publisher / Imprint are editable `ComboBoxEx` (library +
  defaults, still free-typeable); Language is a neutral-cultures `LanguageComboBox`;
  Manga / BlackAndWhite / SeriesComplete are fixed `DropDownList` Yes/No/(RTL)/Unknown.

This spec is the deferred fast-follow, built to match CE's intent with two deliberate
improvements (see §7).

## 2. Scope

**In:** both editors — `IssuePropertiesScreen` (single) and `BulkIssuePropertiesScreen` (bulk),
which share `BulkFieldRegistry`. Per the 2026-09-05 grilling round:

1. Both editors (Q1).
2. Autocomplete candidates = library-learned vocabulary **merged with** the existing static
   catalogs (Q2).
3. Multi-value fields get **per-item** autocomplete — complete the segment after the last comma,
   leave the rest intact (Q3).
4. Dropdowns are **editable** — the list is offered, free text is still accepted and never
   destroyed (Q4).
5. **Every** numeric field gets an up/down affordance (Q5).
6. **No** new per-issue Manga field — Paperbunkr models manga-ness as `Series.ContentType` +
   reading mode, and a per-issue Manga dropdown would be a second conflicting source of truth
   (Q6).

**Out:**

- CE's `<< < > >>` transfer-list picker — per-item autocomplete (§4.4) folds in its purpose.
- Per-issue Manga field, "Include in Updates" / "Proposed Values" combos, the Catalog tab — all
  already excluded by the 2026-08-07 spec, still excluded.
- App-wide vocabulary caching / live invalidation — the vocabulary is rebuilt per editor-open
  (§3), which is rare enough that a cache is not worth its invalidation surface.
- `Series` name editing (still a move-to-different-series operation, its own future feature).

## 3. Architecture — Approach 1 (shared infrastructure + in-place control swaps)

Three shared pieces are built once and consumed by both editors; each editor keeps its own
layout and swaps the inner control per field.

```
                         ┌─────────────────────────────────┐
                         │  MetadataVocabularyService       │  Services/
                         │  Build(DbContext) -> Vocabulary  │
                         └─────────────────────────────────┘
                                    ▲              ▲
              (Task.Run in Load)    │              │   (Task.Run in Load)
              ┌──────────────────────┘              └───────────────────────┐
   ┌──────────────────────────────┐          ┌──────────────────────────────────┐
   │ IssuePropertiesScreenVM      │          │ BulkIssuePropertiesScreenVM      │
   │  + VocabFor(field) lists     │          │  + BulkFieldViewModel.Autocomplete│
   └──────────────────────────────┘          │    merges static + vocab          │
                │                            └──────────────────────────────────┘
                │  binds                                     │  binds
   ┌──────────────────────────────┐          ┌──────────────────────────────────┐
   │ IssuePropertiesScreen.axaml  │          │ BulkIssuePropertiesScreen.axaml  │
   │  TextBox -> AutoCompleteBox / │          │  FieldRowTemplate gains a         │
   │  editable ComboBox /          │          │  numeric branch; AutoCompleteBox  │
   │  NumericUpDown / Text+spinner │          │  branch merges vocab              │
   └──────────────────────────────┘          └──────────────────────────────────┘
                         shared:  Behaviors/MultiValueAutoComplete.cs
                                  Behaviors/TextSpinner.cs
                                  Styles/FormControls.axaml
```

### 3.1 `MetadataVocabularyService` (new — `Paperbunkr.App/Services/MetadataVocabularyService.cs`)

```csharp
public enum VocabField
{
    Series, Title, AlternateSeries, StoryArc, SeriesGroup,
    Publisher, Imprint, Format, AgeRating, BookAge, LanguageIso,
    Writer, Penciller, Inker, Colorist, Letterer, CoverArtist, Editor, Translator,
    Genre, Tags, Characters, Teams, MainCharacterOrTeam, Locations,
}

public sealed class MetadataVocabulary
{
    public IReadOnlyList<string> this[VocabField field] { get; }   // sorted, deduped, never null
}

public static class MetadataVocabularyService
{
    public static MetadataVocabulary Build(PaperbunkrDbContext context);
    public static MetadataVocabulary Empty { get; }               // all fields -> []
}
```

- **One pass over `Issues`** (`AsNoTracking`, projecting only the string columns needed +
  `Tags` for Genre/Tags), plus `Series.Name`/`Series.Titles` for `Series`, plus
  `context.Characters.Select(c => c.Name)` for `Characters` (not reachable from the Issue
  projection). Same shape as `LibraryScreenViewModel.BuildSuggestionIndex`
  (LibraryScreenViewModel.cs:928) — that method is the reference, not a dependency.
- **List fields** (Writer…Locations, Genre, Tags — anything `BulkFieldRegistry` marks
  `IsListField`) are split into individual tokens via `ListFieldTokens.Parse` before dedup, so
  the candidate list is names, not prior comma-strings. This is the per-item improvement over
  CE's whole-value `GetComicFieldList`.
- **Static-catalog merge** (union, `OrdinalIgnoreCase`):
  | VocabField | Static source |
  |---|---|
  | `Format` | `FormatSignalCatalog.CeDefaultFormats` ∪ `SpecialFormatCatalog.KavitaOnlyAdditions` |
  | `AgeRating` | canonical column of `Assets/Marks/age-rating-aliases.tsv` (already parsed by `MarkResolver` — reuse its loaded list, do not re-read the file) |
  | `BookAge` | `Enum.GetValues<ComicAge>().Select(a => ComicAgeCatalog.All[a].CeListLabel)` |
  | `LanguageIso` | `CultureInfo.GetCultures(CultureTypes.NeutralCultures)` — see §4.3 |
  | all others | none — purely library-learned |
- **Sort:** `OrderBy(v => v, StringComparer.OrdinalIgnoreCase)`. Empty/whitespace dropped.
- **Threading:** pure function of its `DbContext` argument, no shared state — safe to call from
  `Task.Run`. Each editor's `Load` starts the build on a threadpool thread and marshals the
  result back (`Dispatcher.UIThread.Post`); until it lands, the fields bind to
  `MetadataVocabulary.Empty` and simply have no suggestions yet (autocomplete degrades to a plain
  editable box — never blocks typing or Save).

### 3.2 `MultiValueAutoComplete` behavior (new — `Paperbunkr.App/Behaviors/MultiValueAutoComplete.cs`)

Attached property, registered the way `Controls/ContextMenuHost.cs` does it
(`AvaloniaProperty.RegisterAttached` + `…Property.Changed.AddClassHandler`) — the codebase has no
`Avalonia.Xaml.Behaviors` package, so this is a plain attached behavior, not an
`Interaction.Behaviors` entry.

```xml
<AutoCompleteBox Text="{Binding Writer}"
                 ItemsSource="{Binding WriterVocab}"
                 behaviors:MultiValueAutoComplete.Enabled="True" />
```

When `Enabled`, the behavior sets `AutoCompleteBox.TextFilter` to a predicate that matches the
candidate against **only the segment after the last comma** in the box's current text, and hooks
`SelectionChanged` so that committing a suggestion splices it into that trailing segment (keeping
`"Grant Morrison, "` prefix, replacing `"Frank Q"` → `"Frank Quitely"`, leaving a trailing `", "`
ready for the next name). Splitting/joining reuses `ListFieldTokens`. `FilterMode` stays
`Contains`; the behavior only narrows *what text* is filtered.

Applied to: Writer, Penciller, Inker, Colorist, Letterer, CoverArtist, Editor, Translator, Genre,
Tags, Characters, Teams, MainCharacterOrTeam, Locations. (Same set as `BulkFieldRegistry`'s
`isList: true` rows — keep them in sync via that flag, §5.2.)

### 3.3 `TextSpinner` behavior (new — `Paperbunkr.App/Behaviors/TextSpinner.cs`)

For the four numeric fields that can legitimately hold non-numeric text — `Number`, `Volume`,
`AlternateNumber`, `StoryArcNumber` (`"1.MU"`, `"½"`, `"2001"`, `"1a"`, `"Vol 3"`). A real
`NumericUpDown` (`decimal?`-bound) would reject those.

Attached property `TextSpinner.Enabled` on a `TextBox` adds a compact two-arrow spinner
(stacked `RepeatButton`s, styled in `FormControls.axaml`) docked to the box's right inner edge
via `TextBox.InnerRightContent`. Click / hold / `Up`+`Down` keys while focused adjust the value:

- If the whole trimmed text parses as an integer → `n ± 1`, clamped to `[min, max]` (attached
  props `TextSpinner.Minimum` / `TextSpinner.Maximum`, default `0` / `int.MaxValue`).
- Else if the text **ends** with a digit run → increment that run in place, preserving the
  prefix (`"Vol 3"` → `"Vol 4"`, `"1a"` — no trailing digit, see next).
- Else if the text **starts** with a digit run → increment that run, preserving the suffix
  (`"1a"` → `"2a"`, `"1.MU"` → `"2.MU"`).
- Else (no digits at all) → set to `min` (or `1` when `min == 0`).

This is deliberately friendlier than CE, which wipes any unparseable value to the default
(§7). Mutates `TextBox.Text` directly; the existing `{Binding}` carries it back to the VM
unchanged.

Real `NumericUpDown` is used for the pure-integer fields instead (§4.2).

### 3.4 `Styles/FormControls.axaml` (new, added to `App.axaml` merged dictionaries)

Control themes / style overrides for `NumericUpDown`, `AutoCompleteBox` (its popup + list item),
editable `ComboBox`, and the `TextSpinner` `RepeatButton`s — all colours from `Pb*` skin brushes
(`PbSurface*`, `PbBorderBrush`, `PbTextBrush`, `PbAccentBrush`, …), **zero literal hex**, so they
react to the runtime skin system. This is the specific defect class the 2026-08-30
`avalonia-pro-max/review-checklist` pass caught; run that checklist again before calling this
done.

## 4. Field → control mapping

The single-issue `Details` tab (`IssuePropertiesScreen.axaml:255-396`) and `Plot & Notes` tab
keep their `WrapPanel` / `StackPanel.field` layout and adornments (BrandMark glyphs, token-insert
flyouts, Genre/Tags "Details" sub-panels). Only the inner editing control changes.

### 4.1 `NumericUpDown` (real — `decimal?` bound through the existing string properties)

| Field | Minimum | Maximum | Notes |
|---|---|---|---|
| Count | 0 | — | CE `txCount` start 1 / min 0 |
| Year | 0 | 9999 | CE start = current year; we keep empty-allowed |
| Month | 1 | 12 | CE `txMonth` |
| Day | 1 | 31 | CE `txDay` (CE's own max is a flat 31, no month awareness — match that) |

**Alternate Count is not added.** CE's dialog has `txAlternateCount`, but `Issue` has no
`AlternateCount` column (verified 2026-09-05 — `IssueToComicInfoMapper` explicitly lists it among
"elements Paperbunkr doesn't model"). Adding it means a schema column + EF migration + write-back
mapping, which is out of proportion to an affordances pass and breaks this spec's "no migration"
constraint. The `of:` box next to Alternate Number in CE's screenshot simply has no Paperbunkr
field behind it yet.

Bound as today: VM keeps `string` properties, `NumericUpDown.Value` ↔ string via a tiny
`decimal?`/`string` converter (empty string ↔ `null`). `Save` still `int.TryParse`s. `NumericUpDown`
gets `AllowSpin="True" ShowButtonSpinner="True" FormatString="0"` and, per the
avalonia-controls/input pitfalls, is bound with no explicit `Mode`.

### 4.2 Text + `TextSpinner` (§3.3)

| Field | Minimum | Notes |
|---|---|---|
| Number | 0 | holds `"1.MU"`, `"½"`, `"0"` |
| Volume | 0 | holds `"2001"` (year-as-volume) and `"3"` alike |
| Alternate Number | 0 | |
| Story Arc Number | 0 | CE attaches no spinner here, but Q5 = "all of them"; harmless |

### 4.3 Editable `ComboBox` (`IsEditable="True"`, `Text="{Binding …}"`, `ItemsSource` = vocab)

| Field | ItemsSource | On save |
|---|---|---|
| Format | `VocabField.Format` | unchanged (`NullIfEmpty`) |
| Age Rating | `VocabField.AgeRating` | unchanged |
| Book Age *(VM has `BookAge` + options already; **not** currently rendered in the single editor's XAML — add the field)* | `VocabField.BookAge` | unchanged |
| Publisher | `VocabField.Publisher` | unchanged |
| Imprint | `VocabField.Imprint` | unchanged |
| Language (ISO) | `VocabField.LanguageIso` | **normalize**: if `Text` case-insensitively equals a neutral culture's `DisplayName` or `EnglishName`, store its `TwoLetterISOLanguageName`; else store `Text` verbatim |
| Color Mode *(already a `ComboBox`)* | `ColorModeOptions` (unchanged, not editable — it is a real closed enum) | unchanged |

`LanguageIso` items render as `"English — en"` (a `{Binding}` `DisplayName` + code) but the
editable `Text` shows/accepts the bare value; the §4.3 save rule bridges the two. Existing
oddball `LanguageISO` strings (`"jp"`, `"en-US"`, `""`) survive untouched because the box is
editable and unmatched text is stored verbatim.

The `Format` field's current control is an `AutoCompleteBox` (IssuePropertiesScreen.axaml:360) —
it becomes an editable `ComboBox` for the visible ▼ affordance, matching CE's `cbFormat`.

### 4.4 `AutoCompleteBox` (type-ahead, no ▼) — single-value

| Field | ItemsSource |
|---|---|
| Title | `VocabField.Title` |
| Alternate Series | `VocabField.AlternateSeries` |
| Story Arc | `VocabField.StoryArc` |
| Series Group | `VocabField.SeriesGroup` |

`FilterMode="ContainsOrdinal"`, `MinimumPrefixLength="1"`. The token-insert flyout buttons on
Title / Alternate Series / Story Arc / Series Group stay exactly as they are (they sit in the
`Grid ColumnDefinitions="*,Auto"` next to the field).

### 4.5 `AutoCompleteBox` + `MultiValueAutoComplete` (§3.2) — per-item

Writer, Penciller, Inker, Colorist, Letterer, Cover Artist, Editor, Translator (Details tab);
Genre, Tags (Details tab — the plain CSV boxes above the "Details" weighted sub-panels);
Characters, Teams, Main Character or Team, Locations (Plot & Notes tab).

### 4.6 Unchanged

Web, Scan Information (single-line `TextBox`); Summary, Notes, Review (multiline); the star-rating
widgets; the `Final issue` checkbox; every read-only Summary-tab field.

## 5. Bulk editor changes

### 5.1 `FieldKind.Numeric` (`Models/BulkFieldDescriptor.cs`)

New `FieldKind` member. `BulkFieldDescriptor` gains `int NumericMin`, `int? NumericMax`,
`bool NumericAllowsText` (true → render Text+`TextSpinner`; false → render `NumericUpDown`). The
existing private `Numeric(...)` factory switches from `FieldKind.Text` to `FieldKind.Numeric`;
`Number`/`Volume` move to a new `NumericText(...)` factory (`NumericAllowsText: true`).

Registry rows affected: `Number`, `Volume` (→ `NumericText`); `Count`, `Year`, `Month`, `Day`
(→ `Numeric` with the §4.1 bounds). `Alternate Number` stays `Text` today in the registry — make
it `NumericText`. (No `Alternate Count` row in the bulk registry today; CE's bulk dialog omits it
too — leave it out of bulk, add it only to the single editor per §4.1.)

### 5.2 Vocabulary wiring (`Models/BulkFieldDescriptor.cs`, `ViewModels/BulkFieldViewModel.cs`)

`BulkFieldDescriptor` gains `VocabField? Vocab`. `BulkFieldViewModel.AutocompleteOptions`
becomes: `static Autocomplete list ∪ vocabulary[Vocab]` (deduped), where the vocabulary is
pushed onto the row VMs by `BulkIssuePropertiesScreenViewModel` once
`MetadataVocabularyService.Build` completes (same `Task.Run` pattern as §3.1). `HasAutocomplete`
stays `AutocompleteOptions.Count > 0` — every list field and the vocab-backed scalar fields now
qualify, so they render `AutoCompleteBox` instead of plain `TextBox`.

Registry rows that gain `Vocab`: every `isList: true` row (Writer…Locations, Genre, Tags), plus
`Publisher`, `Imprint`, `Story Arc`, `Series Group`, `Alternate Series`, `Title`, `Age Rating`,
`Language (ISO)` (the bulk registry has no `Series` row). `Format` / `Book Age` already carry a
static `autocomplete:` list — they additionally get `Vocab` and the union covers both.

The bulk row template renders every vocab-backed field as an `AutoCompleteBox` (no ▼) — it has no
editable-`ComboBox` branch and its rows are width-constrained. So Age Rating / Language / Format /
Publisher / Imprint look slightly different between the two editors (▼ combo in the single editor,
type-ahead box in bulk). Acceptable: the candidate list and free-text behaviour are identical, only
the disclosure affordance differs, and adding a combo branch to the bulk template for five fields
isn't worth it.

### 5.3 `FieldRowTemplate` (`BulkIssuePropertiesScreen.axaml`)

- New branch: `IsVisible="{Binding IsNumericKind}"` → `NumericUpDown` or `TextBox`+`TextSpinner`
  per `Descriptor.NumericAllowsText` (expose two bools `IsNumericSpinnerKind` /
  `IsNumericTextKind` on `BulkFieldViewModel`, same pattern as the existing
  `IsPlainTextKind`/`HasAutocomplete` split).
- Existing `AutoCompleteBox` branch: add `behaviors:MultiValueAutoComplete.Enabled="{Binding
  Descriptor.IsListField}"`.
- The `Age Rating` / `Language (ISO)` rows are plain `Text` today → they become
  `HasAutocomplete` rows automatically once they carry `Vocab`; no enum-picker change (they were
  never `FieldKind.Enum`).

Staging semantics are untouched — `BulkFieldViewModel.OnValueChanged` already auto-stages on any
`Value` change, and `NumericUpDown` / `TextSpinner` both write through `Value`.

## 6. Save paths

- **Single editor** (`IssuePropertiesScreenViewModel.Save`): unchanged except the Language
  normalization (§4.3) — a new `NormalizeLanguage(string) -> string?` helper applied where
  `issue.LanguageISO` is assigned. Everything else still flows `string` → `NullIfEmpty` /
  `ParseInt`.
- **Bulk editor** (`BulkIssuePropertiesScreenViewModel.Save`): unchanged — `FieldKind.Numeric`
  rows still round-trip through the descriptor's existing `Set(issue, string?)`, which already
  `ParseInt`s. Language normalization applied in the `Language (ISO)` descriptor's `Set`.

No migration — every field already exists on `Issue`; this is a pure UI/vocabulary change.

## 7. Deliberate deviations from CE

| CE behavior | Paperbunkr | Why |
|---|---|---|
| Spinner on unparseable text wipes the field to a default | `TextSpinner` increments an embedded digit run in place, preserving prefix/suffix | Losing `"1.MU"` because you brushed the up-arrow is a data-loss papercut |
| Autocomplete matches the whole field value (`SuggestAppend`); a separate `<< < > >>` picker handles individual multi-value names | One `AutoCompleteBox` with per-item (post-last-comma) matching | Folds two widgets into one; the transfer-list picker is a lot of surface for the same job |
| Language is a closed `DropDownList` of cultures | Editable `ComboBox`; unmatched text stored verbatim | Never destroy an existing `LanguageISO` value that isn't a clean culture code |
| `SpinButton` on `txDay` has a flat max of 31 regardless of month | Same (flat 31) | Matching CE; month-aware validation is scope creep for a field most users leave blank |
| Per-issue Manga / BlackAndWhite / SeriesComplete `DropDownList`s | Not added (Manga); already `ColorMode` enum `ComboBox` (B&W); already `Final issue` checkbox (SeriesComplete) | Q6 — Paperbunkr's model already covers these, differently and deliberately |

## 8. Testing

No GUI automation (standing rule — [[feedback_no_computer_use]]). Build + `dotnet test` targeted
filters (full-suite headless run flakes — [[project_paperbunkr_full_suite_headless_flake]]), then a
manual pass by the user.

- **`MetadataVocabularyServiceTests`** (new): distinct + trimmed + sorted; list fields split into
  tokens (a `Writer` of `"A, B"` yields `A` and `B`, not `"A, B"`); static catalog merged for
  Format/AgeRating/BookAge/Language; empty library → all fields `[]` (not null); `Empty` is
  all-empty.
- **`TextSpinnerTests`** (new, plain logic — extract the string-mutation core into a static
  `TextSpinner.Step(string, int delta, int min, int max) -> string`): parse-whole increment +
  clamp; trailing-digit-run increment (`"Vol 3"` → `"Vol 4"`); leading-digit-run increment
  (`"1.MU"` → `"2.MU"`, `"1a"` → `"2a"`); no-digit → `min`/`1`; clamp at both ends.
- **`MultiValueAutoCompleteTests`** (new, plain logic — extract the segment splice into a static
  helper): filter predicate matches only the trailing segment; commit splices into the trailing
  segment and preserves the prefix + adds `", "`.
- **`IssuePropertiesScreenViewModelTests`** (extend): vocabulary lists populate after `Load`
  (await the background build in the test seam); `NormalizeLanguage` — `"English"` → `"en"`,
  `"en-US"` → `"en-US"` (verbatim), `""` → `null`; the existing numeric-field and
  Cancel-never-writes assertions still pass.
- **`BulkIssuePropertiesScreenViewModelTests`** (extend): `FieldKind.Numeric` row stages on
  edit and writes an int; a list-field row's `AutocompleteOptions` is the union of its static
  list and the pushed vocabulary.
- **`avalonia-pro-max/review-checklist`** run on `FormControls.axaml` + both edited `.axaml`
  files before calling it done — specifically the "no hardcoded colours / DynamicResource for
  every skinnable brush" item.

## 9. Files

**New**
- `src/Paperbunkr.App/Services/MetadataVocabularyService.cs`
- `src/Paperbunkr.App/Behaviors/MultiValueAutoComplete.cs`
- `src/Paperbunkr.App/Behaviors/TextSpinner.cs`
- `src/Paperbunkr.App/Styles/FormControls.axaml`
- `src/Paperbunkr.App/Models/StringDecimalConverter.cs` (or fold into an existing converters file)
- test files per §8

**Changed**
- `src/Paperbunkr.App/App.axaml` — merge `FormControls.axaml`
- `src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml` — control swaps
- `src/Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs` — vocab build, vocab list
  properties, `NormalizeLanguage`
- `src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml` — `FieldRowTemplate` numeric branch +
  per-item behavior hook
- `src/Paperbunkr.App/ViewModels/BulkIssuePropertiesScreenViewModel.cs` — vocab build + push
- `src/Paperbunkr.App/ViewModels/BulkFieldViewModel.cs` — numeric-kind bools, merged
  `AutocompleteOptions`
- `src/Paperbunkr.App/Models/BulkFieldDescriptor.cs` — `FieldKind.Numeric`, numeric bounds,
  `VocabField? Vocab`, factory updates

## 10. Rollout

Single commit / PR on a `claude/metadata-editor-affordances` branch. Update
`docs/ce-feature-inventory.md` §A (the "Single-book properties editor" row picks up an
autocomplete/spinner note) and `docs/alpha-todo.md` (Beta backlog) with what was verified, not
just what the commit says. Correct the 2026-08-07 editor spec's §1 bullets in a one-line
pointer to this doc rather than editing history.
