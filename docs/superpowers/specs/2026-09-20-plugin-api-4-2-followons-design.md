# Plugin API 4.2 — follow-ons to 4.1 (logger, telemetry, backstop, locked settings, reset, export/import)

*Date: 2026-09-20. Roadmap backlog items 15–20, taken as one spec after a grilling round in which the user
accepted every recommendation. Builds on `2026-09-20-plugin-api-4-1-design.md` (read its "as implemented"
notes first). Status: design; implementation follows in the order below.*

## Decisions (settled in the grilling round)

| # | Decision |
|---|---|
| Q1 | One spec, six slices, in this order: **logger (19) → telemetry (15) → backstop (20) → locked settings (17) → reset (18, replaced) → export/import (16)**. Each ships on its own. |
| Q2 | Telemetry is measured in `Command.InvokeAsync` (so every hook, CE-era and new, and every caller is covered), kept **in memory only** for the session: runs, failures, average/max/last duration, plus the dispatcher's timed-out and dropped counts. |
| Q3 | Shown as a read-only performance line per command in the Plugin screen's detail pane. No new page. |
| Q4 | `Environment.Log` (Debug/Info/Warn/Error, Error also takes an exception) → `%AppData%\Paperbunkr\logs\plugins\<key>.log`, UTC timestamp + level per line, 1 MB cap rolling to `<key>.1.log` (one previous file kept), thread-safe. The host also writes that plugin's failures, timeouts and dropped-event reports into its log. Both tiers. "Open log folder" button on the Plugin screen. The live log pane stays in backlog item 9. |
| Q5 | `Environment.Log` is a new callable member, so **`PluginApi.Current` → 4.2**. The other slices add no callable members (`locked` is a manifest attribute; the rest is host behaviour). |
| Q6 | `PaperbunkrDbContext.SaveChanges` becomes a **gap-filler**: reading-list item adds/removes on a list that no `ReadingListManager` call covered on that context are announced by the context itself (`Added`/`Removed`) and a bypass diagnostic is raised. No interceptor, no analyzer. |
| Q7 | `locked="true"` engages once a value is **stored** (first-time setup stays editable). It blocks only the **user** editing in the overlay; the plugin's own `SetSetting` is unaffected. Unlock is an explicit two-step confirm, per overlay session. Reset is blocked while locked. |
| Q8 | Export is one JSON file per plugin from the overlay: all stored keys **except declared secrets** (DPAPI is bound to one Windows user; exporting plaintext would defeat it). Import validates against the schema, skips and reports invalid values, skips locked settings that already have a value, imports undeclared keys as-is. |
| Q9 | The separate defaults file is **dropped** (`default=` already exists, nothing can "clear configuration", and two sources would disagree). Replaced by **Reset to defaults**: per setting and "Reset all". |

**Defaults chosen without a second round (review these):** the export file shape below; the Export/Import/
Reset-all buttons live in the settings overlay header and the Reset button on each row; an import file for a
*different* plugin key is refused; unlock and the locked state are per overlay session and not persisted.

## 1. Logger (backlog 19)

- `IPluginLogger { Debug(string); Info(string); Warn(string); Error(string, Exception? = null) }` in
  `Paperbunkr.Plugins`; `IPluginEnvironment.Log`. Built **per access** from the current clone's `PluginKey`
  (same reasoning as `Activity`: the environment is shallow-cloned per command).
- `PluginLogFiles` (App) owns the files: key sanitised for the filename, one lock per file, `2026-09-20T13:45:01.123Z [INF] message`
  lines, an exception's full text indented under its line, roll at 1 MB. Directory = `DiagnosticsService.LogDirectory`
  + `plugins`, injectable for tests. A logging failure never reaches the plugin.
- The host writes into the same file: domain-hook problems (§ 4.1 dispatcher) and command failures reported by
  `PluginHostService`. Note in the wiki that a plugin should not log secrets.
- Plugin screen: "Open log folder" opens the plugin logs folder (created if missing).

## 2. Telemetry (backlog 15)

- `CommandStats` on each `Command` (thread-safe): `Runs`, `Failures`, `TotalDuration`, `MaxDuration`,
  `LastDuration`, `Average`, `TimedOut`, `Dropped`. `Command.InvokeAsync` times every call and counts a thrown
  exception as a failure (then rethrows unchanged). `DomainHookDispatcher` bumps `TimedOut`/`Dropped`.
- The command row shows one muted line when there's anything to show: `12 runs · avg 45 ms · max 210 ms · 1 failed · 1 timed out`.
  Snapshot at pane build (selecting the package rebuilds it); memory only, gone on restart.

## 3. Backstop (backlog 20)

- `PaperbunkrDbContext` records which lists a `ReadingListManager` call covered on that context
  (`MarkReadingListManaged`, by id for an existing list, by reference for one not yet saved). Before saving, any tracked
  `ReadingListItem` `Added`/`Deleted` entry on an **uncovered** list is turned into a fallback `ReadingListChangedEvent`
  (`Added` with the issue ids, `Removed` with the issue ids), released after a successful save like every staged action,
  plus a `LibraryEvents.ManagerBypassed` diagnostic (the host logs it). `Record` marks its list covered **even when its
  kind is `None`** (an arc refresh that only removed duplicate rows must not announce a removal).
- Not flagged: relinking (an item's `IssueId` changes, state Modified), and deleting a whole list.

## 4. Locked settings (backlog 17)

- `<Setting locked="true"/>` (`true`/`false`, anything else is a schema error). `PluginSettingDefinition.Locked`.
- `PluginSettingsAccess.IsLocked(pluginKey, definition)` = `definition.Locked && a value is stored`. The overlay row
  computes it when it opens, so it doesn't lock mid-typing; while locked the inputs are disabled, a note explains why,
  and an "Unlock" two-step confirm enables editing for that overlay session.

## 5. Reset to defaults (backlog 18, replaced)

- `PluginSettingsAccess.Reset(pluginKey, key)` removes the stored row so reads fall back to the declared default.
  Per-row Reset (shown when a value is stored and the row isn't locked) and a header "Reset all" two-step confirm that
  skips locked rows and says how many it skipped. A reset secret is cleared.

## 6. Export / import (backlog 16)

```json
{
  "format": "paperbunkr-plugin-settings",
  "version": 1,
  "plugin": "my-sync",
  "pluginName": "My Sync",
  "apiVersion": "4.2",
  "exportedUtc": "2026-09-20T13:45:01Z",
  "secretsExcluded": ["token"],
  "settings": { "limit": "50", "mode": "fast", "server": "https://example.com" }
}
```
- Export: every stored key for the plugin, sorted, minus declared secrets (listed in `secretsExcluded`).
- Import: rejects malformed JSON, another `format`/`version`, or a different `plugin` key. Then per key: a declared secret is
  skipped; an invalid value is skipped with its reason; a locked setting that already has a value is skipped; everything
  else is written (undeclared keys verbatim). The overlay shows a one-line summary and refreshes its rows **in place**
  (never by clearing the collection from inside a click - see the removal-during-event gotcha in `CLAUDE.md`).

## Non-goals
Change-notification callbacks, a live log pane (backlog 9), persisted telemetry, an analyzer, a separate defaults file.

## As implemented (2026-09-20)

All six slices are built and tested; nothing is committed and no on-screen check has been done yet.

- **Backstop** (`PaperbunkrDbContext.AnnounceUnmanagedReadingListWrites`): announces on `LibraryEvents.Default` only - the
  context has no other hub - so tests listen there under a unique list name (`ReadingListBackstopTests`). The bypass text
  goes to `LibraryEvents.ManagerBypassed`, which `PluginHostService` logs as a diagnostics milestone.
- **Locked / reset / export-import**: `PluginSettingsAccess` gained `HasStored`, `IsLocked`, `Reset` and `StoredEntries`;
  the file format lives in `PluginSettingsTransfer` (testable without a picker); the overlay view-model takes an optional
  `IFilePickerService` (the host passes a real one). Rows are refreshed in place after a reset or import, never rebuilt.
- **Unlock is per overlay session**: the lock is computed once when the overlay opens, so saving the first value of a
  `locked` setting doesn't lock it under the user's fingers.
- A `Command` timing/`CommandStats` test (`Every_dropped_event_...`) failed twice under heavy machine load before it waited
  for the script to actually start; it now uses a marker file instead of a fixed delay.
