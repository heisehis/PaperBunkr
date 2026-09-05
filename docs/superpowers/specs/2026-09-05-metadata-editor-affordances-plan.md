# Metadata Editor Affordances — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-05-metadata-editor-affordances-design.md*

## Survey corrections (found while planning, folded into the spec)
- `Issue.AlternateCount` **does not exist** — the "add Alternate Count row" idea is dropped; no
  migration.
- `Book Age` is in the bulk registry but **not** rendered in `IssuePropertiesScreen.axaml`
  (the VM already has `BookAge` + `BookAgeOptions`). Step 5 adds the field.
- `MarkResolver.AliasTable` is `internal` and keyed-only — it exposes no list of canonical age
  ratings. Step 1 adds `AliasTable.Canonicals` + `MarkResolver.AgeRatingCanonicals`.
- No `Avalonia.Xaml.Behaviors` package — behaviors are plain attached properties, modelled on
  `Controls/ContextMenuHost.cs`.
- VMs already carry a `Func<PaperbunkrDbContext> _contextFactory` (default `PaperbunkrDb.CreateContext`,
  test seam overrides it) — the vocab build reuses it, no `MainViewModel` wiring change.

---

## Step 1: Age-rating canonical list on MarkResolver
**Files:** `src/Paperbunkr.App/Services/MarkResolver.cs` (edit)
**What:** In `AliasTable`, collect canonical spellings into a `List<string> _canonicals` during
`Load` (the `p[0]` value of each non-comment row) and expose `IReadOnlyList<string> Canonicals`.
Add `public IReadOnlyList<string> AgeRatingCanonicals => _ageRatings.Canonicals;` to `MarkResolver`.
**Depends on:** none
**Verify:** `MarkResolverTests` — add a case asserting `AgeRatingCanonicals` contains
`"Mature 17+"` and `"Everyone"` and does not contain an alias like `"m17"`.

## Step 2: MetadataVocabularyService
**Files:**
- `src/Paperbunkr.App/Services/MetadataVocabularyService.cs` (new)
- `src/Paperbunkr.App.Tests/MetadataVocabularyServiceTests.cs` (new)

**What:** `VocabField` enum (per design §3.1), `MetadataVocabulary` (indexer
`this[VocabField] -> IReadOnlyList<string>`, plus `Empty`), and
`static MetadataVocabulary Build(PaperbunkrDbContext context)`:
- Project issues once (`AsNoTracking`): the scalar string columns for each `VocabField` +
  `Include(i => i.Tags)` (Genre/Tags) + `Series.Name`/`Series.Titles` for `Series`.
- Separate `context.Characters.Select(c => c.Name)` query for `Characters`.
- List fields (`Writer` Penciller Inker Colorist Letterer CoverArtist Editor Translator Genre
  Tags Characters Teams MainCharacterOrTeam Locations) → split each value with
  `ListFieldTokens.Parse` before dedup. Non-list fields → whole trimmed value.
- Merge static catalogs (union, `OrdinalIgnoreCase`): Format ←
  `FormatSignalCatalog.CeDefaultFormats` ∪ `SpecialFormatCatalog.KavitaOnlyAdditions`; AgeRating ←
  `MarkResolver.Instance.AgeRatingCanonicals`; BookAge ←
  `Enum.GetValues<ComicAge>().Select(a => ComicAgeCatalog.All[a].CeListLabel)`; LanguageIso ←
  `CultureInfo.GetCultures(CultureTypes.NeutralCultures)` mapped to `"{DisplayName} — {twoletter}"`.
- Sort each list `OrdinalIgnoreCase`; drop null/whitespace.
- `SpecialFormatCatalog.KavitaOnlyAdditions` is `internal` in `Paperbunkr.Data` — it is already
  reached from `App` today (BulkFieldDescriptor.cs:131) via `InternalsVisibleTo`, so no change.

**Depends on:** Step 1
**Verify:** new test class (no `[Collection]` needed — pure SQLite, no Avalonia types):
distinct+trimmed+sorted; `"A, B"` writer → tokens `A`,`B` not `"A, B"`; Format list contains a
CE default even with an empty library; empty library → every field `[]`, `Empty` all-empty.

## Step 3: MultiValueAutoComplete behavior
**Files:**
- `src/Paperbunkr.App/Behaviors/MultiValueAutoComplete.cs` (new)
- `src/Paperbunkr.App.Tests/MultiValueAutoCompleteTests.cs` (new)

**What:** Static helper `MultiValueAutoComplete.LastSegment(string text) -> (string prefix, string segment)`
splitting on the final comma (`"A, Fr"` → `("A, ", "Fr")`), and
`Splice(string prefix, string chosen) -> string` (`("A, ", "Frank Q") -> "A, Frank Q, "`), both
reusing/parallel to `ListFieldTokens`. Attached property `Enabled` (bool) on `AutoCompleteBox`:
on attach, set `TextFilter = (search, item) => item.Contains(LastSegment(search).segment, OrdinalIgnoreCase)`
when the segment is non-empty (else no filter), and handle `SelectionChanged` — when an item is
committed, set `Text = Splice(prefix, item)` and move the caret to end. Guard against re-entrancy
with a flag. Registration pattern: `AvaloniaProperty.RegisterAttached` +
`EnabledProperty.Changed.AddClassHandler<AutoCompleteBox>` (see `ContextMenuHost`).
**Depends on:** none
**Verify:** `MultiValueAutoCompleteTests` — `LastSegment`/`Splice` unit cases incl. no-comma,
trailing-comma, empty; leading/trailing whitespace preserved sensibly.

## Step 4: TextSpinner behavior
**Files:**
- `src/Paperbunkr.App/Behaviors/TextSpinner.cs` (new)
- `src/Paperbunkr.App.Tests/TextSpinnerTests.cs` (new)

**What:** Static core `TextSpinner.Step(string text, int delta, int min, int max) -> string`:
- whole trimmed text parses as int → `clamp(n + delta, min, max)`;
- else trailing digit-run → increment it in place, keep prefix (`"Vol 3"`→`"Vol 4"`);
- else leading digit-run → increment it, keep suffix (`"1.MU"`→`"2.MU"`, `"1a"`→`"2a"`);
- else no digits → `min == 0 ? "1" : min.ToString()`.
Attached props on `TextBox`: `Enabled` (bool), `Minimum` (int, default 0), `Maximum` (int,
default `int.MaxValue`). On attach, build a small vertical two-`RepeatButton` control (▲/▼,
styled `.textSpinner` in FormControls.axaml) and assign it to `TextBox.InnerRightContent`; button
clicks call `Step` on `tb.Text` and reassign. Also handle `KeyDown` Up/Down on the TextBox when
`Enabled`. Registration pattern as Step 3.
**Depends on:** none
**Verify:** `TextSpinnerTests` — every `Step` branch incl. clamp at both ends, negative delta,
`"½"` (no digit) → `"1"`.

## Step 5: FormControls.axaml + string/decimal converter
**Files:**
- `src/Paperbunkr.App/Styles/FormControls.axaml` (new)
- `src/Paperbunkr.App/App.axaml` (edit — add `StyleInclude`)
- `src/Paperbunkr.App/Converters/NullableDecimalStringConverter.cs` (new, or into an existing
  converters file if one exists — check `src/Paperbunkr.App/Converters/`)

**What:**
- `FormControls.axaml`: style/`ControlTheme` overrides for `NumericUpDown`, `AutoCompleteBox`
  (popup `Border` + list items), editable `ComboBox`, and `RepeatButton.textSpinner` /
  the `.textSpinner` container — colours strictly from `Pb*` `DynamicResource` brushes
  (`PbSurface2/3Brush`, `PbBorderBrush`, `PbTextBrush`, `PbTextMutedBrush`, `PbAccentBrush`),
  radii from `PbRadiusSm`. No literal hex, no literal sizes where a `PbFontSize*` token fits.
  Add `StyleInclude` to `App.axaml` after `Primitives.axaml`.
- `NullableDecimalStringConverter : IValueConverter` — `string <-> decimal?` (empty/whitespace ↔
  `null`, invariant parse), for binding `NumericUpDown.Value` to the VMs' `string` properties.
**Depends on:** none (used by Steps 6–7)
**Verify:** build; `avalonia-pro-max/review-checklist` pass on the new file (Step 9). Converter
gets a tiny unit test in whichever converters test file exists, else inline in Step 6's tests.

## Step 6: Single-issue editor — XAML + VM
**Files:**
- `src/Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs` (edit)
- `src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml` (edit)
- `src/Paperbunkr.App.Tests/IssuePropertiesScreenViewModelTests.cs` (edit)

**What:**
- VM: in `Load`, after the existing populate, start
  `_vocabTask = Task.Run(() => MetadataVocabularyService.Build(_contextFactory()))` and
  `await`-continue on the UI thread to copy each list into `ObservableCollection<string>`
  properties (`SeriesVocab`… one per `VocabField` actually used on this screen). Until then they
  start empty. Expose `internal Task? VocabularyLoadTask => _vocabTask` for tests.
  Add these to `NonDataProperties` so filling them doesn't trip `_isDirty`.
- VM: `NormalizeLanguage(string) -> string?` — trims; if it case-insensitively matches a neutral
  culture `DisplayName`/`EnglishName` or a `"Name — code"` vocab entry, store the two-letter
  code; else store verbatim (`NullIfEmpty`). Apply in `Save` where `issue.LanguageISO` is set.
- XAML: per design §4 table — swap controls in place inside the existing `WrapPanel` /
  `StackPanel.field` blocks:
  - `NumericUpDown` (+ `NullableDecimalStringConverter`) for Count, Year, Month, Day.
  - `TextBox` + `behaviors:TextSpinner.Enabled` for Number, Volume, Alternate Number,
    Story Arc Number.
  - editable `ComboBox` for Format (replaces the existing `AutoCompleteBox`), Publisher, Imprint,
    Age Rating, Language (ISO); keep the `BrandMark` adornment grids.
  - `AutoCompleteBox` for Title, Alternate Series, Story Arc, Series Group (keep their token
    flyout buttons).
  - `AutoCompleteBox` + `behaviors:MultiValueAutoComplete.Enabled` for Writer, Penciller, Inker,
    Colorist, Letterer, Cover Artist, Editor, Translator, Genre, Tags, Characters, Teams,
    Main Character or Team, Locations.
  - **new field:** Book Age (`StackPanel.field`, editable `ComboBox`, `Text="{Binding BookAge}"`,
    `ItemsSource="{Binding BookAgeVocab}"`) — placed next to Format.
  - Add `xmlns:behaviors="using:Paperbunkr.App.Behaviors"`.
- Tests: `VocabularyLoadTask` completes and populates a known value from the seeded issue;
  `NormalizeLanguage` cases (`"English"`→`"en"`, `"en-US"`→`"en-US"`, `""`→`null`); existing
  numeric round-trip + Cancel-never-writes still green.
**Depends on:** Steps 2, 3, 4, 5
**Verify:** `dotnet test --filter "FullyQualifiedName~IssuePropertiesScreenViewModelTests"`;
build the app (AVLN weave — watch for the CLAUDE.md XAML-compile gotcha since no new `x:Class`
here, only edits, so low risk).

## Step 7: Bulk editor — descriptor model, VM, XAML
**Files:**
- `src/Paperbunkr.App/Models/BulkFieldDescriptor.cs` (edit)
- `src/Paperbunkr.App/ViewModels/BulkFieldViewModel.cs` (edit)
- `src/Paperbunkr.App/ViewModels/BulkIssuePropertiesScreenViewModel.cs` (edit)
- `src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml` (edit)
- `src/Paperbunkr.App.Tests/BulkIssuePropertiesScreenViewModelTests.cs` (edit)

**What:**
- `BulkFieldDescriptor`: add `FieldKind.Numeric`; fields `int NumericMin`, `int? NumericMax`,
  `bool NumericAllowsText`; `VocabField? Vocab`. New factories `Numeric(...)` (→ `FieldKind.Numeric`,
  `NumericAllowsText: false`) and `NumericText(...)` (`true`). Update rows: `Count`/`Year`/`Month`/
  `Day` → `Numeric` with §4.1 bounds; `Number`/`Volume`/`Alternate Number` → `NumericText`. Add
  `Vocab:` to every `isList:true` row + Publisher, Imprint, Story Arc, Series Group, Alternate
  Series, Title, Age Rating, Language (ISO), Format, Book Age.
- `BulkFieldViewModel`: `IsNumericKind`, `IsNumericSpinnerKind`, `IsNumericTextKind`; an injected
  `IReadOnlyList<string> _vocab` (setter pushed by the screen VM) and
  `AutocompleteOptions => (Descriptor.Autocomplete ?? []).Union(_vocab, OrdinalIgnoreCase).OrderBy(...)`;
  `HasAutocomplete` unchanged (`Count > 0`). `SetVocabulary(MetadataVocabulary)` helper.
- `BulkIssuePropertiesScreenViewModel.Load`: after populating fields, same
  `Task.Run(MetadataVocabularyService.Build)` → on UI thread call `field.SetVocabulary(...)` for
  every field whose `Descriptor.Vocab` is set. `internal Task? VocabularyLoadTask`.
  Language normalization: wrap the `Language (ISO)` descriptor's `Set` (or normalize in the
  descriptor factory) via the same helper — extract `NormalizeLanguage` to a shared static
  (`Paperbunkr.App/Models/LanguageNormalizer.cs`) used by both editors.
- `BulkIssuePropertiesScreen.axaml` `FieldRowTemplate`: add a numeric branch
  (`IsVisible="{Binding IsNumericSpinnerKind}"` → `NumericUpDown`;
  `IsVisible="{Binding IsNumericTextKind}"` → `TextBox`+`TextSpinner`), and add
  `behaviors:MultiValueAutoComplete.Enabled="{Binding Descriptor.IsListField}"` to the existing
  `AutoCompleteBox`. Add the `behaviors` xmlns.
- Tests: a `FieldKind.Numeric` row stages on edit and writes an int; a list row's
  `AutocompleteOptions` = union of static list + pushed vocab; `LanguageNormalizer` shared cases.
**Depends on:** Steps 2, 3, 4, 5 (and the shared `LanguageNormalizer` from Step 6 — do that
extraction here if Step 6 hasn't).
**Verify:** `dotnet test --filter "FullyQualifiedName~BulkIssueProperties"` +
`--filter "FullyQualifiedName~BulkFieldRegistry"` (parity tests) + build.

## Step 8: Targeted regression + build
**Files:** none
**What:** Run the field-registry parity + metadata-editor test subsets and a clean app build
(delete `obj/Debug/net10.0/Paperbunkr.App.dll` + `.pdb` first per the CLAUDE.md XAML-weave
gotcha, then `dotnet build src/Paperbunkr.App`). Do **not** run the full App.Tests suite (headless
flake — [[project_paperbunkr_full_suite_headless_flake]]); use filters.
**Depends on:** Steps 6, 7
**Verify:** `dotnet test src/Paperbunkr.Data.Tests`; `dotnet test src/Paperbunkr.App.Tests
--filter "FullyQualifiedName~Metadata|FullyQualifiedName~IssueProperties|FullyQualifiedName~BulkField|FullyQualifiedName~TextSpinner|FullyQualifiedName~MultiValueAutoComplete|FullyQualifiedName~MarkResolver|FullyQualifiedName~ListFieldTokens"`.

## Step 9: UI review checklist + docs + branch
**Files:**
- `docs/ce-feature-inventory.md` (edit — §A "Single-book properties editor" row gains an
  autocomplete/spinner note)
- `docs/alpha-todo.md` (edit — Beta backlog: record what shipped + verified)

**What:** Read `~/.claude/skills/avalonia/avalonia-pro-max/review-checklist/SKILL.md` and run it
against `FormControls.axaml` + both edited `.axaml` files (focus: no hardcoded colours,
`DynamicResource` for every skinnable brush, token-based sizing, theme-reactive). Fix findings.
Update the two docs with verified status (not commit-message claims). Commit on
`claude/metadata-editor-affordances`.
**Depends on:** Step 8
**Verify:** checklist clean; `git log` shows the work; hand back to user for the on-screen pass
(no GUI automation — [[feedback_no_computer_use]]).

---

## Test strategy summary
- **xUnit**, per-test temp SQLite DB with `EnsureCreated()` (existing convention —
  `IssuePropertiesScreenViewModelTests` fixture). Pure-logic helpers (`Step`, `LastSegment`,
  `Splice`, `NormalizeLanguage`, `MetadataVocabularyService.Build`) tested directly with no
  Avalonia dependency and no `[Collection]`.
- VM tests await the new `internal Task? VocabularyLoadTask` seam for determinism.
- No live network. No full-suite run. Manual on-screen verification by the user at the end.
