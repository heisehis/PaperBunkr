# Issue.AlternateCount — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-12-issue-alternate-count-design.md*

## Step 1: Entity column
**Files:** `src/Paperbunkr.Data/Entities/Issue.cs` (edit)
**What:** Add `public int? AlternateCount { get; set; }` immediately after `AlternateNumber` (line 47),
matching the `Count` field's nullable-int convention.
**Depends on:** none
**Verify:** compiles.

## Step 2: EF migration
**Files:** new migration under `src/Paperbunkr.Data/Migrations/` (generated), `PaperbunkrDbContextModelSnapshot.cs` (regenerated)
**What:** `dotnet ef migrations add AddIssueAlternateCount --project src/Paperbunkr.Data`. Confirm the
scaffolded `Up()` adds one nullable `int` column and `Down()` is a real `DropColumn` (expected by
default for a brand-new column — no hand-editing needed, unlike the no-op-`Down()` cases elsewhere
in this codebase).
**Depends on:** Step 1
**Verify:** migration file reviewed by eye; `dotnet ef database update` not required for tests (they
use `EnsureCreated`/in-memory contexts per existing test patterns).

## Step 3: ComicInfo.xml round-trip
**Files:** `src/Paperbunkr.Data/CeMigration/CeLibraryMigrator.cs` (edit),
`src/Paperbunkr.Data/CeMigration/IssueToComicInfoMapper.cs` (edit)
**What:**
- `CeLibraryMigrator.MapStoryFields`: add `issue.AlternateCount = Pick(issue.AlternateCount, info.AlternateCount > 0 ? info.AlternateCount : null);` next to the existing `AlternateNumber` line (~522).
- `IssueToComicInfoMapper.Apply`: add `target.AlternateCount = issue.AlternateCount ?? 0;` next to the existing `AlternateNumber` line (~40) — plain field, not an `Effective*` one (matches `AlternateSeries`/`AlternateNumber`, not `Count`, since it isn't proposal-backed).
- Update `IssueToComicInfoMapper`'s class-doc comment (line ~14) to drop `AlternateCount` from the list of unmodeled elements.
**Depends on:** Step 1
**Verify:** `Paperbunkr.Data.Tests` build.

## Step 4: IssueListRow projection
**Files:** `src/Paperbunkr.App/Models/IssueListRow.cs` (edit)
**What:** Add `public int? AlternateCount { get; init; }` after `AlternateNumber` (~line 97), and
`AlternateCount = issue.AlternateCount,` in the factory initializer after the `AlternateNumber =` line
(~221).
**Depends on:** Step 1
**Verify:** compiles.

## Step 5: Library sort field
**Files:** `src/Paperbunkr.Data/Entities/IssueListSortField.cs` (edit),
`src/Paperbunkr.App/Models/IssueListFieldCatalog.cs` (edit)
**What:**
- Enum: add `AlternateCount` after `AlternateNumber` (line 70).
- `SortFields`: add `[IssueListSortField.AlternateCount] = new(IssueListSortField.AlternateCount, "Alternate Count", SortStrategies.Numeric(r => r.AlternateCount)),` after the `AlternateNumber` entry (~line 108).
- `Col(...)` block: add `Col(IssueListSortField.AlternateCount, r => r.AlternateCount?.ToString(CultureInfo.InvariantCulture));` after the `AlternateNumber` line (~187).
**Depends on:** Step 4
**Verify:** `IssueListFieldCatalogTests`.

## Step 6: Library group field + shared count-bucket helper
**Files:** `src/Paperbunkr.Data/Entities/IssueListGroupField.cs` (edit),
`src/Paperbunkr.App/Models/IssueListFieldCatalog.cs` (edit)
**What:**
- Enum: add `AlternateCount` after `AlternateNumber` (line 56).
- Generalize the existing `OpenCountRanges`/`OpenCountBucketKey`/`OpenCountBucketOrder` (lines
  324–333) into shared `CountRanges`/`CountBucketKey(int? value)`/`CountBucketOrder`, where
  `CountBucketKey` returns `"Unspecified"` for `null` (CE's own 8th `CountGroups` bucket, previously
  dropped because `OpenCount` — `int`, never null — couldn't reach it) and `CountBucketOrder` ranks
  `"Unspecified"` first.
- `OpenCount`'s `GroupFields` entry (line 320) switches to `r => CountBucketKey(r.OpenCount)` /
  `CountBucketOrder` (behavior-identical for it — still never sees `"Unspecified"`).
- Add `[IssueListGroupField.AlternateCount] = new(IssueListGroupField.AlternateCount, "Alternate Count", r => CountBucketKey(r.AlternateCount), CountBucketOrder),` after the `AlternateNumber` entry (~line 295).
- Update the comment above (lines 317–319) to describe the shared, nullable-aware helper instead of
  just `OpenCount`'s dropped-bucket case.
**Depends on:** Step 4
**Verify:** `IssueListFieldCatalogTests` (new + existing `OpenCount` bucket tests must still pass
unchanged — confirms the refactor is behavior-preserving for `OpenCount`).

## Step 7: Issue Properties editor
**Files:** `src/Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml` (edit)
*(load `avalonia-xaml` subskill guidance before the `.axaml` edit — already consulted this session)*
**What:**
- ViewModel: `[ObservableProperty] private string _alternateCountText = string.Empty;` after
  `_alternateNumber` (line 210); add `AlternateCountText` to the `FieldClipboard` record, `CopyFields`,
  `PasteFields` (mirroring `CountText` in each); `Load`: `AlternateCountText = issue.AlternateCount?.ToString() ?? string.Empty;` after the `AlternateNumber` line (~552); `Save`:
  `issue.AlternateCount = ParseInt(AlternateCountText);` after the `AlternateNumber` line (~669).
- View: new field row after "ALTERNATE NUMBER" (line 307), same `NumericUpDown` +
  `NullableDecimalStringConverter` idiom as "COUNT" (line 260):
  ```xml
  <StackPanel Classes="field"><TextBlock Classes="fieldLabel" Text="ALTERNATE COUNT" /><NumericUpDown HorizontalAlignment="Stretch" Minimum="0" FormatString="0" Value="{Binding AlternateCountText, Converter={x:Static v:NullableDecimalStringConverter.Instance}}" /></StackPanel>
  ```
**Depends on:** Step 1
**Verify:** `IssuePropertiesScreenViewModelTests` (Load/Save round-trip + clipboard copy/paste, new
case mirroring the existing `Count`/`CountText` coverage); app build (XAML weave — this edits an
*existing* compiled view, not a new one, so the AVLN2000 new-view gotcha doesn't apply; a plain
`dotnet build` suffices, no `obj`/`dll` deletion needed).

## Step 8: Bulk Issue Editing
**Files:** `src/Paperbunkr.App/Models/BulkFieldDescriptor.cs` (edit)
**What:** Add `Numeric("Alternate Count", Main, i => i.AlternateCount, (i, v) => i.AlternateCount = v, min: 0),` after the `Count` entry (line 101) — or immediately after "Alternate Number" (line 108) for
field-grouping proximity; update the class-doc comment (lines 48–56) to remove `AlternateCount` from
the deliberately-absent list and note it shipped here (2026-09-12).
**Depends on:** Step 1
**Verify:** bulk-editor VM tests (mixed-value/diff-merge case for the new field).

## Step 9: Smart Lists
**Files:** `src/Paperbunkr.Data/Entities/SmartListField.cs` (edit),
`src/Paperbunkr.Data/SmartLists/SmartListCatalog.cs` (edit)
**What:**
- Enum: add `AlternateCount` after `AlternateNumber` (line 42).
- `Definitions`: add `new(SmartListField.AlternateCount, "Alternate Count", SmartListDataType.Number),` after the `AlternateNumber` entry (line 44).
- `NumberSelectors`: add `[SmartListField.AlternateCount] = i => i.AlternateCount ?? -1,` after the `Count` entry (line 206).
**Depends on:** Step 1
**Verify:** Smart List catalog/VM tests (new `Number`-type case).

## Step 10: Tests
**Files:**
- `src/Paperbunkr.Data.Tests/IssueToComicInfoMapperTests.cs` — add `AlternateCount = 6,` to the
  existing `Apply_WritesEveryModeledField` issue init and `Assert.Equal(6, info.AlternateCount);`.
- `src/Paperbunkr.Data.Tests/CeLibraryMigratorTests.cs` — one new focused test,
  `MapStoryFields_ReadsAlternateCountFromComicInfo`, mirroring the existing narrow-field
  `MapStoryFields_*` tests (not the comprehensive mapper test's shape).
- `src/Paperbunkr.Data.Tests/` — migration `Up`/`Down` test alongside the other single-column
  migration tests (add column / drop column, matching e.g. `AddCoverAspectRatio`'s test shape).
- `src/Paperbunkr.App.Tests/IssueListFieldCatalogTests.cs` — sort entry existence/behavior; group
  bucket boundary cases (0/20/21/100/1000/1001) reusing the existing `[Theory]` shape at line 339-350;
  a `null → "Unspecified"` case; an order case confirming `"Unspecified"` sorts before `"0-20"`; a
  regression re-run confirming `OpenCount`'s own existing bucket tests (lines 339-359) are unaffected
  by the shared-helper refactor.
- `src/Paperbunkr.App.Tests/` bulk-editor VM tests — new `AlternateCount` case.
- `src/Paperbunkr.App.Tests/` or `Paperbunkr.Data.Tests/` Smart List catalog tests — new
  `AlternateCount`/`Number` case.
- `src/Paperbunkr.App.Tests/IssuePropertiesScreenViewModelTests.cs` — Load/Save round-trip +
  clipboard copy/paste case for `AlternateCountText`.
**Depends on:** Steps 1–9
**Verify:** targeted `dotnet test` filters for each touched test class, then a full
`Paperbunkr.Data.Tests` + relevant `Paperbunkr.App.Tests` subset run.

## Step 11: Build + verify
**What:** Full solution build; run the targeted test filters from Step 10; confirm no regressions in
the pre-existing `OpenCount` bucket tests. On-screen verification of the new Issue Properties field
and Library sort/group toolbar entries is out of scope this session (no computer-use available) —
flag as outstanding per the design doc §5.
