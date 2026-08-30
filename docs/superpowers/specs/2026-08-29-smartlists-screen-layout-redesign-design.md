# Smart Lists Screen Layout Redesign — Design

**Date:** 2026-08-29
**Follows:** the Reading Lists screen redesign and the Events & Continuity screen redesign (both
`2026-08-28`), the two prior "remaining screens" of UI-rework Phase 7. Smart Lists was left on its
pre-redesign layout at the time — this closes that gap.

## Background

The Smart Lists sidebar (`MainWindow.axaml`, `IsSmart` block) and the editor pane
(`Views/SmartScreen.axaml`) still use the styling and structure that predates the Reading Lists v2
and Events & Continuity redesigns:

- **Sidebar:** active row is a soft accent-color fill (no left accent bar); "New Smart List" is a
  bottom dashed button rather than a header icon button; the custom-list delete icon is always
  visible instead of hover-only; the "Maintenance" disclosure uses literal `▸`/`▾` text glyphs.
- **Editor pane:** the whole screen (title/actions, condition builder, results grid) lives inside
  one `ScrollViewer`, so the header and the "Currently matches N issues" pill scroll out of view
  along with everything else. Reading Lists and Events & Continuity both pin their top band (and,
  for Reading Lists, a progress row) and only scroll the content below it.
- A handful of hardcoded values (`CornerRadius="5"/"6"/"7"`, border color `#383D47`) predate the
  design-token pass that already replaced the equivalent literals in `ReadingScreen.axaml` /
  `EventsScreen.axaml` (see `Styles/ScreenChrome.axaml`'s own comment, which documents that exact
  cleanup).

None of this is a functional gap — `SmartScreen.axaml`'s condition builder, results grid, and the
`Smart.*` sidebar bindings (`BuiltInLists`, `CustomLists`, `MaintenanceLists`, `SelectListCommand`,
`DeleteConfirm`, `ToggleMaintenanceCommand`, `CreateNewCommand`) all already work correctly — this
is a pure View-layer restyle/restructure to bring Smart Lists visually and structurally in line
with the rest of the reworked app.

## Goal

Make Smart Lists read as part of the same system as Reading Lists / Events & Continuity: same
sidebar row treatment, same pinned-header-then-scrolling-content editor structure, same design
tokens instead of ad-hoc literals.

## Non-goals / out of scope

- Consolidating the editor's Duplicate/Save/Cancel/Apply buttons into a "⋯ Manage" menu — confirmed
  during design review to keep the current two-button-pair shape (Duplicate + Save up top, Cancel +
  Apply below the condition card), just restyled.
- Any change to the condition-builder logic, `SmartListGroupViewModel`/`SmartListConditionViewModel`,
  or the SmartList v2 rule engine (nested AND/OR groups, operators, virtual tags) — all unchanged.
- A "New Smart List" naming dialog (the kind Reading Lists has) — Smart Lists only has one
  creatable kind of item, so the header `＋` button calls the existing parameterless
  `Smart.CreateNewCommand` directly, same behavior as today's bottom button just relocated.
- Promoting the condition-builder styles (`conditionPicker`, `conditionValue`, `conditionFlag`,
  `groupCard`, `resultTile`) into a shared `Styles/*.axaml` file — unlike `statCard`/`searchBox`,
  these aren't duplicated verbatim across other screens, so there's no duplication to collapse; they
  stay local to `SmartScreen.axaml`, just token-based instead of hardcoded.
- Results-grid tile styling (`Border.resultTile`) — already documented as intentionally matching
  `Border.issueTile` in `DetailTabs.axaml`; untouched.

## Sidebar (`MainWindow.axaml`, `IsSmart` block)

Rework to match the `ecRow` (Events & Continuity) / `rlRow` (Reading Lists) pattern already
established in this same file:

- New `Border.smRow` / `Border.smRow.active` styles (scoped to the `IsSmart` `StackPanel`, same as
  `ecRow`/`rlRow`): `BorderThickness="3,0,0,0"`, transparent by default, `PbAccentBrush` border +
  `PbSurface2Brush` background when `.active`.
- Each `SmartListSummary` row (built-in, custom, and maintenance) becomes a `Border.smRow` wrapping
  the existing `sideItemButton` + count badge, mirroring the `ecRow` `Grid ColumnDefinitions="*,Auto"`
  shape used for events/continuities.
- Header row gains a compact `＋` icon button (24×24, `PbSurface2Brush` background,
  `PbBorderBrush` border, `fi:SymbolIcon Symbol="Add"`) bound to `Smart.CreateNewCommand`, styled
  identically to the Events & Continuity header's `＋` button (minus its `MenuFlyout`, since there's
  only one creatable kind here). The bottom bordered "+ New Smart List" button is removed.
- Custom-list delete button (`fi:SymbolIcon Symbol="Delete"` bound to `DeleteConfirm.TriggerCommand`)
  gets a `smDelete` class; new styles `Border.smRow Button.smDelete { Opacity 0, IsHitTestVisible
  False }` / `Border.smRow:pointerover Button.smDelete { Opacity 1, IsHitTestVisible True }` make it
  hover-only, same mechanism as `ecDelete`/`rlDelete`.
- Built-in and maintenance list rows keep no delete button (unchanged — they're not deletable today
  either).
- "Maintenance" disclosure: replace the `▾ Maintenance` / `▸ Maintenance` `TextBlock`s with a single
  row using `fi:SymbolIcon Symbol="ArrowDown"` (expanded) / `Symbol="ChevronRight"` (collapsed),
  matching `EventsScreen.axaml`'s `RelatedEventsExpanded` disclosure exactly. Still a
  `Smart.ToggleMaintenanceCommand`-bound button; still collapsible (confirmed during design review —
  not flattened to a permanent group label).
- No `SmartListSummary` / `MainViewModel.Smart` changes — every binding used above already exists.

## Editor pane (`Views/SmartScreen.axaml`)

### Structure

Replace the outer `ScrollViewer > StackPanel` with:

```
Grid RowDefinitions="Auto,Auto,*"
├─ Row 0: top band — title/subtitle + Duplicate/Save (unchanged content, current Grid at line 220)
├─ Row 1: stat row — the "Currently matches N issues" pill (unchanged content, current Border at
│         line 244), on its own pinned row with the same bottom divider treatment as row 0
└─ Row 2: ScrollViewer — everything currently below the pill: the `RootGroup` condition-group
          card, the Cancel/Apply row, the empty-state message, and the results `ItemsControl`
```

Row 0 and row 1 keep their existing bottom `Border` dividers (`BorderThickness="0,0,0,1"`) so the
pinned/scrolling boundary reads the same way `ReadingScreen.axaml`'s top-band/progress-row dividers
do. The `MaxWidth="820" HorizontalAlignment="Left"` wrapper around the condition card + results
moves inside the row-2 `ScrollViewer` unchanged.

### Actions — unchanged shape

Duplicate (top, ghost) + Save (top, primary) stay; Cancel (ghost) + Apply (primary) stay below the
condition card, both still bound to the same commands they are today
(`DuplicateCommand`/`SaveCommand`, `CancelCommand`/`SaveCommand`). Confirmed during design review:
no "⋯ Manage" consolidation.

### Token cleanup

While restructuring this file, replace the hardcoded values that predate the design-token pass:

- `CornerRadius="5"` (five occurrences: `headerAction`, `conditionPicker`, `conditionValue`,
  `conditionFlag`, `modeToggle`) → `{DynamicResource PbRadiusSm}`
- `CornerRadius="7"` (`groupCard`) → `{DynamicResource PbRadius}`
- `CornerRadius="6"` (`resultTile`) → `{DynamicResource PbRadiusSm}`. This is a genuine drift fix,
  not just tokenization: `resultTile`'s own comment claims it "Matches `Border.issueTile` in
  `DetailTabs.axaml` exactly", but `issueTile` uses `{DynamicResource PbRadiusSm}` (`5`), not a
  hardcoded `6` — verified by reading `DetailTabs.axaml` directly. Using the token makes the
  existing comment true again instead of just tokenizing a stray literal.
- `BorderBrush="#383D47"` (`conditionPicker`, `conditionValue`, `conditionFlag`) →
  `{DynamicResource PbBorderBrush}`
- The `CornerRadius="999"` pill is left as a literal — there's no pill-radius token in the app.

No other visual changes — `groupCard`, `resultCard`, `modeToggle` colors, condition-row layout, and
the results grid (`VirtualizingWrapPanel`) are untouched.

## Out of scope for ViewModel/model

`SmartScreenViewModel`, `SmartListGroupViewModel`, `SmartListConditionViewModel`,
`MainViewModel.Smart`, and `SmartListSummary` are unchanged — every property/command this design
uses (`ListName`, `Subtitle`, `MatchCountLabel`, `RootGroup`, `IsReadOnly`, `DuplicateCommand`,
`SaveCommand`, `CancelCommand`, `Smart.BuiltInLists`/`CustomLists`/`MaintenanceLists`,
`Smart.SelectListCommand`, `Smart.CreateNewCommand`, `Smart.ToggleMaintenanceCommand`,
`Smart.IsMaintenanceExpanded`, `SmartListSummary.DeleteConfirm`) already exists.

## Testing

- No new `SmartScreenViewModelTests` / `MainViewModelTests` cases — no VM behavior changed.
- Build: `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` must be 0 errors. Both files are
  existing compiled views (no `x:Class` is new), so there's no `AVLN2000` risk per CLAUDE.md — but
  if a XAML-compile failure ever follows a successful `CoreCompile`, delete
  `obj/Debug/net10.0/Paperbunkr.App.dll`/`.pdb` and rebuild rather than retrying `dotnet build`.
- Manual on-screen pass (standing no-unattended-GUI caveat, so this is a request for the user to
  verify, not a claim of having done so): sidebar active-row left accent bar on built-in/custom/
  maintenance rows; header `＋` creates a new list and selects it; custom-list delete only appears
  on hover and still round-trips through the two-step confirm; Maintenance chevron toggles between
  `ChevronRight`/`ArrowDown` and still expands/collapses; editor top band + stat pill stay fixed
  while scrolling a list with enough results to overflow; Duplicate/Save/Cancel/Apply all still
  work; a read-only built-in list still hides Save/Cancel/Apply and the remove/add-condition
  affordances exactly as it does today.

## Build note

Per CLAUDE.md: both files are pre-existing compiled views, so the "new View" `AVLN2000` failure
mode doesn't apply here. Still, if a build ever fails inside XAML compilation after `CoreCompile`
already produced output, don't just retry `dotnet build` — delete
`obj/Debug/net10.0/Paperbunkr.App.dll` + `.pdb` first per the root-cause note at the top of
CLAUDE.md.
