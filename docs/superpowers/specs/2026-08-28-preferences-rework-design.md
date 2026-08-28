# Preferences Rework — Design

**Date:** 2026-08-28
**Phase:** Sub-project 7 of 7 in the full UI rework (see
`docs/superpowers/specs/2026-08-24-design-language-foundation-design.md` → "Full UI rework — phase
breakdown"). This is the last remaining phase.

## Background

`src/Paperbunkr.App/Views/PreferencesScreen.axaml` is a single 1002-line file with a horizontal
text tab strip (Appearance · Behavior · Libraries · Reader · Advanced · Plugins). It grew
organically across ~15 earlier specs and now has:

- **Pre-design-language styling** — local `Button.prefTab` / `Border.groupBox` / `Border.groupHeader`
  styles, raster `ImageBrush` + `Border.OpacityMask` icons instead of the Phase 6 `Path.pbIcon`
  vectors, none of the Phase 1 primitives.
- **Muddled information architecture** — a 2-checkbox "Behavior" tab while "App Behavior"
  (minimize-to-tray) sits under a sprawling "Advanced" tab alongside Rendering, File Association,
  Backup Manager, Reading List Sources *and* Trackers. Account connections (5 trackers + 2 metadata
  sources) are buried at the bottom of Advanced. Keyboard Shortcuts is buried at the bottom of
  Reader.
- **No search.**

The backing `PreferencesScreenViewModel` is a single 1450-line partial class. Its `#region`s are
already well organized and its persistence helpers (`PersistBehaviorSetting` / `PersistVirtualTag`
/ `PersistRenderingSetting`) are shared across every tab.

## Goals

1. **Visual restyle** onto the design-language tokens, primitives, and vector icons — consistent
   with the rest of the reworked app (Phase 7).
2. **Reorganize the IA** into clearer sections.
3. **Add search** that jumps to an individual setting.

## Non-goals / out of scope

- Per-control search granularity (group-card granularity only — see §4).
- Decomposing `PreferencesScreenViewModel` into child ViewModels — a separate refactor that does
  not serve these goals. The VM stays a single class.
- Any new settings, or changes to what a section *contains* beyond relocating existing groups.
  Section membership will be tweaked in later sessions; this phase just establishes the shell.
- Plugins section internals — the embedded `PluginScreen` is unchanged.
- The `windows_11` skin and the installable-skin `theme.json` schema — untouched.

## Architecture

**Approach: view split, shared ViewModel.** Break the one `.axaml` into a shell + 8 section
UserControls, all bound to the existing `PreferencesScreenViewModel`. Medium effort, large
maintainability win, low risk (no persistence code moves).

### Navigation shell

New `Views/PreferencesShell.axaml` (+ `.axaml.cs`) replacing `PreferencesScreen.axaml` as the
screen mounted by the nav rail. Three-part layout:

- **Header** — "PREFERENCES" title (`TextBlock.pbTextHeading`, Bebas) + a search box (see §4).
- **Left sidebar** (~200px, fixed) — the 8 section names as a vertical list. Selecting one is a
  **hard switch**: the right pane swaps to show only that section. The active item has an amber
  left-border (`PbAccentBrush`) + a subtle `PbSurface2` highlight. Sidebar item style is a new
  small token-based `Style Selector="Button.prefNavItem"` in the shell (not the deleted
  `prefTab`).
- **Right pane** — a `ContentControl` hosting exactly one section control, each section control
  wrapping its body in its own `ScrollViewer`.

`PreferencesScreenViewModel.ActiveTab` (string) → **`ActiveSection`** of a new
`Models/PreferencesSection.cs` enum:

```
enum PreferencesSection { General, Appearance, Library, Reader, KeyboardShortcuts, Connections, Plugins, Advanced }
```

The existing computed-bool pattern (`IsAppearanceTab` etc., mirrored from `DetailTabsViewModel`)
becomes `IsGeneralSection` … `IsAdvancedSection`. `Go<Section>Command` methods keep the same shape.

Section switching cross-fades over `PbMotionFast`; the duration resolves to 0 ms when the existing
`ReducedMotion` setting is on (same resolution path the motion tokens already use).

The shell is a full-width screen with no contextual sidebar — unchanged from today
(`ShowContextualSidebar` is not extended, matching the current Preferences/Plugins behavior).

### Section controls

Eight new `Views/Preferences/<Name>Section.axaml` (+ minimal `.axaml.cs` code-behind — required,
see CLAUDE.md "adding a new Avalonia View"). Each has `x:DataType="vm:PreferencesScreenViewModel"`
and is given the shell's DataContext. Content is the existing groups, relocated:

| Section | Groups (all already exist in `PreferencesScreen.axaml` today) |
|---|---|
| **General** | Resume reading + auto-advance (from *Behavior*); Minimize-to-tray (from *Advanced → App Behavior*) |
| **Appearance** | Skins; Install Skin; Font; Motion (reduce motion); Developer (debug-only); the "Future" placeholder is dropped |
| **Library** | Comic Library Folders; Book Folders; Migrate from ComicRack CE; Virtual Tags |
| **Reader** | Right to Left; Display; Zoom & Navigation; Image Adjustment; Background & Margin |
| **Keyboard Shortcuts** | Import/Export Layout; conflict banner; Navigation; Zoom & Fit; Display |
| **Connections** | Reading List Sources (ComicVine, Metron); Trackers (AniList, MyAnimeList, Shikimori, Bangumi, MangaBaka) |
| **Plugins** | `<views:PluginScreen DataContext="{Binding Plugin}" />` — unchanged |
| **Advanced** | Rendering; File Association; Backup Manager |

No behavior, binding, or command changes inside the groups — this is a cut/paste + reskin.

## Search → jump to a setting

### Index

New `Models/PreferenceIndex.cs` — a static `IReadOnlyList<PreferenceIndexEntry>`:

```
record PreferenceIndexEntry(
    PreferencesSection Section,
    string GroupTitle,     // e.g. "Display"
    string Title,          // display label in results, often == GroupTitle
    string[] Keywords,     // terms the group's controls should match: "double page", "spread", "fit mode", ...
    string AnchorKey);     // e.g. "reader.display"
```

**Granularity: one entry per group card** (~20 entries total), not one per control. `Keywords`
carries the individual control labels so "double-page spread" still finds Reader → Display.
Per-control granularity is a later add-on that would not require reworking this structure.

Each group `Border` in the section `.axaml` files carries `Tag="<AnchorKey>"`.

### Behavior

`PreferencesScreenViewModel` gains:

- `[ObservableProperty] string searchQuery`
- `ObservableCollection<PreferenceSearchResultViewModel> SearchResults` — recomputed on
  `searchQuery` change: case-insensitive substring match of the query against each entry's
  `Section` name, `GroupTitle`, `Title`, and `Keywords`; empty query → empty results.
- `bool IsSearching` → `!string.IsNullOrWhiteSpace(SearchQuery)`; while true the shell replaces the
  sidebar section list with the results list (the right pane keeps showing the last-active section
  underneath).
- `OpenSearchResultCommand(PreferenceSearchResultViewModel)` → sets `ActiveSection = result.Section`,
  clears `SearchQuery`, then raises an event / sets a property the shell observes to scroll the
  target into view.

Shell code-behind, on the "navigate to anchor" signal: after the section control is mounted (one
dispatcher tick), walk its visual tree for a `Border` with `Tag == AnchorKey`, call
`BringIntoView()`, then run a one-shot highlight pulse — a ~1 s `PbGlowRing` box-shadow animation
on that border, skipped entirely when `ReducedMotion` is on.

`Escape` in the search box clears it (reuses the app-wide Escape convention already wired
elsewhere).

## Visual restyle

- **Delete** `Button.prefTab` / `Button.prefTab.active` styles.
- `Border.groupBox` + `Border.groupHeader` → a new `Border.settingsGroup` + `Border.settingsGroupHeader`
  built on `PbSurface2` / `PbBorderBrush` / `PbRadiusMd`; header text uses `TextBlock.pbTextCaption`
  (uppercase). Defined once in the shell's `Styles` (or `Primitives.axaml` if a second consumer
  appears — not proactively).
- Section titles → `TextBlock.pbTextHeading`.
- `Button.headerAction primary` / `Button.headerAction ghost` → the real primitives
  `Button.primary` / `Button.secondary` / `Button.ghost` from `Primitives.axaml`.
- Every raster icon (`<ImageBrush Source="/Assets/Icons/*.png">` inside `Border.OpacityMask`) →
  a `Path.pbIcon` vector from `Styles/Icons.axaml`. Add any missing `PbIcon*` geometries (Folder
  Open/Add/Search, Trash, Archive, Undo, Cloud Upload, Circle Warning, Book, Loading, Add/Plus
  are the ones used here — most already exist from the Phase 6 icon pass). Update
  `src/Paperbunkr.App/Assets/Icons/icon-mapping.md` per the standing rule.
- ComboBox / TextBox / Slider / CheckBox already theme correctly via FluentAvalonia — only
  spacing/margin normalization against the `PbSpacingUnit` scale.
- Pane switch + search highlight both respect `ReducedMotion`.

## ViewModel changes (summary)

- `ActiveTab` (string) → `ActiveSection` (`PreferencesSection` enum); `Is*Tab` → `Is*Section`;
  `Go*Command` names follow.
- Add `SearchQuery`, `SearchResults`, `IsSearching`, `OpenSearchResultCommand`,
  `PreferenceSearchResultViewModel`.
- `OnActiveTabChanged` logic (currently persists last-viewed tab / lazy-loads a tab's data) is
  ported to `OnActiveSectionChanged` unchanged in intent.
- No changes to any `Persist*` helper, any `On<Setting>Changed` partial, or any command body
  inside a group.

## Testing / verification

- **`PreferenceIndexTests`** (new) — load each section `.axaml` via `AvaloniaRuntimeXamlLoader`
  and assert: (a) every `PreferenceIndexEntry.AnchorKey` matches a `Border.Tag` in its section;
  (b) every `PreferencesSection` enum value has ≥1 index entry and a corresponding section
  control type. This is the drift guard.
- **`PreferencesScreenViewModelTests`** — updated for the enum rename; add: query string →
  expected `SearchResults`; `OpenSearchResultCommand` sets `ActiveSection`; empty query →
  empty results; `ReducedMotion` on → resolved transition duration is 0.
- Existing `SkinServiceTests` / `WindowsElevenSkinTests` — unaffected (no schema change) but
  re-run.
- **Manual on-screen pass** (standing no-unattended-GUI caveat): launch the app, open
  Preferences, confirm each of the 8 sections renders and hard-switches; type in search, confirm
  the results list and that selecting one opens the section and pulses the target group; toggle
  reduce-motion and confirm the pane switch + pulse go instant; spot-check that every relocated
  group still functions (add a watched folder, connect a tracker field, change a fit mode).
- Full test suite run (not just the filtered subset) per the `DatabasePathOverride` /
  `AvaloniaTestCollection` isolation lesson from the original Preferences work.

## Build note

Per CLAUDE.md: each new `.axaml` (shell + 8 sections) ships with its code-behind `.cs` in the
same commit, and if a build fails inside XAML compilation after `CoreCompile` succeeded, delete
`obj/Debug/net10.0/Paperbunkr.App.dll` + `.pdb` (note: net10, not net8 — the solution migrated)
and rebuild rather than retrying `dotnet build`.
