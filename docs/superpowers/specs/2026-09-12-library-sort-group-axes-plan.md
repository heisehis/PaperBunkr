# Library Sort/Group Axes — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-12-library-sort-group-axes-design.md*

Ground truth confirmed while surveying (not re-derived from the design doc alone):
`IssueListSortField`/`IssueListGroupField` (`src/Paperbunkr.Data/Entities/`) currently have **no**
`VirtualTag`, `NeedsReview`, `PendingProposalCount`, or `IsFinalIssue` members at all — every one of
these is a net-new enum entry, not a rename of something existing. `OpenCount` sort exists;
`OpenCount` group does not. The dynamic-per-tag mechanism mirrors `SmartListField.VirtualTag` +
`SmartListCondition.VirtualTagId` exactly, down to reusing the existing `VirtualTagOption(int Id,
string Name)` readonly record struct (`src/Paperbunkr.App/Models/SmartListOptionLabels.cs:19`) —
no new option type needed. `IssueListFieldCatalogTests.cs` and `SortGroupStrategiesTests.cs`
already exist and set the test-style precedent to mirror.

Working-tree caution: `LibraryScreenViewModel.cs`, `IssuePropertiesScreenViewModel.cs`, and several
other files this plan touches currently have **unrelated uncommitted changes** from a concurrent
session's dispatcher-defer fix. Every edit below must be additive to those files' current on-disk
state, never a reset/discard of them.

## Step 1: Data-layer schema — `IsFinalIssue` tri-state + Virtual Tag sort/group persistence columns
**Files:** `src/Paperbunkr.Data/Entities/Issue.cs` (edit), `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), `src/Paperbunkr.Data/CeMigration/PaperbunkrSidecar.cs` (edit), new EF migration (via `dotnet ef migrations add`)
**What:**
- `Issue.cs:58`: `public bool IsFinalIssue { get; set; }` → `public bool? IsFinalIssue { get; set; }`. No initializer exists today, so this alone gives new rows `null` for free.
- `PaperbunkrSidecar.cs:36`: `public bool IsFinalIssue { get; init; }` → `public bool? IsFinalIssue { get; init; }` (still assigned straight from `issue.IsFinalIssue` at line 62 — no other change needed there).
- `AppSettings.cs`, next to `LibraryIssueListSortField`/`LibraryIssueListGroupField` (lines 193/199): add `public int? LibrarySortVirtualTagId { get; set; }` and `public int? LibraryGroupVirtualTagId { get; set; }` — meaningful only when the paired field enum is `VirtualTag`, matching `SmartListCondition.VirtualTagId`'s shape.
- Generate the migration: `dotnet ef migrations add LibrarySortGroupAxesAndFinalIssueTriState --project src/Paperbunkr.Data --startup-project src/Paperbunkr.App`. **Manually review the generated file** before moving on: the `IsFinalIssue` column change is a nullability change (not a plain `AddColumn`), so EF's SQLite provider will emit a full-table-rebuild `AlterColumn`, not the `AddColumn`/no-op-`Down()` shape this project's other recent migrations use for pure additions (see `20260911011311_AddReaderBackgroundTextureAndSpreadPosition.cs`'s comment on why `Down()` is a no-op there — that reasoning is specific to *added* columns and doesn't automatically transfer). Confirm the generated `Down()` correctly restores a non-nullable `bool` (coalescing existing `NULL`s to `false` is the only sane direction) rather than assuming it's safe unexamined. Do not exercise this via the project's discouraged up-down-up test pattern (`project_paperbunkr_migration_updown_up_test_antipattern` memory) — verify by reading the generated SQL/builder calls directly.
**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.Data` compiles; migration applies cleanly against a fresh DB (`dotnet ef database update` in a scratch copy, or the project's existing migration-test pattern — see Step 8).

## Step 2: `SortGroupStrategies` — tri-state group helper
**Files:** `src/Paperbunkr.App/Models/SortGroupStrategies.cs` (edit)
**What:** Add to `GroupStrategies`:
```csharp
public static (Func<IssueListRow, string> Key, Comparison<string> Order) TriState(
    Func<IssueListRow, bool?> get, string yesLabel, string noLabel, string unknownLabel) =>
    (row => get(row) switch { true => yesLabel, false => noLabel, null => unknownLabel },
     (a, b) => Rank(a).CompareTo(Rank(b)));
// Rank: unknownLabel=0, noLabel=1, yesLabel=2 — matches CE's YesNo ordering (Unknown < No < Yes),
// not alphabetical (existing GroupStrategies.Boolean's ordering would misorder this).
```
No new sort-strategy helper is needed for the tri-state sort itself: `SortStrategies.Numeric<T>`
(line 17) is already generic over any `T : struct, IComparable<T>`, and `bool` satisfies that
constraint — `SortStrategies.Numeric<bool>(r => r.IsFinalIssue)` reuses it as-is, and
`Nullable.Compare`'s existing null-sorts-first semantics already produce the desired
Unknown(null) < No(false) < Yes(true) order.
The `OpenCount` grouper's fixed ranges (0-20/21-50/.../>1000) are a one-off shape used by exactly
one field — write it as an inline lambda directly in the `IssueListFieldCatalog` entry (Step 4),
not as a new shared `GroupStrategies` helper (no second consumer to justify one).
**Depends on:** none
**Verify:** `SortGroupStrategiesTests.cs` gains a case for `TriState` (yes/no/unknown bucketing and rank order) — extend that file following its existing test shapes.

## Step 3: `IssueListRow` — new computed fields + `FromIssue` signature change
**Files:** `src/Paperbunkr.App/Models/IssueListRow.cs` (edit), `src/Paperbunkr.App/ViewModels/IssueListScreenViewModel.cs` (edit, call site only), `src/Paperbunkr.App/Models/SeriesCardSample.cs` (edit, call site only)
**What:**
- Add three properties: `public bool? IsFinalIssue { get; init; }`, `public bool HasPendingProposal { get; init; }`, `public int PendingProposalCount { get; init; }`, `public IReadOnlyDictionary<int, string> VirtualTagValues { get; init; } = new Dictionary<int, string>();`.
- `FromIssue(Issue issue, Series series, Func<int, bool>? isSelected = null, IReadOnlyList<VirtualTagDefinition>? virtualTags = null)` — new optional 4th param, mirroring `DetailBandViewModel.LoadSeries`'s existing `virtualTags` param shape.
- Inside `FromIssue`, add:
  ```csharp
  IsFinalIssue = issue.IsFinalIssue,
  HasPendingProposal = issue.MetadataProposals.Any(p => p.Status == MetadataProposalStatus.Pending),
  PendingProposalCount = issue.MetadataProposals.Count(p => p.Status == MetadataProposalStatus.Pending),
  VirtualTagValues = virtualTags is { Count: > 0 }
      ? virtualTags.ToDictionary(t => t.Id, t => VirtualTagTemplateEvaluator.Evaluate(t.CaptionFormat, issue, series))
      : new Dictionary<int, string>(),
  ```
  (`VirtualTagTemplateEvaluator.Evaluate(string template, Issue issue, Series? series)` — confirmed signature in `src/Paperbunkr.Data/VirtualTags/VirtualTagTemplateEvaluator.cs:30`.) Confirm `Issue.MetadataProposals` is loaded (`.Include`) wherever the issues passed into this pipeline are queried — if not already eager-loaded for Library's issue query, add the `.Include(i => i.MetadataProposals)` there (check `LibraryScreenViewModel`'s issue-loading query in Step 6's survey).
- Update both call sites to pass the new optional param through once the callers themselves are updated (Step 5 threads it from `IssueListScreenViewModel.ToRow`; `SeriesCardSample.cs:245-246`'s `IssueListRow.FromIssue(coverIssue, series)` call can stay 3-arg for now since series cards don't need a per-issue Virtual Tag sort key — leave it unless Step 5's testing shows otherwise).
**Depends on:** Step 1 (`Issue.IsFinalIssue` type), Step 2 not required here.
**Verify:** `dotnet build src/Paperbunkr.App`; existing `IssueListRow`/catalog tests still compile (some may need a `virtualTags: null` default which the optional param already provides, so no call-site breakage expected).

## Step 4: `IssueListFieldCatalog` — new static entries + dynamic Virtual Tag descriptor factories
**Files:** `src/Paperbunkr.App/Models/IssueListFieldCatalog.cs` (edit)
**What:**
- Add three new enum members first (own small edit to `IssueListSortField.cs`/`IssueListGroupField.cs`, done alongside this step since they're only meaningful together): `IssueListSortField.NeedsReview`, `PendingProposalCount`, `IsFinalIssue`, `VirtualTag`; `IssueListGroupField.NeedsReview`, `OpenCount`, `IsFinalIssue`, `VirtualTag`.
- Register in `BuildSortFields()`:
  ```csharp
  [IssueListSortField.NeedsReview] = new(IssueListSortField.NeedsReview, "Needs Review", SortStrategies.Boolean(r => r.HasPendingProposal)),
  [IssueListSortField.PendingProposalCount] = new(IssueListSortField.PendingProposalCount, "Pending Proposals", (a, b) => a.PendingProposalCount.CompareTo(b.PendingProposalCount)),
  [IssueListSortField.IsFinalIssue] = new(IssueListSortField.IsFinalIssue, "Final Issue", SortStrategies.Numeric<bool>(r => r.IsFinalIssue)),
  ```
  `IssueListSortField.VirtualTag` is deliberately **not** registered here — there is no single fixed comparer for it (see the dynamic factory below); `IssueListScreenViewModel.SortRows` special-cases it instead (Step 5).
- Register in the group-fields builder (find/extend the equivalent `BuildGroupFields()` method — read its current shape before editing, it wasn't excerpted above but follows the same `Dictionary<IssueListGroupField, IssueListGroupFieldDescriptor>` pattern as sort fields):
  ```csharp
  [IssueListGroupField.NeedsReview] = new(IssueListGroupField.NeedsReview, "Needs Review", GroupStrategies.Boolean(r => r.HasPendingProposal, "Needs Review", "Up to Date")),
  [IssueListGroupField.OpenCount] = new(IssueListGroupField.OpenCount, "Times Opened", /* inline fixed-range lambda per Step 2's note: 0-20/21-50/51-100/101-200/201-500/501-1000/>1000, ordered by the range's lower bound, not alphabetically (so ">1000" doesn't alpha-sort before "0-20") */),
  [IssueListGroupField.IsFinalIssue] = new(IssueListGroupField.IsFinalIssue, "Final Issue", GroupStrategies.TriState(r => r.IsFinalIssue, "Final issue", "Not final", "Unknown")),
  ```
  `IssueListGroupField.VirtualTag` likewise not registered — dynamic, special-cased in `GroupRows`.
- Add two new public static factory methods for the dynamic case:
  ```csharp
  public static IssueListSortFieldDescriptor BuildVirtualTagSortDescriptor(VirtualTagDefinition tag) =>
      new(IssueListSortField.VirtualTag, tag.Name,
          SortStrategies.CaseInsensitiveString(r => r.VirtualTagValues.GetValueOrDefault(tag.Id)));

  public static IssueListGroupFieldDescriptor BuildVirtualTagGroupDescriptor(VirtualTagDefinition tag) =>
      new(IssueListGroupField.VirtualTag, tag.Name,
          GroupStrategies.Alphabetical(r => r.VirtualTagValues.GetValueOrDefault(tag.Id), fallback: "Unspecified").Key,
          GroupStrategies.Alphabetical(r => r.VirtualTagValues.GetValueOrDefault(tag.Id), fallback: "Unspecified").Order);
  ```
  (Reuses the existing `GroupStrategies.Alphabetical` helper — CE's Virtual Tag grouper is exact-value-per-bucket, case-insensitive alphabetical order, "Unspecified" fallback, which is exactly what `Alphabetical` already does; no new helper needed here, just a non-default fallback string.)
**Depends on:** Step 3 (`IssueListRow.HasPendingProposal`/`PendingProposalCount`/`IsFinalIssue`/`VirtualTagValues`).
**Verify:** `IssueListFieldCatalogTests.cs` gains cases for `NeedsReview`, `PendingProposalCount`, `IsFinalIssue` (sort order + group buckets, including the null/Unknown case) and `OpenCount`'s new grouper (range edges, especially the 20/21 and 1000/1001 boundaries). The two new `BuildVirtualTag*Descriptor` factories get direct unit tests (construct a fake `VirtualTagDefinition`, a couple of `IssueListRow`s with different `VirtualTagValues`, assert sort order and bucketing including the "Unspecified" fallback for a missing dictionary entry).

## Step 5: `IssueListScreenViewModel` — Virtual Tag sort/group wiring
**Files:** `src/Paperbunkr.App/ViewModels/IssueListScreenViewModel.cs` (edit)
**What:**
- New fields/properties: `private IReadOnlyList<VirtualTagDefinition> _availableVirtualTagDefinitions = new List<VirtualTagDefinition>();`, `public IReadOnlyList<VirtualTagOption> AvailableVirtualTags => _availableVirtualTagDefinitions.Select(t => new VirtualTagOption(t.Id, t.Name)).ToList();`, `[ObservableProperty] private int? _sortVirtualTagId;`, `[ObservableProperty] private int? _groupVirtualTagId;`.
- New method `SetVirtualTags(IReadOnlyList<VirtualTagDefinition> tags)` (called by `LibraryScreenViewModel`, Step 6) storing the list and raising `OnPropertyChanged(nameof(AvailableVirtualTags))`, then `Reload()` (a changed enabled-tag set can change previously-cached row values on next render).
- `ToRow` (line 163) becomes `IssueListRow.FromIssue(issue, issue.Series!, _isSelected, _availableVirtualTagDefinitions)`.
- New commands, following `SetSortField`/`SetGroupField`'s existing `[RelayCommand]` shape:
  ```csharp
  [RelayCommand]
  private void SetVirtualTagSortField(VirtualTagOption tag)
  {
      _suppressRender = true;
      try { SortVirtualTagId = tag.Id; SortField = IssueListSortField.VirtualTag; }
      finally { _suppressRender = false; }
      Reload();
  }
  ```
  (mirrors `ConfigureSortGroup`'s suppress-then-single-render pattern, since setting two observable properties independently would otherwise trigger `Reload()` twice via `OnSortFieldChanged`/a new `OnSortVirtualTagIdChanged`). Same shape for `SetVirtualTagGroupField`.
- `SortRows` (line 165): when `SortField == IssueListSortField.VirtualTag`, build the descriptor via `IssueListFieldCatalog.BuildVirtualTagSortDescriptor(tag)` looking `SortVirtualTagId` up in `_availableVirtualTagDefinitions` (fall back to the `Added` default descriptor if the id no longer exists — e.g. the tag was deleted after being selected) instead of the static dictionary lookup. Same shape for `GroupRows` with `GroupField == IssueListGroupField.VirtualTag`.
- `SortFieldLabel`/`GroupFieldLabel` (lines 77-78): extend to return the resolved tag's `Name` when the field is `VirtualTag` (fall back to `"Virtual Tag"` if the id isn't found), so the toolbar pill shows the actual tag name instead of the enum's own name.
**Depends on:** Step 4.
**Verify:** New tests in whatever test file already covers `IssueListScreenViewModel` (check for one — if none exists, add `IssueListScreenViewModelTests.cs` following `LibraryScreenViewModelTests.cs`'s fixture-setup style): setting a virtual-tag sort/group field renders rows ordered/bucketed by that tag's evaluated value; selecting a since-deleted tag id falls back gracefully instead of throwing.

## Step 6: `LibraryScreenViewModel` — load enabled Virtual Tags, feed `IssueList`, persist companion ids
**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit)
**What:** First read the file's current issue-loading query and its `AppSettings` load/save round-trip in full (it has unrelated uncommitted changes from a concurrent session — edit around them, don't reformat or reflow surrounding code). Then:
- Wherever Library's own DB context loads data for a refresh, add a query for enabled `VirtualTagDefinition`s (mirror `SmartScreenViewModel.cs:399-402`'s exact `context.VirtualTagDefinitions.Where(t => t.IsEnabled).OrderBy(t => t.SortOrder)` shape) and call `IssueList.SetVirtualTags(...)` with the result.
- Confirm the issue query feeding `IssueList.SetRows` (line 1160) eager-loads `MetadataProposals` (`.Include(i => i.MetadataProposals)`) — add it if missing, needed by Step 3's `HasPendingProposal`/`PendingProposalCount`.
- Wherever `AppSettings.LibraryIssueListSortField`/`LibraryIssueListGroupField` are currently read/written on load/save, add the paired `LibrarySortVirtualTagId`/`LibraryGroupVirtualTagId` round-trip (read into `IssueList.SortVirtualTagId`/`GroupVirtualTagId` on load, write from them on save) — same pattern as the two existing fields, just doubled up.
**Depends on:** Step 5.
**Verify:** Existing `LibraryScreenViewModelTests.cs` suite still passes unmodified where unrelated; add a case that a saved sort-by-virtual-tag selection round-trips through `AppSettings` (load → still selected on the tag → correct id).

## Step 7: Toolbar UI — dynamic Virtual Tag sort/group entries
**Files:** `src/Paperbunkr.App/Views/LibraryToolbar.axaml` (edit), new small `IMultiValueConverter` (e.g. `src/Paperbunkr.App/Views/VirtualTagFieldActiveConverter.cs`, following this project's existing converter-file placement — check `DetailsCellConverters.cs` for where sibling converters actually live before creating a new file)
**What:** Per this project's mandatory Avalonia-skill rule, `avalonia-data-binding`'s documented `MultiBinding` + custom `IMultiValueConverter` pattern is the mechanism here — the existing `Classes.active="{Binding ..., Converter={x:Static conv:ObjectConverters.Equal}, ConverterParameter={Binding Field}}"` shape (lines 467, 490) only compares one value and can't express "SortField == VirtualTag AND SortVirtualTagId == this tag's id."
- New converter: `IMultiValueConverter` taking `[SortField, SortVirtualTagId, thisTagId]` (or the Group equivalents), returning `true` when the first equals `VirtualTag` and the second equals the third.
- In the Sort tab (after the existing `SortFieldOptions` `ItemsControl`, before the Ascending/Descending buttons), add a new `ItemsControl ItemsSource="{Binding IssueList.AvailableVirtualTags}"` with `x:DataType="models:VirtualTagOption"` (or wherever that type's namespace alias already is in this file), each item a `Button` with `Content="{Binding Name}"`, `Command="...IssueList.SetVirtualTagSortFieldCommand"`, `CommandParameter="{Binding}"`, and `Classes.active` bound via the new `MultiBinding`. Mirror the same shape in the Group tab for `SetVirtualTagGroupFieldCommand`.
- Skip rendering this section entirely (`IsVisible` bound to `AvailableVirtualTags.Count > 0` or similar) when there are no enabled Virtual Tags, so users with none defined see no empty "Virtual Tags" section.
**Depends on:** Step 6.
**Verify:** On-screen check (per this project's standing no-computer-use caveat, flagged explicitly rather than assumed): define 2+ enabled Virtual Tags, confirm they appear as sort and group options, confirm selecting one actually sorts/groups by its evaluated value, confirm the active-highlight follows the specific tag (not just "some virtual tag is active"). Run `avalonia-pro-max/review-checklist` (read `~/.claude/skills/avalonia/avalonia-pro-max/review-checklist/SKILL.md` directly per this project's CLAUDE.md — the router `Skill` call does not work for subskills) before calling this step done.

## Step 8: `IsFinalIssue` tri-state UI + write-back round-trip
**Files:** `src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml` (edit, lines 406-409), `src/Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs` (edit, lines 321, 355, 424, 472, 576, 693)
**What:**
- `IssuePropertiesScreen.axaml:408`: replace `<ToggleSwitch Grid.Column="1" IsChecked="{Binding IsFinalIssue}" OnContent="{x:Null}" OffContent="{x:Null}" />` with `<CheckBox Grid.Column="1" IsThreeState="True" IsChecked="{Binding IsFinalIssue}" />` (same "Final issue" label at line 407, unchanged).
- `IssuePropertiesScreenViewModel.cs`: `[ObservableProperty] private bool _isFinalIssue;` (line 321) → `private bool? _isFinalIssue;`; `FieldClipboard`'s `bool IsFinalIssue` field (line 355) → `bool?`; the three assignment sites (`CopyFields` line 424, `PasteFields`-equivalent line 472, `Load` line 576, `Save`-equivalent line 693) need no logic change since `bool`→`bool?`/`bool?`→`bool?` assignments compile as-is — just confirm after the type change that nothing downstream still assumes non-null (e.g. a `if (IsFinalIssue)` ternary somewhere would need to become a `switch` on `bool?` — grep the file again post-edit for any remaining plain-`bool` usage the initial grep might have missed).
**Depends on:** Step 1.
**Verify:** `dotnet build src/Paperbunkr.App`; on-screen check that the checkbox now cycles through all three visual states and persists correctly (checked/unchecked/indeterminate) across a save+reload.

## Step 9: Write-back and migration test updates
**Files:** `src/Paperbunkr.App.Tests/MetadataFileWriteBackServiceTests.cs` (edit, ~line 102/115), `src/Paperbunkr.Data.Tests/MetadataFileFieldSnapshotTests.cs` (edit, ~line 77/88), `src/Paperbunkr.Data.Tests/CeLibraryMigratorTests.cs` (verify only, ~lines 180/191), new migration test file (e.g. `src/Paperbunkr.Data.Tests/LibrarySortGroupAxesAndFinalIssueTriStateMigrationTests.cs`, following this project's existing migration-test pattern — see `SeriesMetadataProposalsMigrationTests.cs`/`AddIssueTagsMigrationTests.cs` for the exact fixture shape)
**What:**
- The two write-back tests currently do `i.IsFinalIssue = true;` / `Assert.True(parsed.IsFinalIssue);` — these compile unchanged against `bool?` (implicit `bool`→`bool?` conversion on assignment; xUnit's `Assert.True`/`Assert.False` have `bool?` overloads treating `null` as failing). Add one new case per file exercising the `null` (Unknown) state explicitly, since the existing tests only ever cover `true`.
- `CeLibraryMigratorTests.cs:180,191` (`Assert.False(i.IsFinalIssue)` / `Assert.True(i.IsFinalIssue)`) — same "should compile unchanged" expectation; confirm by building, don't assume.
- New migration test: seed a pre-migration row with `IsFinalIssue` as raw SQLite `0`/`1` (following the existing raw-INSERT fixture pattern those other migration tests use), apply this step's migration, assert the value survives unchanged, then insert a fresh post-migration row without specifying the column and assert it reads back `null`.
**Depends on:** Step 1, Step 8.
**Verify:** `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~LibrarySortGroupAxes|FullyQualifiedName~CeLibraryMigrator|FullyQualifiedName~MetadataFileFieldSnapshot"` and `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~MetadataFileWriteBackService|FullyQualifiedName~IssueListFieldCatalog|FullyQualifiedName~SortGroupStrategies"` (targeted filters, not a full-suite run — this project's own memory flags the full `App.Tests` suite as flake-prone headless; targeted filters avoid that while still covering everything this plan touches).

## Explicitly out of scope (per the design doc)
`AlternateCount`/variant tracking — no `IssueEdition` model work here.
