# Preferences Rework — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-28-preferences-rework-design.md*

Deviation from the design, decided during planning: the section switch is an **instant hard
swap** (`IsVisible` toggle), not an animated cross-fade. The user explicitly asked for a hard
switch; an instant swap is simpler, lower-risk, and makes the reduce-motion branch moot for
navigation. The search-result highlight pulse still animates and still respects `ReducedMotion`.
`PreferencesScreen` keeps its class name (still mounted by MainWindow's existing DataTemplate) —
it becomes the shell internally rather than being renamed.

## Step 1: `PreferencesSection` enum + section metadata
**Files:** `src/Paperbunkr.App/Models/PreferencesSection.cs` (new)
**What:** `enum PreferencesSection { General, Appearance, Library, Reader, KeyboardShortcuts, Connections, Plugins, Advanced }`.
Same file: `static class PreferencesSectionMeta` with `IReadOnlyList<PreferencesSection> Order` (= `Enum.GetValues`)
and `string Label(PreferencesSection)` (only `KeyboardShortcuts` → "Keyboard Shortcuts"; rest `ToString()`).
**Depends on:** none
**Verify:** compiles; used by later steps.

## Step 2: Preference search index
**Files:** `src/Paperbunkr.App/Models/PreferenceIndex.cs` (new)
**What:** `record PreferenceIndexEntry(PreferencesSection Section, string GroupTitle, string Title, IReadOnlyList<string> Keywords, string AnchorKey)`
and `static class PreferenceIndex { IReadOnlyList<PreferenceIndexEntry> Entries }` — one entry per
group card (~20): general.reading, general.window, appearance.skin, appearance.installSkin,
appearance.font, appearance.motion, appearance.developer, library.comicFolders, library.bookFolders,
library.migration, library.virtualTags, reader.rtl, reader.display, reader.zoomNav,
reader.imageAdjust, reader.background, shortcuts.io, shortcuts.navigation, shortcuts.zoomFit,
shortcuts.display, connections.metadataSources, connections.trackers, advanced.rendering,
advanced.fileAssociation, advanced.backup. Keywords carry the inner control labels
("double page", "spread", "fit mode", "anilist", "backup", …).
**Depends on:** Step 1
**Verify:** `PreferenceIndexTests` (Step 11).

## Step 3: search result view-model
**Files:** `src/Paperbunkr.App/ViewModels/PreferenceSearchResultViewModel.cs` (new)
**What:** wraps a `PreferenceIndexEntry`; exposes `Section`, `AnchorKey`, `Title`, `GroupTitle`,
`SectionLabel` (via `PreferencesSectionMeta.Label`).
**Depends on:** Steps 1–2
**Verify:** compiles.

## Step 4: ViewModel — section enum + search members
**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit)
**What:**
- Replace `[ObservableProperty] string _activeTab = "appearance"` with
  `[ObservableProperty] PreferencesSection _activeSection = PreferencesSection.General`.
- `IsAppearanceTab`…`IsPluginsTab` → `IsGeneralSection`…`IsAdvancedSection` (8 bools).
- `OnActiveTabChanged` → `OnActiveSectionChanged` raising `OnPropertyChanged` for all 8.
- `GoAppearance`…`GoPlugins` relay commands → `GoGeneral`/`GoAppearance`/`GoLibrary`/`GoReader`/
  `GoKeyboardShortcuts`/`GoConnections`/`GoPlugins`/`GoAdvanced`, each `ActiveSection = …`.
- Add: `[ObservableProperty] string _searchQuery = ""`, `ObservableCollection<PreferenceSearchResultViewModel> SearchResults` (ctor-init),
  `bool IsSearching => !string.IsNullOrWhiteSpace(SearchQuery)`,
  `OnSearchQueryChanged` → rebuild `SearchResults` (case-insensitive substring over section label,
  GroupTitle, Title, Keywords) + `OnPropertyChanged(nameof(IsSearching))`,
  `event Action<string>? ScrollToAnchorRequested`,
  `[RelayCommand] OpenSearchResult(PreferenceSearchResultViewModel r)` → set `ActiveSection`, clear
  `SearchQuery`, invoke `ScrollToAnchorRequested`,
  `[RelayCommand] ClearSearch()` → `SearchQuery = ""`.
- Keep everything else (all `Persist*`, all `On<Setting>Changed`, `ReaderDisplaySettingsChanged`,
  `EnsureLoaded`/`Reload`) untouched.
**Depends on:** Steps 1–3
**Verify:** `PreferencesScreenViewModelTests` (Step 10); full build.

## Step 5: MainViewModel call-site
**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:** line ~402 `Preferences.ActiveTab = "libraries"` → `Preferences.ActiveSection = PreferencesSection.Library`
(add `using Paperbunkr.App.Models;` if absent).
**Depends on:** Step 4
**Verify:** build; existing MainViewModel/navigation tests.

## Step 6: new vector icons
**Files:** `src/Paperbunkr.App/Styles/Icons.axaml` (edit),
`src/Paperbunkr.App/Assets/Icons/icon-mapping.md` (edit)
**What:** add `PbIconFolderAdd`, `PbIconFolderSearch`, `PbIconCloudUpload`, `PbIconArchive`
StreamGeometries (Lucide-style, computed not traced, 24×24). Record them in the mapping doc with
the actions they serve (Add Folder / Scan Now / Migrate / Backup Now).
**Depends on:** none
**Verify:** build; visual check in Step 12.

## Step 7: shell shared styles
**Files:** `src/Paperbunkr.App/Views/PreferencesScreen.axaml` (edit — `<UserControl.Styles>`)
**What:** add `Button.prefNavItem` (+ `.active`), `Border.settingsGroup`, `Border.settingsGroupHeader`
built on `PbSurface2Brush` / `PbBorderBrush` / `PbRadiusMd` / `pbTextCaption`. Delete
`Button.prefTab`, `Button.headerAction*`, `Border.groupBox`, `Border.groupHeader`, `Button.sideItemButton`,
`Border.skinRow` once the sections that used them are migrated (Steps 8–9) — keep during the transition.
**Depends on:** none (additive first)
**Verify:** build.

## Step 8: shell layout
**Files:** `src/Paperbunkr.App/Views/PreferencesScreen.axaml` (rewrite body),
`src/Paperbunkr.App/Views/PreferencesScreen.axaml.cs` (edit)
**What:** `Grid RowDefinitions="Auto,*"`:
- Row 0 header: "PREFERENCES" (`pbTextHeading`) + search `TextBox` bound to `SearchQuery`
  (leading `PbIconSearch`, `Escape` → `ClearSearchCommand`).
- Row 1 `Grid ColumnDefinitions="220,*"`:
  - Col 0: when `!IsSearching` → `StackPanel` of 8 `Button Classes="prefNavItem"` with
    `Classes.active="{Binding IsXSection}"` + `Command="{Binding GoXCommand}"`; when `IsSearching`
    → `ItemsControl ItemsSource="{Binding SearchResults}"`, each item a button →
    `OpenSearchResultCommand` (`CommandParameter="{Binding}"`), showing `Title` + `SectionLabel`.
  - Col 1: `Panel` with the 8 section controls, each `IsVisible="{Binding IsXSection}"`.
- Code-behind: subscribe to `ScrollToAnchorRequested` (on `DataContextChanged`); on fire, post to
  the dispatcher, walk the visual tree for a `Border` with `Tag == anchorKey`, `BringIntoView()`,
  then a one-shot ~1s `PbGlowRing` `BoxShadow` animation unless `SkinService`/VM `ReducedMotion` is set
  (read the VM's `ReducedMotion` property).
**Depends on:** Steps 4, 7, 9
**Verify:** build; Step 12 manual.

## Step 9: eight section UserControls
**Files (all new, `.axaml` + `.axaml.cs` per CLAUDE.md):**
`src/Paperbunkr.App/Views/Preferences/GeneralSection.{axaml,axaml.cs}`,
`AppearanceSection`, `LibrarySection`, `ReaderSection`, `KeyboardShortcutsSection`,
`ConnectionsSection`, `PluginsSection`, `AdvancedSection`.
**What:** each `UserControl` `x:DataType="vm:PreferencesScreenViewModel"`, root
`<ScrollViewer><StackPanel Margin="28,20,28,40" Spacing="20">` with a `pbTextHeading` title then the
relocated group `Border`s (each `Classes="settingsGroup"` + `Tag="<anchorKey>"`). Move the existing
markup verbatim from `PreferencesScreen.axaml`, swapping `groupBox`→`settingsGroup`,
`headerAction primary/ghost`→`primary`/`ghost`, raster `ImageBrush`+`OpacityMask` icons→`Path.pbIcon`.
Bindings/commands/`x:Name`s/`AutomationId`s unchanged. Group relocation per the design's table
(General gets Reading + Minimize-to-tray; Connections gets Reading List Sources + Trackers; etc.).
`PluginsSection` = just `<views:PluginScreen DataContext="{Binding Plugin}" />`. Drop the "Future"
placeholder group.
**Depends on:** Steps 4, 6, 7
**Verify:** build (watch for `AVLN2000` — code-behind ships same commit); Step 12.

## Step 10: update `PreferencesScreenViewModelTests`
**Files:** `src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:** `Is*Tab`→`Is*Section`; `GoLibrariesCommand`→`GoLibraryCommand`; add nav tests for
General / KeyboardShortcuts / Connections; add search tests — query filters `SearchResults`,
empty query ⇒ empty, `OpenSearchResultCommand` sets `ActiveSection` + clears query + fires
`ScrollToAnchorRequested` with the entry's anchor.
**Depends on:** Step 4
**Verify:** `dotnet test --filter PreferencesScreenViewModelTests`.

## Step 11: new `PreferenceIndexTests`
**Files:** `src/Paperbunkr.App.Tests/PreferenceIndexTests.cs` (new)
**What:** for each `PreferenceIndexEntry`, instantiate the section `UserControl` for
`entry.Section` (small `switch`), walk its logical tree, assert some `Border.Tag` equals
`entry.AnchorKey`. Assert every `PreferencesSection` value has ≥1 entry and a section control.
Assert no duplicate `AnchorKey`. Tag `[Collection(nameof(AvaloniaTestCollection))]` (constructs
Avalonia controls).
**Depends on:** Steps 2, 9
**Verify:** `dotnet test --filter PreferenceIndexTests`.

## Step 13: shared screen-chrome styles (added mid-implementation)

**Files:** `src/Paperbunkr.App/Styles/ScreenChrome.axaml` (new), `src/Paperbunkr.App/App.axaml` (edit — register in `Application.Styles`)
**What:** promote the style blocks duplicated across `ReadingScreen.axaml` and `EventsScreen.axaml`
(`Border.statCard`, `TextBlock.statNumber`, `TextBlock.statLabel`, `Border.issueRow`,
`TextBox.searchBox`) into one shared file, rebuilt on design tokens (`PbSurface2Brush`,
`PbBorderBrush`, `PbRadiusSm`, `pbTextCaption`), replacing `#383D47`/`PbChromeBrush` literals.
**Depends on:** none
**Verify:** build.

## Step 14: restyle the Reading Lists screen

**Files:** `src/Paperbunkr.App/Views/ReadingScreen.axaml` (edit)
**What:** drop the local style block (use Step 13's shared styles + the `Button.primary/.secondary/.ghost`
primitives in place of `headerAction`), header title → `TextBlock.pbTextHeading`, every raster
`ImageBrush`+`OpacityMask` icon → `Path.pbIcon` vector (needs new `PbIconFileUp`, `PbIconFileDown`,
`PbIconInfo` geometries + `icon-mapping.md`). No binding/command/VM changes.
**Depends on:** Steps 6, 13
**Verify:** build; `ReadingScreenViewModelTests` (unchanged, must stay green); on-screen pass.

## Step 15: restyle the Story Events screen

**Files:** `src/Paperbunkr.App/Views/EventsScreen.axaml` (edit)
**What:** same treatment as Step 14 across all three modes (Events / Continuities / Timeline).
**Caveat:** `EventsScreen.axaml` + `EventsScreenViewModel.cs` are uncommitted-modified in the tree
by in-flight metadata phases 4d–4g work; this restyle is layered onto that current content and may
need a manual merge if the other work lands concurrently. No binding/command/VM changes here.
**Depends on:** Steps 6, 13
**Verify:** build; `EventsScreenViewModelTests` (unchanged, must stay green); on-screen pass.

## Deferred: "Reading Lists" / "Story Events" Preferences sections

Parked, not built. `AppSettings` has no reading-list or story-event fields today, so a dedicated
Preferences section would be a hollow placeholder — the same call the project already made for the
Scripts tab. Revisit when real settings exist for either (e.g. CBL import defaults, event-relation
auto-link toggles). The 8-section shell + `PreferenceIndex` structure absorb a 9th/10th section
with no rework.

## Step 12: full build, full test suite, manual on-screen pass
**What:** `dotnet build` clean (delete `obj/Debug/net10.0/Paperbunkr.App.dll`+`.pdb` and rebuild
if XAML compile fails after CoreCompile). Full `dotnet test` (not filtered — `DatabasePathOverride`
isolation). Launch app: each of 8 sections renders + hard-switches; search filters sidebar,
selecting a result opens the section and pulses the target group; `ReducedMotion` on ⇒ pulse is
instant; spot-check a relocated control in each section still works (add watched folder, tracker
field, fit-mode change, keybinding change).
**Depends on:** all
**Verify:** green build + suite; manual checklist.
