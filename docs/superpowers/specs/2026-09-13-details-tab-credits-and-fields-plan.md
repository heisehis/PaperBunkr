# Details tab: credits + fields — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-13-details-tab-credits-and-fields-design.md*

## Step 1: New chip/row models
**Files:** `src/Paperbunkr.App/Models/CreditRoleGroup.cs` (new), `src/Paperbunkr.App/Models/DetailFieldRow.cs` (new)
**What:**
- `CreditRoleGroup`: `{ string Label; ObservableCollection<TagPillViewModel> Chips; }`, plain class/record, constructed with a label + chip list.
- `DetailFieldRow`: `{ string Label; string Value; bool IsLink; }` plus, for link rows, an `[RelayCommand] OpenLink()` that calls an injected `Action<string>? _openLink` with `Value` (mirrors `TagPillViewModel`'s constructor-injected-callback shape, [TagPillViewModel.cs:22](../../../src/Paperbunkr.App/ViewModels/TagPillViewModel.cs#L22)). Needs `ObservableObject`/`CommunityToolkit.Mvvm.Input` since it has a command — make it a `partial class : ObservableObject`, not a record, for consistency with every other command-bearing row-model in this codebase.
**Depends on:** none
**Verify:** compiles; no behavior yet.

## Step 2: `DetailTabsViewModel` — Publisher fix + new collections + aggregation
**Files:** `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (edit)
**What:**
1. Add `Action<string>? goLibraryWithSearch = null` as a new trailing parameter on **both** the public constructor ([DetailTabsViewModel.cs:55](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L55)) and the internal test-seam constructor ([DetailTabsViewModel.cs:61](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L61)), threaded through the `: this(...)` delegation. Store as `private readonly Action<string> _goLibraryWithSearch = goLibraryWithSearch ?? (_ => { });` (null-object pattern, matches `_navigateToSeries`/`_openInReader` on the same class).
2. Add 8 `private static readonly BulkFieldDescriptor` fields for the Artists-group roles (Writer, Penciller, Inker, Colorist, Letterer, Cover Artist, Editor, Translator) via `BulkFieldRegistry.Find(...)`, plus 6 more for the Additional Details fields (Imprint, Web, Notes, Scan Information, Alternate Series, Series Group) — same pattern as `DetailBandViewModel.cs:39-42`.
3. Add `public ObservableCollection<CreditRoleGroup> CreditRoles { get; } = new();`, `public ObservableCollection<DetailFieldRow> AdditionalDetails { get; } = new();`, `public bool HasCreditRoles => CreditRoles.Count > 0;`, `public bool HasAdditionalDetails => AdditionalDetails.Count > 0;`.
4. In `LoadSeries` ([DetailTabsViewModel.cs:204](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L204)):
   - Replace the Publisher line ([:239](../../../src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs#L239)) with `Publisher = SeriesMetaFields.FromSeries(series).Publisher ?? "Unknown";` — add `using Paperbunkr.App.Models;` if not already present (it is — `Models` namespace already used throughout this file).
   - Clear + repopulate `CreditRoles` via a private `AddCreditRole(string label, BulkFieldDescriptor field, IReadOnlyList<Issue> issues)` helper: `CsvFieldAggregator.Distinct(issues.Select(field.Get))`, skip adding when the resulting list is empty, else wrap each value in `new TagPillViewModel(value, category: null, IssueTagWeight.Unset, _goLibraryWithSearch, reweight: null)`.
   - Clear + repopulate `AdditionalDetails` via a private `AddDetailField(string label, Func<Issue,string?> get, IReadOnlyList<Issue> issues, bool isLinkCandidate = false)` helper: aggregate distinct values, skip when empty, join with `", "` when >1, set `IsLink = isLinkCandidate && distinctValues.Count == 1`. Story Arc Number uses `i => i.StoryArcNumber` directly (not a `BulkFieldDescriptor.Get`, per the design doc's registry-gap note).
   - Raise `OnPropertyChanged(nameof(HasCreditRoles))` / `OnPropertyChanged(nameof(HasAdditionalDetails))` after both.
5. `DetailFieldRow` for the Web row gets `_openLink = url => { try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { } }` passed in from `DetailTabsViewModel` (needs `using System.Diagnostics;`) — keep the try/catch inside `DetailTabsViewModel`'s lambda (or inside `DetailFieldRow.OpenLink` itself, whichever keeps `DetailFieldRow` framework-agnostic; simplest is putting the try/catch directly in `DetailFieldRow.OpenLink` and not injecting a callback at all — reduces Step 1's surface. Revise Step 1 accordingly if so.).
**Depends on:** Step 1
**Verify:** `dotnet build` (watch for the AVLN2000 gotcha this repo's CLAUDE.md documents — not applicable here since no new `.axaml`/`x:Class` is added in this step).

## Step 3: Forward `goLibraryWithSearch` from both host screens
**Files:** `src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs` (edit, line 42), `src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs` (edit, line 50)
**What:** Both already receive `goLibraryWithSearch` as their own constructor parameter, currently only forwarded to `Band`. Add it as the trailing argument to their existing `Tabs = new DetailTabsViewModel(...)` calls.
```csharp
// DetailScreenViewModel.cs:42
Tabs = new DetailTabsViewModel(goToProperties, goToBulkProperties, RefreshForSelection, onQuickRate, _goDetailForSeries, goToReader, goLibraryWithCollection, goLibraryWithSearch);

// MangaDetailScreenViewModel.cs:50
Tabs = new DetailTabsViewModel(goToProperties, goToBulkProperties, navigateToSeries: _goDetailForSeries, openInReader: goToReader, navigateToCollection: goLibraryWithCollection, goLibraryWithSearch: goLibraryWithSearch) { ShowIssuesTab = false, ShowTabStrip = false };
```
**Depends on:** Step 2 (new parameter must exist first)
**Verify:** `dotnet build` — both call sites must compile against the new signature.

## Step 4: XAML — Credits + Additional Details sections
**Files:** `src/Paperbunkr.App/Views/DetailTabs.axaml` (edit, insert between line 533 and line 535)
**What:** Two new blocks in the Details-tab `Grid`'s containing `StackPanel`, following the existing icon+caption section header pattern ([DetailTabs.axaml:511-532](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L511-L532) for field rows, [:542-553](../../../src/Paperbunkr.App/Views/DetailTabs.axaml#L542-L553) for named-section headers):

```xml
<StackPanel IsVisible="{Binding HasCreditRoles}" Margin="0,24,0,0" Spacing="12">
    <StackPanel Orientation="Horizontal" Spacing="10">
        <fi:SymbolIcon Symbol="People" FontSize="{StaticResource PbIconSizeSm}" Foreground="{DynamicResource PbTextFaintBrush}" VerticalAlignment="Center" />
        <TextBlock Text="Credits" FontSize="12.5" FontWeight="SemiBold" Foreground="{DynamicResource PbTextMutedBrush}" VerticalAlignment="Center" />
    </StackPanel>
    <ItemsControl ItemsSource="{Binding CreditRoles}">
        <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="models:CreditRoleGroup">
                <StackPanel Spacing="4" Margin="0,0,0,8">
                    <TextBlock Text="{Binding Label}" FontSize="10.5" Foreground="{DynamicResource PbTextFaintBrush}" />
                    <ItemsControl ItemsSource="{Binding Chips}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate><WrapPanel Orientation="Horizontal" /></ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:TagPillViewModel">
                                <!-- reuse whatever Border/Button chip template DetailBand.axaml already
                                     uses for its own writer/artist pills (same DataType) -->
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>

<StackPanel IsVisible="{Binding HasAdditionalDetails}" Margin="0,24,0,0" Spacing="8">
    <StackPanel Orientation="Horizontal" Spacing="10">
        <fi:SymbolIcon Symbol="TextAlignLeft" FontSize="{StaticResource PbIconSizeSm}" Foreground="{DynamicResource PbTextFaintBrush}" VerticalAlignment="Center" />
        <TextBlock Text="Additional Details" FontSize="12.5" FontWeight="SemiBold" Foreground="{DynamicResource PbTextMutedBrush}" VerticalAlignment="Center" />
    </StackPanel>
    <ItemsControl ItemsSource="{Binding AdditionalDetails}">
        <ItemsControl.ItemTemplate>
            <DataTemplate x:DataType="models:DetailFieldRow">
                <StackPanel Margin="0,0,0,6" Spacing="2">
                    <TextBlock Text="{Binding Label}" FontSize="10.5" Foreground="{DynamicResource PbTextFaintBrush}" />
                    <Button Classes="creditName" Content="{Binding Value}" Command="{Binding OpenLinkCommand}"
                            IsVisible="{Binding IsLink}" HorizontalContentAlignment="Left" Padding="0" />
                    <TextBlock Text="{Binding Value}" FontSize="13" Foreground="{DynamicResource PbTextBrush}"
                               TextWrapping="Wrap" IsVisible="{Binding !IsLink}" />
                </StackPanel>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</StackPanel>
```
The exact chip template for `TagPillViewModel` inside the Credits `ItemsControl` must be copied from whatever `DetailBand.axaml` already renders for its own Writer/Artist pills (find via `Grep "TagPillViewModel" src/Paperbunkr.App/Views/DetailBand.axaml`, since that file wasn't fully read in this planning pass) — reuse verbatim, don't invent a new chip look.
**Depends on:** Step 2 (bindings must exist on the ViewModel)
**Verify:** `dotnet build` per this repo's CLAUDE.md gotcha — this edits an *existing* `.axaml` with an existing compiled `x:Class`, so the AVLN2000/stale-assembly trap doesn't apply (that's only for brand-new views); a plain `dotnet build` is sufficient. Then on-screen check: open a series with multi-role credits and one with none, confirm the sections show/hide correctly.

## Step 5: Tests
**Files:** `src/Paperbunkr.App.Tests/DetailTabsViewModelTests.cs` (edit)
**What:** Add to `CreateViewModel`'s parameter list: `Action<string>? goLibraryWithSearch = null`, forwarded to the internal ctor. New test cases (using this file's existing `LoadSeriesEntity()`/context-seeding conventions):
- Publisher fallback: series with blank `Publisher`, one issue with `Publisher = "DC Comics"` → `vm.Publisher == "DC Comics"`.
- Credits aggregation: two issues with different `Writer` values → `CreditRoles` has a "Writer" entry with both, deduped case-insensitively.
- Credits omission: no issue has any `Inker` value → no "Inker" entry in `CreditRoles`; `HasCreditRoles` false when every role is empty.
- Additional Details aggregation + omission: a field blank everywhere → no row; a single-issue value → shown as-is.
- Web link `IsLink`: single distinct `Web` value → `IsLink == true`; two issues with different `Web` values → `IsLink == false`, value is the joined string.
- Chip click: construct with a captured `goLibraryWithSearch` action, invoke `CreditRoles[...].Chips[...].SearchCommand.Execute(null)`, assert the captured action received the chip's `Value`.
**Depends on:** Steps 1-3
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter DetailTabsViewModelTests` (or this project's existing per-fixture test-run convention if narrower filtering is customary here — check a recent test-running command in `git log`/CI config if unsure, otherwise this filter is the direct equivalent of every other `*ViewModelTests` class in this suite).

## Step 6: On-screen verification
**What:** Launch the app (per this repo's `run` skill / existing dev workflow), open a comic series with rich ComicInfo.xml data (multiple credit roles, a Web URL, Notes) and confirm: Credits section shows all populated roles with clickable chips that filter the Library; Additional Details shows the 7 fields, Web is a clickable link that opens the browser; Publisher on the Details tab now matches the hero badge for a series whose `Series.Publisher` is blank but issues carry one. Also check a manga series (same `DetailTabsViewModel`) shows the same sections.
**Depends on:** Steps 1-4
**Verify:** manual, no automated substitute — this is real rendering/click behavior in Avalonia, per this repo's own "test UI in a browser/on-screen before calling it done" rule.
