# Plugin Screen (Duplicate Finder) Cleanup — Design Spec

*Date: 2026-08-09. Scope: `PluginScreenViewModel`/`PluginScreen.axaml` and the rail-nav "Pl" icon
only. Not building a real duplicate-finder or plugin system — that's explicitly Beta scope
(`alpha-roadmap.md`'s Plugin API v2 section). This is a P4-adjacent placeholder-content sweep.*

## 1. Problem

`PluginScreenViewModel` ships fully fake "Duplicate Finder" data: two hardcoded duplicate groups
(`Brass Horizon #12`, `Nightshift Orchid Vol. 4`) with invented file names/sizes/timestamps, a
hardcoded `GroupCount="7"`, `LastScanLabel="Last scan: 11 minutes ago · 1,847 series scanned"`, and
`PluginBadge="Plugin · Duplicate Finder v1.4"`. The screen's three buttons (Scan Library, Skip All,
Remove Selected Duplicates) have no `Command` bindings — dead controls. The rail-nav icon's tooltip
("Duplicate Finder (plugin)") and its badge (hardcoded `"7"`, matching the fake group count) present
this as a real, working feature. None of it is real; no plugin engine exists yet.

## 2. Fix

- Delete `Models/DuplicateGroupSample.cs` (`DuplicateGroupSample`/`DuplicateItemSample`) — nothing
  else references them once the fake data is gone.
- `PluginScreenViewModel`: strip to no properties/fake data at all. The screen becomes a genuine
  empty state, same pattern as Detail's Related/Activity tabs (docs/superpowers/specs/... "no
  activity-log schema/feature exists yet, so this is left as a real empty state rather than faked").
- `PluginScreen.axaml`: replace the group-list `ItemsControl` + stats card + three dead buttons with
  a centered empty-state block: heading "Plugins", body text "No plugins installed yet." plus a
  muted line "Plugins will appear here once the plugin API ships." (Beta backlog, already documented
  — no new commitment, just pointing at what's already scoped).
- `MainWindow.axaml`: rail icon's `ToolTip.Tip` changes from `"Duplicate Finder (plugin)"` to
  `"Plugins"`. The `Border`/`TextBlock` badge showing hardcoded `"7"` is removed entirely (there's
  no real count to show).
- Untouched: `GoPluginCommand`, `IsPlugin`, `Plugin` property on `MainViewModel`, the rail icon
  itself, and the `Views.PluginScreen`/`ViewModels.PluginScreenViewModel` class names — this stays
  the plugin screen's home, just honestly empty instead of fake-populated.

## 3. Testing

- No new unit tests needed — the change removes fake data and dead bindings, it doesn't add new
  logic to test. (`PluginScreenViewModelTests` doesn't exist today and isn't warranted for an
  empty view model with no behavior.)
- Manual: open Plugins from the rail nav, confirm the empty state renders instead of fake duplicate
  groups, and the rail icon shows no badge.
