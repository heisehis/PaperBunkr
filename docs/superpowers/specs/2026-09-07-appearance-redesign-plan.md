# Appearance Redesign — Implementation Plan
*Implements: [docs/superpowers/specs/2026-09-07-appearance-redesign-design.md](2026-09-07-appearance-redesign-design.md)*

Real current shapes confirmed by direct read before planning: `SkinSummary.cs` (4 lines, `Key`/
`Name`/`IsActive` only), `SkinService.cs` (`GetAvailableSkins()` lines 62-99, builds a `SkinTheme`
via `TryLoadSkin` in two loops — built-in keys and installed-skin directories — discarding
everything but `.Name`; `SetColorAndBrush`/`Color.Parse` pattern at lines 216-221 is what this plan
mirrors), `SkinTheme.cs`/`SkinColors` (field names: `Bg`, `Chrome`, `Border`, `Text`, `TextMuted`,
`TextFaint`, `Accent`, `Surface3`, etc.), `AppearanceSection.axaml` (108 lines, full current
structure), `PreferenceIndex.cs` (5 real Appearance entries, lines 41-55), `PreferenceIndexTests.cs`
(confirmed `AnchorKeysAreUnique` — the reason Motion/Developer merge into one entry, not two
pointing at the same tag, per the design doc's plan-time correction), and `SkinServiceTests.cs`
(`GetAvailableSkins_ListsAllBuiltIns_WhenNothingInstalled` at line 86 is the closest existing test to
extend). No other test file (App.Tests or App.UiTests) references any of the strings/tags this plan
changes.

## Step 1: `SkinSummary` — add preview brush properties

**Files:** `src/Paperbunkr.App/Models/SkinSummary.cs` (edit)

**What:** Add `using Avalonia.Media;` and 5 new `required init` properties:
`BackgroundBrush`, `ChromeBrush`, `AccentBrush`, `SurfaceBrush` (from `Surface3`), `TextMutedBrush`
— all typed `IBrush`. Update the class doc comment to mention these drive the mini-mockup preview
card (docs/superpowers/specs/2026-09-07-appearance-redesign-design.md). `Key`/`Name`/`IsActive`
unchanged.

**Depends on:** none
**Verify:** compiles (nothing constructs `SkinSummary` yet with the new required properties until
Step 2 — this step alone will not compile standalone, land it together with Step 2).

## Step 2: `SkinService.GetAvailableSkins()` — populate the new brushes

**Files:** `src/Paperbunkr.App/Services/SkinService.cs` (edit, both `SkinSummary` construction call
sites at lines ~74 and ~93)

**What:** Add a small private static helper mirroring `SetColorAndBrush`'s exact pattern:
```csharp
private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
```
Update both `new SkinSummary { Key = key, Name = theme.Name, IsActive = activeKey == key }` call
sites to also set:
```csharp
BackgroundBrush = Brush(theme.Colors.Bg),
ChromeBrush = Brush(theme.Colors.Chrome),
AccentBrush = Brush(theme.Colors.Accent),
SurfaceBrush = Brush(theme.Colors.Surface3),
TextMutedBrush = Brush(theme.Colors.TextMuted),
```
No behavior change to which skins are listed or how active-state is determined — purely additive
fields on an already-constructed object.

**Depends on:** Step 1
**Verify:** `dotnet build` succeeds; Step 6's new `SkinServiceTests` cases.

## Step 3: `PreferenceIndex.cs` — consolidate the search entries

**Files:** `src/Paperbunkr.App/Models/PreferenceIndex.cs` (edit, lines 41-55)

**What:** Replace the 5 current Appearance entries with 4:
```csharp
new(PreferencesSection.Appearance, "Skins", "Skin",
    new[] { "skin", "theme", "colors", "palette", "windows 11", "evolved amber" },
    "appearance.skin"),
new(PreferencesSection.Appearance, "Install Skin", "Install Skin",
    new[] { "install skin", "crpck", "browse skin", "skins folder" },
    "appearance.skin"),
new(PreferencesSection.Appearance, "Font", "Font",
    new[] { "font", "typeface", "font family" },
    "appearance.font"),
new(PreferencesSection.Appearance, "Interface", "Interface",
    new[] { "motion", "reduce motion", "animation", "transitions", "nav rail", "hover", "expand",
             "developer", "design showcase", "debug" },
    "appearance.interface"),
```
(Skin's own entry `Tag` is unchanged; only Install Skin's `Tag` moves. Motion/Developer's 2 old
entries are deleted, replaced by the one Interface entry — not 3 entries collapsed into 2, since
`AnchorKeysAreUnique` forbids two entries sharing `"appearance.interface"`, per the design doc's
plan-time correction.)

**Depends on:** none (independent of Steps 1-2; both feed into Step 4's XAML which needs the new
`Tag` values to exist for `PreferenceIndexTests` to pass, but the C# and XAML edits can happen in
either order)
**Verify:** Step 6's `PreferenceIndexTests` run.

## Step 4: `AppearanceSection.axaml` — full restructure

**Files:** `src/Paperbunkr.App/Views/Preferences/AppearanceSection.axaml` (edit, full rewrite of the
Skins/Install Skin/Motion/Navigation/Developer sections; Font/General structure elsewhere unchanged)

**What:**

1. **Skins group header:** the `Border Classes="groupBox" Tag="appearance.skin"` wrapper's
   `Border Classes="groupHeader"` becomes a `Grid ColumnDefinitions="*,Auto,Auto"`: "Skins" caption
   text in column 0, `Button Classes="headerAction ghost" Content="Install…"
   Command="{Binding BrowseForSkinCommand}"` in column 1, an icon-only
   `Button Command="{Binding OpenSkinsFolderCommand}"` (`fi:SymbolIcon Symbol="FolderOpen"`,
   `AutomationProperties.Name="Open Skins Folder"`, `ToolTip.Tip="Open Skins Folder"`) in column 2.
2. **Error banner** (currently inside the now-removed Install Skin `StackPanel`, using hardcoded
   `#D96C6C`) moves to sit directly below this header, above the card grid:
   `StackPanel Orientation="Horizontal" Spacing="5" IsVisible="{Binding HasInstallSkinError}"` with
   `fi:SymbolIcon Symbol="Warning" Foreground="{DynamicResource PbDangerBrush}"` and
   `TextBlock Text="{Binding InstallSkinError}" Foreground="{DynamicResource PbDangerBrush}"`.
3. **Card grid:** replace the `ItemsControl x:Name="SkinsList"` (currently `Button.sideItemButton`
   vertical rows) with the same `ItemsControl` but:
   - `ItemsControl.ItemsPanel` → `ItemsPanelTemplate` wrapping a `WrapPanel Orientation="Horizontal"`.
   - `ItemTemplate`: a `Button` (unchanged `Command="{Binding #SkinsList.((vm:PreferencesScreenViewModel)DataContext).SelectSkinCommand}"`/
     `CommandParameter="{Binding}"`) whose content is a `Border` (fixed size, e.g.
     `Width="140" Height="90"`, `CornerRadius="{StaticResource PbRadiusSm}"`,
     `Background="{Binding BackgroundBrush}"`, `Classes.skinActive="{Binding IsActive}"`) containing:
     - a title-bar `Border` (`Height="18"`, `Background="{Binding ChromeBrush}"`) with a small
       circle (`Width="6" Height="6" CornerRadius="3"`, `Background="{Binding AccentBrush}"`)
     - below it, a `Grid ColumnDefinitions="Auto,*"`: a sidebar `Border`
       (`Width="10"`, `Background="{Binding SurfaceBrush}"`) and a content column with 2-3 thin
       `Border`s (`Height="4"`, varying `Width`, `Background="{Binding TextMutedBrush}"`)
     - the skin `Name` as a small `TextBlock` below the mockup, `Foreground="{DynamicResource PbTextBrush}"`
   - A local `UserControl.Styles` entry:
     ```xml
     <Style Selector="Border.skinActive">
         <Setter Property="BorderBrush" Value="{DynamicResource PbAccentBrush}" />
         <Setter Property="BorderThickness" Value="2" />
     </Style>
     ```
     plus a checkmark badge (`fi:SymbolIcon Symbol="Checkmark"`, `Foreground="{DynamicResource
     PbSuccessBrush}"`, small, corner-positioned via a `Grid`/`Canvas` overlay) `IsVisible="{Binding
     IsActive}"` layered on top of the card (e.g. the card's outer container becomes a `Grid` so the
     badge can sit in a corner cell independent of the mockup content's own layout).
4. **Remove** the entire `StackPanel Tag="appearance.installSkin"` block (folded into the Skins
   group per items 1-2 above).
5. **Merge Motion/Navigation/Developer:** replace the 3 separate `StackPanel Tag="appearance.motion"`
   / `"appearance.navigation"` / `"appearance.developer"` blocks with one:
   ```xml
   <StackPanel Tag="appearance.interface">
       <TextBlock Classes="settingsGroupCaption" Text="INTERFACE" />
       <pref:SettingsRow Icon="Pulse" Title="Reduce motion" Description="Shortens UI transitions to effectively instant.">
           <pref:SettingsRow.SettingsContent>
               <ToggleSwitch IsChecked="{Binding ReducedMotion}" OnContent="{x:Null}" OffContent="{x:Null}" />
           </pref:SettingsRow.SettingsContent>
       </pref:SettingsRow>
       <pref:SettingsRow Icon="Navigation" Title="Expand nav rail on hover" Description="When off, the rail only expands while pinned open.">
           <pref:SettingsRow.SettingsContent>
               <ToggleSwitch IsChecked="{Binding NavRailHoverExpandEnabled}" OnContent="{x:Null}" OffContent="{x:Null}" />
           </pref:SettingsRow.SettingsContent>
       </pref:SettingsRow>
       <pref:SettingsRow Icon="Code" Title="Open the internal component/style showcase" IsVisible="{Binding IsDebugBuild}">
           <pref:SettingsRow.SettingsContent>
               <Button Content="Open Design Showcase" Command="{Binding OpenDesignShowcaseCommand}" />
           </pref:SettingsRow.SettingsContent>
       </pref:SettingsRow>
   </StackPanel>
   ```
   (`IsVisible` moves from the old Developer `StackPanel` onto its `SettingsRow` directly — check
   `pref:SettingsRow` actually supports `IsVisible` as a passthrough `UserControl`/`TemplatedControl`
   property before assuming; if it doesn't expose one, wrap just that row in a 1-child
   `StackPanel IsVisible="{Binding IsDebugBuild}"` instead, same net effect.)
6. **Font preview:** inside the existing `StackPanel Tag="appearance.font"`, after the Font family
   `SettingsRow`, add:
   ```xml
   <TextBlock Text="The quick brown fox jumps over the lazy dog" FontFamily="{Binding SelectedFontFamily}"
              FontSize="14" Foreground="{DynamicResource PbTextMutedBrush}" Margin="0,8,0,0" />
   ```

**Depends on:** Steps 1-3 (needs the new `SkinSummary` brush properties and `PreferenceIndex`'s new
`Tag` values to exist for consistency, though XAML itself would still compile without them — do
Steps 1-3 first to keep the build meaningful at each step)
**Verify:** `dotnet build` — this is not a new `x:Class`, so the AVLN2000 fresh-view gotcha doesn't
apply, but per `CLAUDE.md`'s standing gotcha, if any interim build during this step fails on XAML
compilation, force `CoreCompile` stale before trusting a later "0 Errors" (delete
`src/Paperbunkr.App/obj/Debug/net10.0/Paperbunkr.App.dll`/`.pdb`, rebuild) rather than just retrying.

## Step 5 (confirmed, no work needed): `pref:SettingsRow` already supports `IsVisible` directly

`SettingsRow.axaml.cs` is a plain `UserControl` (confirmed by direct read) with no override of the
inherited `Control.IsVisible` — `IsVisible="{Binding IsDebugBuild}"` on the `pref:SettingsRow`
element in Step 4 works with zero code change. Kept as a numbered note rather than renumbering the
remaining steps.

## Step 6: Tests

**Files:** `src/Paperbunkr.App.Tests/SkinServiceTests.cs` (edit)

**What:** Extend `GetAvailableSkins_ListsAllBuiltIns_WhenNothingInstalled` (or add a new adjacent
test) with brush assertions for at least 2 skins, e.g.:
```csharp
var defaultSkin = skins.Single(s => s.Key == SkinService.DefaultSkinKey);
Assert.Equal(Avalonia.Media.Color.Parse("#131519"), ((SolidColorBrush)defaultSkin.ChromeBrush).Color);
Assert.Equal(Avalonia.Media.Color.Parse("#C9803F"), ((SolidColorBrush)defaultSkin.AccentBrush).Color);
```
`PreferenceIndexTests` needs no new test cases — its existing 4 generic tests
(`EverySectionHasAResourceMapping`, `EverySectionHasAtLeastOneIndexEntry`, `AnchorKeysAreUnique`,
`EveryEntryAnchorResolvesToATagInItsSection`) automatically re-validate the new 4-entry shape once
Steps 3-4 land; just confirm they pass.

**Depends on:** Steps 1-4
**Verify:** `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter
"FullyQualifiedName~SkinServiceTests|FullyQualifiedName~PreferenceIndexTests"` — all pass.

## Step 7: Build, review-checklist pass, manual verification

**Files:** none (verification only)

**What:**
1. `dotnet build` — 0 errors (with the force-stale-`CoreCompile` caveat from Step 4 if needed).
2. Review-checklist subset: no hardcoded hex left (`#D96C6C` fully gone from this file); the mini-
   mockup cards use only `DynamicResource`/bound brushes, no literal colors in the card template
   itself (the brushes themselves carry the color, which is correct — this is data-driven, not
   hardcoded); Open Skins Folder's icon-only button has `AutomationProperties.Name`; the active-card
   indicator pairs a border ring with the checkmark badge, not color alone.
3. Manual on-screen pass (standing no-unattended-GUI caveat — hand off to the user): all 5 skins
   render distinguishable mini-mockups in a wrapping grid; clicking a card applies that skin and the
   accent-ring+checkmark move to it; Install… and Open Skins Folder both work from the header;
   triggering an install error shows it in `PbDangerBrush` red, not the old hardcoded color; the
   INTERFACE group shows 3 rows in a Debug build (2 in Release); the Font preview line updates live
   as a different font family is picked; Preferences search still finds Appearance's skin/install/
   font/interface groups by their keywords (e.g. searching "debug" or "nav rail" jumps to the
   Interface group).

**Depends on:** Steps 1-6
**Verify:** build + targeted test filter green; manual pass confirms all 7 design goals.
