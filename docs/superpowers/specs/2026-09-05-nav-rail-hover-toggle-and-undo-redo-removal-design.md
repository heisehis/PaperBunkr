# Nav Rail Hover Toggle + Undo/Redo Button Removal

**Status:** Implemented 2026-09-05. User-requested, direct follow-up to the 2026-08-24 navigation-shell
motion system and the 2026-09-04 navigation-transition-system work - small enough that this spec
covers design and plan in one document rather than the usual pair.

## Background

The nav rail (`MainWindow.axaml`) has hovered-expanded to 200px since the 2026-08-24 navigation
shell rework, with no opt-out - `MainWindow.axaml.cs`'s `RailPointerEntered`/`RailPointerExited`
always set `MainViewModel.IsNavRailHoverExpanded`, debounced by `_railCollapseTimer`. Separately, the
rail's bottom utility row carried standalone Undo/Redo buttons for metadata-edit history
(`MetadataEditHistoryService`) with no keyboard-shortcut equivalent anywhere in the app.

## Changes

**1. Hover-expand becomes a Preferences toggle.**
- `AppSettings.NavRailHoverExpandEnabled` (bool, default `true` - preserves today's behavior for
  existing installs; `HasDefaultValue(true)` set explicitly in `PaperbunkrDbContext.OnModelCreating`,
  same requirement every other `= true`-default `AppSettings` bool has, or an existing row backfills
  to `false` instead). Migration: `AddNavRailHoverExpandEnabled`.
- Preferences → Appearance gets a new "Navigation" group box (`AppearanceSection.axaml`), mirroring
  the existing "Motion" (`ReducedMotion`) group box's shape: one `CheckBox` + a caption line.
  `PreferencesScreenViewModel.NavRailHoverExpandEnabled` loads/persists via the same
  `PersistBehaviorSetting` helper every other Behavior-tab bool already uses.
- **The live gate itself does NOT read from `PreferencesScreenViewModel`** - `RailPointerEntered`
  reads `AppSettings.NavRailHoverExpandEnabled` fresh from a throwaway `PaperbunkrDb.CreateContext()`
  on every hover. `PreferencesScreenViewModel`'s own copy is lazy-loaded (only populated once
  `EnsureLoaded()` runs, i.e. once the user visits Preferences) - proxying the live rail behavior
  through it would leave hover-expand silently disabled-by-default-zero-value for anyone who never
  opens Preferences that session. `NavRailPinned` (the sibling "always expanded" mechanism) already
  established this "live shell state lives outside the lazy-loaded Preferences VM" principle by
  living on `MainViewModel` instead; this toggle keeps the same spirit while still letting the
  *checkbox* live in `PreferencesScreenViewModel` where the user asked for it, since a cheap fresh DB
  read per hover is simpler than wiring a live cross-VM proxy for a low-frequency event.
- Pinning is unaffected either way - it's a separate, explicit mechanism.

**2. Undo/Redo buttons removed from the rail.**
- The two rail buttons + the separator that used to sit between them and Preferences are gone
  (`MainWindow.axaml`).
- `MainViewModel.UndoCommand`/`RedoCommand` (`[RelayCommand]`-generated, `Undo()`/`Redo()`) are
  unchanged - only their rail UI went away.
- New app-wide keyboard shortcuts, **Ctrl+Z** / **Ctrl+Y** (`MainWindow.axaml.cs`'s
  `OnMainWindowKeyDown`, same Tunnel-KeyDown mechanism as Ctrl+,/Ctrl+Tab), so the feature stays
  reachable - user's explicit choice over just letting it go fully unreachable.

## Testing

- `AddNavRailHoverExpandEnabledMigrationTests` - column adds with `defaultValue: true`, round-trips.
- `PreferencesScreenViewModelTests` - load-from-AppSettings and change-persists-to-AppSettings, same
  shape as the existing Behavior-batch2 tests.
- Manual/on-screen (no unattended GUI automation in this environment,
  `[[feedback_no_computer_use]]`): the checkbox actually gates hover-expand live, Ctrl+Z/Ctrl+Y
  actually trigger Undo/Redo, pinning still works with hover-expand off.
