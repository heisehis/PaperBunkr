# Keyboard Shortcuts master-detail: Implementation Plan
*Implements: docs/superpowers/specs/2026-10-04-keyboard-shortcuts-master-detail-design.md*

All paths are under `src/Paperbunkr.App` (tests under `src/Paperbunkr.App.Tests`). Never run two `dotnet build`/`test` at once.

## Step 1: View-model state (filters, selection, row summaries, conflict details)
**Files:** ViewModels/ShortcutsEditorViewModel.cs (edit; also holds `ShortcutRowViewModel`, `ShortcutChip`, `ShortcutGroupViewModel`), Services/Input/InputScopeDisplay.cs (new)
**What:**
- `ShortcutChip` record becomes a small `ObservableObject` class (same `Binding`, `Label`; adds `IsPad`, `IsLocked` via `GamepadInputs.IsAxis`, observable `IsConflicted`).
- `ShortcutRowViewModel`: `SummaryChip`, `ExtraText` (`+N`), `HasExtra`, `ScopeText` (via `InputScopeDisplay`: scope name to "comic reader" etc. plus context text, nothing for `Always`),
  `IsSelected`, `Conflicts` (other label, shadows flag, other row) and `HasConflictDetails`, `MatchesDevice`, `MatchesSearch`. Chip `IsConflicted` is set where `RecomputeConflicts` already runs.
- `ShortcutsEditorViewModel`: `SearchText`, `DeviceFilter` (`ShortcutDeviceFilter`), `CustomisedOnly`, `SelectedRow` + `SelectRow`, `SelectGroupByTag`, `FilteredGroups`
  (rebuilt on filter change and at the end of `Refresh`/`SyncAll`, always including the selected row), `HasNoMatches`. `Groups`, `ConflictError`, `HasConflictError`, `IsConflicted` stay.
**Depends on:** none
**Verify:** new cases in ShortcutsEditorViewModelTests (filters, selection kept when filtered out, default selection, conflict details, summary, locked axis chips, `SelectGroupByTag`); `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~ShortcutsEditorViewModelTests"`.

## Step 2: Search-anchor wiring
**Files:** ViewModels/PreferencesScreenViewModel.cs (edit)
**What:** in the constructor, after `Shortcuts` exists, subscribe `ScrollToAnchorRequested` to call `Shortcuts.SelectGroupByTag(anchor)` for anchors starting `shortcuts.`.
**Depends on:** Step 1
**Verify:** a PreferencesScreenViewModelTests case: `ScrollToAnchor("shortcuts.zoomFit")` selects that group's first action (that class is tagged Slow, so run it by name).

## Step 3: The section view
**Files:** Views/Preferences/KeyboardShortcutsSection.axaml (rewrite), Views/Preferences/KeyboardShortcutsSection.axaml.cs (edit)
**What:** search box (`Name`d for `FocusSearch`), device `segToggle` buttons, Customised-only toggle, Import/Export/Reset buttons; left list (`ItemsControl` of group headers with `Tag` + `Button` rows,
`SelectRowCommand`, neutral warning icon, `+N`); right detail pane (title, scope line, chips with remove, Add shortcut / capture box, Reset, defaults, conflict block with "Select it");
narrow-width stacking via a `Bounds`/`SizeChanged` class toggle. Remove `Border.ksConflict` red styling and the red banner. Colours are `Pb*` tokens only. Selection on row `GotFocus` in the code-behind.
Add the code-behind in the same step as the axaml (CLAUDE.md build gotcha: there is no new `x:Class`, but keep the pair edited together).
**Depends on:** Steps 1 and 2
**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` (0 errors; if XAML compile fails after CoreCompile, delete the obj dll/pdb per CLAUDE.md before rebuilding), then a headless render check below.

## Step 4: Keyboard and controller behaviour
**Files:** Views/Preferences/KeyboardShortcutsSection.axaml.cs (edit)
**What:** a tunnel `KeyDown` handler on the list calling `FocusReclaimer.TryMoveDirectionally` against the Preferences screen; Right from a list row focuses the pane's first control;
Left from the pane (not inside the capture box) focuses the selected row's button; Ctrl+F already reaches the search box through the screen's `FocusSearch`.
**Depends on:** Step 3
**Verify:** a `RealWindowKeyboardTests` case (Slow trait is on two sweeps only, so this runs in the fast set): open Preferences > Keyboard Shortcuts in `MainWindow`, Down walks rows and updates the pane,
Right enters the pane, Left returns to the same row.

## Step 5: Polish and verification
**Files:** wiki/ and docs only if they describe this page (grep for "Keyboard Shortcuts"), Assets/Icons/icon-mapping.md (add the new icons)
**What:** headless PNG render of the section (see memory "About polish" trick) in both skins and narrow width; read `avalonia-pro-max/review-checklist/SKILL.md` and fix findings.
**Depends on:** Steps 3 and 4
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter "Speed!=Slow"`; on-screen check by the user is the only way to judge the look, and I will say it was not seen on a real display.
