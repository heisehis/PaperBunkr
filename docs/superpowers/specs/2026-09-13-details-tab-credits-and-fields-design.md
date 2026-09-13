# Details tab: full credits + unsurfaced metadata fields

Date: 2026-09-13
Status: Approved for implementation

## Problem

The Series Detail screen's "Details" tab ([DetailTabs.axaml:510-533](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L510-L533)) shows exactly two fields: Publisher and Reading Mode. Two concrete defects motivate this change:

1. **Dead "full credits" link.** The hero band's Credits group shows Writer + Penciller (as "Artist"), capped, with a "full credits ›" button ([DetailBand.axaml:182](../../../src/Paperbunkr.App/Views/DetailBand.axaml#L182)) whose command (`DetailBandGroupViewModel.FullCreditsCommand`) navigates to the Details tab ([DetailScreenViewModel.cs:43](../../../src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs#L43): `() => Tabs.GoDetailsCommand.Execute(null)`). The Details tab has no credits content at all — the link promises more detail and delivers nothing.
2. **Stale Publisher.** `DetailTabsViewModel.LoadSeries` sets `Publisher = string.IsNullOrWhiteSpace(series.Publisher) ? "Unknown" : series.Publisher` ([DetailTabsViewModel.cs:239](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L239)) — it reads only the series-level field. The hero badge row uses `SeriesMetaFields.FromSeries(series).Publisher` ([SeriesMetaFields.cs:22](../../../src/Paperbunkr.App/Models/SeriesMetaFields.cs#L22)), which falls back to the most common `Issue.Publisher` value across the series when `Series.Publisher` is blank. Result: a comic can show its real publisher as a hero badge while the Details tab says "Unknown" for the same series.

Separately, the `Issue` entity carries metadata that is written by ComicInfo.xml import and editable in the bulk/single-issue editors, but has no read surface anywhere in the Detail screen: full credit roles beyond Writer/Penciller, Imprint, Web, Notes, Scan Information, Alternate Series, Series Group, Story Arc Number.

Confirmed **not** part of this gap (already surfaced elsewhere, excluded here to avoid duplication):
- Publisher, Status, Year, Format, Age Rating, Language — hero badge row (`DetailMetaBadge.Build`, [DetailMetaBadge.cs:29](../../../src/Paperbunkr.App/Models/DetailMetaBadge.cs#L29)).
- Genre, Teams, Locations, Characters — hero chip groups (`DetailBandViewModel.BuildGroups`, [DetailBandViewModel.cs:212](../../../src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs#L212)).

## Scope

`DetailTabsViewModel` is embedded identically by:
- `DetailScreenViewModel` (Western/Comic screen) — full tab strip.
- `MangaDetailScreenViewModel` — same `DetailTabsViewModel`, `ShowIssuesTab = false, ShowTabStrip = false` ([MangaDetailScreenViewModel.cs:50](../../../src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs#L50)), Details/Activity content unaffected by that flag.

This change ships identically to both screens with no manga-specific branching — same `Issue` entity, same field names, no separate manga credit vocabulary exists in the data model.

`BookDetailScreenViewModel` does not use `DetailTabsViewModel` at all (Books is a fully independent screen) — out of scope.

## Design

### 1. Publisher fix

`DetailTabsViewModel.LoadSeries` ([DetailTabsViewModel.cs:239](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L239)):

```csharp
// before
Publisher = string.IsNullOrWhiteSpace(series.Publisher) ? "Unknown" : series.Publisher;

// after
Publisher = SeriesMetaFields.FromSeries(series).Publisher ?? "Unknown";
```

No other change — `SeriesMetaFields` is already a public static helper in `Paperbunkr.App.Models`, already referenced from `DetailScreenViewModel`.

### 2. Credits section

New group shown between the existing Publisher/Reading-Mode grid and the External Metadata block ([DetailTabs.axaml:533](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L533), before line 535's `ComicScraperDetailView` content control).

**Roles** (8, not 7 — `BulkFieldRegistry` has one more real role than the hero's capped display or the original bug report accounted for): Writer, Penciller, Inker, Colorist, Letterer, Cover Artist, Editor, Translator. All eight are already-registered `BulkFieldDescriptor` entries in the `Artists` group ([BulkFieldDescriptor.cs:170-177](../../../src/Paperbunkr.App/Models/BulkFieldDescriptor.cs#L170-L177)).

**Aggregation**: series-wide, distinct across every issue in the series — same mechanism `DetailBandViewModel.LoadSeries` already uses for Writer/Artist ([DetailBandViewModel.cs:39-42](../../../src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs#L39-L42), [DetailBandViewModel.cs:181-186](../../../src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs#L181-L186)):

```csharp
private static readonly BulkFieldDescriptor WriterField = BulkFieldRegistry.Find("Writer");
private static readonly BulkFieldDescriptor PencillerField = BulkFieldRegistry.Find("Penciller");
private static readonly BulkFieldDescriptor InkerField = BulkFieldRegistry.Find("Inker");
private static readonly BulkFieldDescriptor ColoristField = BulkFieldRegistry.Find("Colorist");
private static readonly BulkFieldDescriptor LettererField = BulkFieldRegistry.Find("Letterer");
private static readonly BulkFieldDescriptor CoverArtistField = BulkFieldRegistry.Find("Cover Artist");
private static readonly BulkFieldDescriptor EditorField = BulkFieldRegistry.Find("Editor");
private static readonly BulkFieldDescriptor TranslatorField = BulkFieldRegistry.Find("Translator");
```

New `DetailTabsViewModel` members, populated in `LoadSeries` via `CsvFieldAggregator.Distinct(series.Issues.Select(RoleField.Get))`:

```csharp
public ObservableCollection<CreditRoleGroup> CreditRoles { get; } = new();
```

`CreditRoleGroup` — new small record/model (`Paperbunkr.App.Models`): `{ string Label; ObservableCollection<TagPillViewModel> Chips; }`. A role with zero aggregated values across the series is not added to `CreditRoles` at all (same "empty group never shown" convention `DetailBandGroupViewModel`'s doc comment already states).

**Interaction**: each chip is a `TagPillViewModel(value, category: null, IssueTagWeight.Unset, goLibraryWithSearch, reweight: null)` — clicking filters the Library to that credit name. This requires plumbing a `goLibraryWithSearch` callback into `DetailTabsViewModel`, which it does not currently receive (`DetailBandViewModel` gets it directly from `DetailScreenViewModel`'s own constructor param; `DetailTabsViewModel`'s constructor has no equivalent today — see [DetailTabsViewModel.cs:55-71](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L55-L71)). Add an optional trailing `Action<string>? goLibraryWithSearch = null` parameter (default `_ => { }`, same null-object pattern `DetailBandViewModel` uses), forwarded from both call sites:
- `DetailScreenViewModel.cs:42` — already has `goLibraryWithSearch` in scope (its own constructor parameter, currently only forwarded to `Band`).
- `MangaDetailScreenViewModel.cs:50` — same, already has it in scope.

### 3. Additional Details section

New group after Credits, before External Metadata. Label/value rows, one per field, dropped entirely when blank across the series:

| Label | Source |
|---|---|
| Imprint | `BulkFieldRegistry.Find("Imprint")` |
| Web | `BulkFieldRegistry.Find("Web")` |
| Notes | `BulkFieldRegistry.Find("Notes")` |
| Scan Information | `BulkFieldRegistry.Find("Scan Information")` |
| Alternate Series | `BulkFieldRegistry.Find("Alternate Series")` |
| Series Group | `BulkFieldRegistry.Find("Series Group")` |
| Story Arc Number | `Issue.StoryArcNumber` directly — **not** in `BulkFieldRegistry` (CE deliberately excludes it from the bulk editor, per [BulkFieldDescriptor.cs:48-51](../../../src/Paperbunkr.App/Models/BulkFieldDescriptor.cs#L48-L51)); this is read-only display, so bypassing the registry for this one field is fine. |

**Aggregation**: same `CsvFieldAggregator.Distinct` over all issues. Unlike Credits, these are scalar (non-list) fields — when aggregation yields more than one distinct value, join with `", "` for display (matches how `SeriesMetaFields.SingleDistinct` prefers a single value but this UI still needs to show something when issues disagree, rather than silently picking one). Each row is plain text except **Web**, which renders as a clickable hyperlink when the aggregated value is a single distinct URL; when multiple issues carry different Web values, render as plain (non-clickable) joined text — a clickable link needs exactly one unambiguous target.

Opening the link reuses the exact `Process.Start` pattern already established for outbound URLs in this codebase ([PreferencesScreenViewModel.cs:2482](../../../src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs#L2482), used for tracker OAuth sign-in): `Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })` wrapped in try/catch (no shell/browser available is silently swallowed, matching every existing call site of this pattern — none of them surface a failure to the user). Correction from an earlier draft of this section: `ExternalLinks`/`TrackerLinks` chips elsewhere in this same tab do **not** open URLs on click today (checked [DetailTabs.axaml:604-623](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L604-L623) — they only show a provider `BrandMark` + an unlink button), so this Web-link command is new, not reused.

New `DetailTabsViewModel` members:

```csharp
public ObservableCollection<DetailFieldRow> AdditionalDetails { get; } = new();
```

`DetailFieldRow` — new small model: `{ string Label; string Value; bool IsLink; }`.

### 4. Layout (XAML)

Both new sections follow the existing "icon + caption label + content" pattern already used for Publisher/Reading Mode ([DetailTabs.axaml:511-532](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L511-L532)) and the section-header pattern External Metadata/Trackers use ([DetailTabs.axaml:542-553](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L542-L553)): `fi:SymbolIcon` + 10.5px caption `TextBlock` in `PbTextFaintBrush`, `Margin="0,24,0,0"` between sections (matches existing section spacing throughout this file), `Spacing="8"` within a section.

- **Credits**: one row per populated role — caption label (`TextBlock`, `PbTextFaintBrush`) then a `WrapPanel` of chips (reusing the existing pill visual — same template shape as the hero band's own credit chips, a `Border` classed like `TagPillViewModel`'s existing consumers).
- **Additional Details**: one row per populated field — caption label then a value `TextBlock` (or a `Button`-styled hyperlink for Web when `IsLink`).
- Inserted as a new `ItemsControl ItemsSource="{Binding CreditRoles}"` and `ItemsControl ItemsSource="{Binding AdditionalDetails}"`, each with its own `IsVisible="{Binding CreditRoles.Count, Converter=...GreaterThanZero}"` (or a `HasCreditRoles`/`HasAdditionalDetails` bool property, consistent with this file's existing `HasExternalLinks`/`HasTrackerLinks` convention) so the whole section vanishes rather than rendering an empty header when a series has literally nothing in that group (e.g. a comic with zero credits data at all).

## Data flow

`LoadSeries(Series series)` ([DetailTabsViewModel.cs:204](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L204)) gains, alongside the existing `Publisher` line:

```csharp
CreditRoles.Clear();
AddCreditRole("Writer", WriterField);
AddCreditRole("Penciller", PencillerField);
AddCreditRole("Inker", InkerField);
AddCreditRole("Colorist", ColoristField);
AddCreditRole("Letterer", LettererField);
AddCreditRole("Cover Artist", CoverArtistField);
AddCreditRole("Editor", EditorField);
AddCreditRole("Translator", TranslatorField);
OnPropertyChanged(nameof(HasCreditRoles));

AdditionalDetails.Clear();
AddDetailField("Imprint", ImprintField);
AddDetailField("Web", WebField, isLinkCandidate: true);
AddDetailField("Notes", NotesField);
AddDetailField("Scan Information", ScanInformationField);
AddDetailField("Alternate Series", AlternateSeriesField);
AddDetailField("Series Group", SeriesGroupField);
AddDetailField("Story Arc Number", i => i.StoryArcNumber);
OnPropertyChanged(nameof(HasAdditionalDetails));
```

`AddCreditRole`/`AddDetailField` are small private helpers mirroring `DetailBandViewModel.AddGroup`'s "only add when non-empty" pattern ([DetailBandViewModel.cs:245-251](../../../src/Paperbunkr.App/ViewModels/DetailBandViewModel.cs#L245-L251)). Runs once per `LoadSeries` call, no selection-change re-aggregation (see below).

## Explicitly not doing (scope boundary from grilling)

- **No focus-aware re-aggregation.** `DetailBandViewModel` has a `LoadSeries`/`LoadIssue` split driven by `DetailScreenViewModel.RefreshForSelection` reacting to `Tabs.SelectedIssueIds`. The Details tab's new Credits/Additional Details content always shows the series-wide aggregate, regardless of which issue (if any) is focused in the Issues tab. Wiring per-issue focus into a second ViewModel was considered and rejected as scope creep for this change.
- **No changes to the hero band's own Credits group.** It keeps showing only Writer + "Artist" (Penciller), capped, with the same "full credits ›" link — that link now actually goes somewhere useful.
- **No confirm-before-navigate on the Web link.** Opening an external URL from already-imported, user-owned comic metadata is a one-tap action, consistent with how `ExternalLinks`/`TrackerLinks` already work in this same tab.

## Testing

Extend `DetailTabsViewModelTests`:
- `LoadSeries` with `Series.Publisher` blank and one issue carrying `Issue.Publisher = "DC Comics"` → `Publisher == "DC Comics"` (not `"Unknown"`).
- `LoadSeries` with two issues, one `Writer = "Alice"` one `Writer = "Bob"` → `CreditRoles` contains a "Writer" entry with both, deduped/case-insensitive per `CsvFieldAggregator.Distinct`'s existing contract.
- A role with zero non-blank values across every issue → no entry added to `CreditRoles` for that role; `HasCreditRoles` false when all roles are empty.
- `AdditionalDetails` a field blank on every issue → no row added; a field with a single distinct value → shown as-is; two issues disagreeing on `Web` → joined text, `IsLink == false`; a single distinct `Web` value → `IsLink == true`.
- Clicking a credit chip invokes the `goLibraryWithSearch` callback passed into the constructor with the chip's value (mirrors existing `TagPillViewModel` click tests elsewhere in this file).

No new UI-automation coverage required beyond what `DetailScreenViewModelTests`/`MangaDetailScreenViewModelTests` already exercise for the Details tab's visibility toggles — extend those only if the self-review pass on the implementation finds a real gap.
