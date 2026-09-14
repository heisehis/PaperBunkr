# Metadata editors redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-14-metadata-editors-redesign-design.md*

## Step 1: Card style + segmented-weight ViewModel plumbing
**Files:** `src/Paperbunkr.App/ViewModels/TagEditRowViewModel.cs` (edit)
**What:** Add one parameterized command for the segmented Weight picker:
```csharp
[RelayCommand]
private void SetWeight(IssueTagWeight weight) => Weight = weight;
```
(needs `using CommunityToolkit.Mvvm.Input;`, already needed elsewhere in this codebase's `[RelayCommand]` pattern). No other ViewModel change - `Weight`/`WeightText`/`WeightNames` are untouched, the segmented control's 5 buttons each bind `Command="{Binding SetWeightCommand}"` with a distinct `CommandParameter`.
**Depends on:** none
**Verify:** `dotnet build`.

## Step 2: Issue Properties — Border.card + icon-per-field + Core Details/Credits split
**Files:** `src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml` (edit)
**What:**
1. Add `xmlns:entities="using:Paperbunkr.Data.Entities"` (needed for the Weight segmented control's `{x:Static entities:IssueTagWeight.X}` parameters).
2. Delete the local `Style Selector="Border.groupBox"` / `Border.groupHeader"` blocks ([:36-45](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml#L36-L45)) - `Border.card` is already globally available via `Styles/DetailChrome.axaml`.
3. Restyle `Button.toolbarPill` ([:75-84](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml#L75-L84)): accent-tinted background/border/foreground per the design doc's §5 (soft `PbAccentBrush`-tinted background and border, `PbAccentBrush` foreground, solid accent fill + dark text on `:pointerover`).
4. Replace every `Border Classes="groupBox"` + `Border Classes="groupHeader"` pair with a single `Border Classes="card"` wrapping a plain caption `TextBlock` (same style as the Detail screen's own section captions: `FontSize="10.5"`, `Foreground="{DynamicResource PbTextFaintBrush}"`, letter-spaced) - applies to Overview, Ratings (Summary tab), Plot, Notes (Plot & Notes tab), and both Genre/Tags Details cards.
5. Split the single "Details" card ([:251-414](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml#L251-L414)) into two `Border Classes="card"` blocks - **Core Details** (Number → Final Issue, everything except the 8 credit-role fields) and **Credits** (Writer, Penciller, Inker, Colorist, Letterer, Cover Artist, Editor, Translator) - same `WrapPanel` of `StackPanel Classes="field"` per card, fields unchanged internally.
6. Add a leading `fi:SymbolIcon` (or keep the existing `pbc:BrandMark` for Format/Age Rating/Language) to every `TextBlock Classes="fieldLabel"` - wrap each into a `StackPanel Orientation="Horizontal" Spacing="5"` with the icon first, same shape as `DetailTabs.axaml`'s own field-label rows (e.g. [:513-514](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L513-L514)). Suggested glyphs (all confirmed to exist in `FluentIcons.Common.Symbol`): Number→`NumberSymbol`, Volume→`BookNumber`, Count→`NumberSymbol`, Title→`TextNumberList`... *(exact glyph per field is implementation-time judgment per the design doc; pick single-purpose, non-repeating icons from the confirmed-available set - `Building` (Publisher, matches the Detail tab), `Globe` (Imprint or Language-adjacent), `Calendar` (Year/Month/Day), `Tag`/`TagMultiple` (Genre/Tags), `PenSparkle`/`CalligraphyPen` (Writer/Letterer), `PaintBrush`/`Eyedropper` (Colorist/Inker/Cover Artist), `PersonEdit`/`PersonBoard` (Penciller/Editor), `Translate` (Translator))*.
7. Genre/Tags Details rows ([:421-457](../../../src/Paperbunkr.App/Views/IssuePropertiesScreen.axaml#L421-L457)): change the `Grid ColumnDefinitions="140,*,140"` row template - Column 0's plain bold `TextBlock` becomes a small chip (`Border` with `PbAccentBrush`-tinted background/foreground, `CornerRadius="999"`, padding, containing the `TextBlock Text="{Binding Value}"`); Column 2's `pbc:SuggestBox` (Weight) becomes a 5-segment control mirroring `DetailTabs.axaml`'s `segItem` view-mode toggle ([DetailTabs.axaml:249-277](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L249-L277) for the shape, [:74-87](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L74-L87) for the `Button.segItem`/`.active` style - mirror both style blocks into this file's own `<UserControl.Styles>`, same "styles don't share across files" convention already documented here): one `Button Classes="segItem" Classes.active="{Binding Weight, Converter=..., ConverterParameter=...}"` per `IssueTagWeight` value (`Unset`, `Incidental`, `Recurrent`, `Defining`, `Core`), each `Command="{Binding SetWeightCommand}" CommandParameter="{x:Static entities:IssueTagWeight.X}"`. Active-state check: `Classes.active="{Binding Weight, Converter={x:Static conv:ObjectConverters.Equal}, ConverterParameter={x:Static entities:IssueTagWeight.Core}}"` (`ObjectConverters.Equal` pattern already used elsewhere in this codebase, e.g. `DetailTabs.axaml`'s own `TierClass` checks).
**Depends on:** Step 1
**Verify:** `dotnet build` - watch for the AVLN2000/stale-assembly gotcha this repo's CLAUDE.md documents if this were a brand-new view (it isn't, so a plain build is sufficient, but force a clean rebuild if anything looks off per that same gotcha's caution).

## Step 3: Bulk Issue Properties — Border.card + icon-per-field
**Files:** `src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml` (edit)
**What:**
1. Delete the local `Border.groupBox`/`Border.groupHeader` styles ([:19-28](../../../src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml#L19-L28)).
2. Restyle `Button.toolbarPill` ([:48-57](../../../src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml#L48-L57)) identically to Step 2.3.
3. Replace the three `groupBox`+`groupHeader` sections (Main/Artists/Plot & Notes, [:211-243](../../../src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml#L211-L243)) with `Border Classes="card"` + caption `TextBlock`, same pattern as Step 2.4. **No regrouping** - same three `MainFields`/`ArtistFields`/`PlotNotesFields` bindings, same `FieldRowTemplate` resource, unchanged.
4. `FieldRowTemplate` ([:71-149](../../../src/Paperbunkr.App/Views/BulkIssuePropertiesScreen.axaml#L71-L149)): add the same leading-icon treatment to its `TextBlock Grid.Column="1"` label (Column 1 is the field label in this row template, distinct from Issue Properties' `fieldLabel` class) - wrap in a horizontal `StackPanel` with an `fi:SymbolIcon` first, same glyph-per-field-name mapping as Step 2.6 (`BulkFieldDescriptor.Label` strings match the same field names, e.g. "Writer"/"Publisher"/"Genre").
**Depends on:** none (independent of Step 2 - separate file, no shared bindings)
**Verify:** `dotnet build`.

## Step 4: Number-spinner restyle
**Files:** `src/Paperbunkr.App/Styles/FormControls.axaml` (edit, `RepeatButton.textSpinner` block at [:157-173](../../../src/Paperbunkr.App/Styles/FormControls.axaml#L157-L173))
**What:** Restyle to the approved "bordered pill stepper" (option B): wrap the two `RepeatButton`s in a bordered, rounded container with a divider between them (`CornerRadius` on the container, 1px `PbBorderBrush` border, a thin divider line between the up/down halves), `:pointerover` fills solid `PbAccentBrush` background with dark (`PbBadgeTextBrush`-equivalent) foreground instead of just changing the icon color. This requires restructuring `TextSpinner.BuildSpinner` in `src/Paperbunkr.App/Behaviors/TextSpinner.cs` slightly - currently returns a bare `StackPanel` with two `RepeatButton`s ([TextSpinner.cs:78-84](../../../src/Paperbunkr.App/Behaviors/TextSpinner.cs#L78-L84)); wrap that `StackPanel` in a `Border` (or change the container itself to a bordered `Border`+`StackPanel`) so the new container-level border/corner-radius/divider styling has an element to attach to. The nudge/step logic (`Nudge`/`Step`/`Clamp`) is untouched.
**Depends on:** none
**Verify:** `dotnet build`; on-screen check a Number/Volume field's spinner renders and still nudges correctly (click up/down, press-and-hold repeat).

## Step 5: Tests
**Files:** `src/Paperbunkr.App.Tests/IssuePropertiesScreenViewModelTests.cs` (edit, if `TagEditRowViewModel` is exercised there) or a focused addition wherever `TagEditRowViewModel` already has coverage - check first.
**What:** One new test: constructing a `TagEditRowViewModel` and calling `SetWeightCommand.Execute(IssueTagWeight.Defining)` sets `Weight` to `Defining` and `WeightText` to `"Defining"`.
**Depends on:** Step 1
**Verify:** `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter "FullyQualifiedName~TagEditRow"` (or wherever the new test lands).

## Step 6: On-screen verification
**What:** Same caveat as this session's prior work - check the shared working tree's concurrent-activity state before launching the app. If clear: open the Issue Properties editor (all 3 tabs - confirm Core Details/Credits split, icon-per-field, restyled spinner on Number/Volume, restyled token button on Title, chip+segmented-weight on a series with existing Genre/Tags), and the Bulk Issue Properties editor (confirm Border.card + icon-per-field on all 3 groups, checkbox-staging still works).
**Depends on:** Steps 1-4
**Verify:** manual, no automated substitute.
