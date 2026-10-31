# Plugin API 4.5 — completing the 2026-09-20 plugin pitch (items 1–14)

*Date: 2026-10-28. The remaining items of the roadmap's "Plugin API pitches — deferred" list (items 15–20 shipped in
4.2, `docs/superpowers/specs/2026-09-20-plugin-api-4-2-followons-design.md`). Two grilling rounds; the user accepted every
recommendation in both. Builds on the 4.1 and 4.2 specs (read their "as implemented" notes). Status: design.*

## Decisions

| # | Decision |
|---|---|
| R1 Q1 | One spec, six slices, built in this order: **A developer experience (9, 10, 11) → B hooks (13) → D data and rules (5, 8) → C providers (1, 2, 3) → E UI surfaces (4, 14) → F network (6)**. Each slice ships on its own. |
| R1 Q2 | **Cut 12** (plugin-to-plugin registry: no consumer, invites load-order problems) and **cut 7** (scoped file system: unenforceable - scripts can call `System.IO`, native plugins are full trust). |
| R1 Q3 | One version bump: **`PluginApi.Current` → 4.5** for every new callable member in this spec. |
| R1 Q13, Q14 | `EventCompleted` is already covered by 4.4's `ContinuityCompleted` (continuities *and* story events). Python plugins get every manifest-declared surface but no native Avalonia controls; that closes item 14. |
| R2 Q6 | Smart fields are **typed custom values**, not a new table: books already carry CE custom values (database-only, never written to files), and smart lists already filter on them as text. |
| R2 Q8 | Providers register through `Environment.Providers` with async delegates, so C#, Python and native plugins all can. 30 s per call. |
| R2 Q9 | A plugin metadata provider's results only become proposals; nothing is stored as a link (the built-in providers are a persisted enum). |
| R2 Q12 | Every fresh install gets a confirm card - today only an overwrite asks. |

## A. Developer experience (items 9, 10, 11)

### A1 Dev mode
- `AppSettings.PluginDeveloperMode` (bool, default false, one migration). Toggle on the Plugin screen header ("Developer mode").
- On: a `PluginFolderWatcher` (App) watches the plugins root recursively for `*.csx`, `*.py`, `plugin.xml`. Changes are debounced
  **500 ms** (quiet period), then `PluginHostService.RediscoverPlugins()` (the existing full re-discovery) runs on the UI thread and the
  Plugin screen refreshes **in place** (the CLAUDE.md removal-during-event rule: the refresh is posted, never run inside a click).
- A change under a native plugin's folder does not reload it (a loaded `AssemblyLoadContext` can't be replaced): the detail pane shows
  "Native plugins reload on restart".
- A reload that leaves a plugin blocked or broken raises one Activity Center toast ("<name> didn't reload: <reason>"), deduped per plugin;
  a clean reload is silent.
- Off: no watcher. Per-command `CommandStats` reset on rediscovery (they belong to the old `Command` objects) - documented, not fixed.

### A2 Live log pane
- Dev mode only, in the plugin detail pane under the commands. Shows the last **200** lines of `<key>.log` (`PluginLogFiles.PathFor`), in a
  monospace `TextBlock` inside a `ScrollViewer` capped at 240 px, auto-scrolling to the end while the user is at the bottom.
- Follows the file with a 1 s poll while the pane is visible (a watcher on a file the host itself appends to is noisier than a poll).
  Buttons: **Copy** (clipboard), **Clear** (truncates the live file; the rolled `.1.log` is kept). Missing file = "Nothing logged yet."

### A3 Scaffolding
- Dev mode only: **New plugin…** button. A small `DialogCard` asks for name (key derived, editable), template (**Script** / **Native**),
  and language for Script (C# / Python). Creates `%AppData%\Paperbunkr\plugins\<key>\` and re-discovers; an existing key is refused.
- **Script (C#):** `plugin.xml` (`requiresApi="4.5"`, one Library command, one setting), `main.csx` that starts with
  `#r "<app dir>\Paperbunkr.Plugins.Abstractions.dll"` and `#r "<app dir>\Paperbunkr.Data.dll"` (absolute paths of the running app;
  `BlockedMetadataReferenceResolver` allows them and the host already references them, so they are no-ops at run time), a comment that
  `Environment` is supplied by the host and an editor can't see it, and a sample that logs and returns.
- **Script (Python):** `plugin.xml` + `main.py` with the same sample.
- **Native:** `plugin.xml` (`tier="Native"`), `<Name>.csproj` (class library, `net10.0`, references to the two dlls with `Private=false`,
  `OutputPath` = the plugin folder), `Module.cs` implementing `INativePluginModule` with one command. Building needs the .NET SDK; the
  template's `README.txt` says so. The native template is written but not built by the host.

### A4 Testing harness
- New project `src/Paperbunkr.Plugins.Testing` (class library, no xUnit dependency): `FakePluginEnvironment`, `FakeNativePluginEnvironment`,
  `FakePluginLogger`, a `PluginScriptRunner` (discover one plugin folder and invoke a command by key) and a temp-SQLite `FixtureLibrary`.
- The existing fakes move out of `Paperbunkr.Plugins.Tests`, which then references the new project - so the harness is exercised by the
  same ~230 tests. Not published to NuGet; the native template's README points at the project.

## B. Hooks (item 13)

| Hook | Payload | Raised by |
|---|---|---|
| `SeriesStatusChanged` | `SeriesId`, `SeriesName`, `OldStatus`, `NewStatus` (strings) | `PaperbunkrDbContext.SaveChanges` / `SaveChangesAsync`: tracked `Series` entries whose `Status` property is modified, compared original→current, one event per series per save, released by `RunAfterSave` (so never for a failed save). One choke point instead of ~17 write sites. Raised on `LibraryEvents.Default`. |
| `ScheduledTaskRan` | `TaskId`, `TaskName`, `StartedUtc`, `Duration`, `Outcome` (`Succeeded`/`Failed`/`Cancelled`), `Message` | `SchedulerService` after every run, scheduled or "Run now". |

Both are notification-only domain hooks through `DomainHookDispatcher` (same queue/timeout/telemetry rules), added to
`PluginHooks.DomainEventHooks`, with a `LibraryEvents` event each and `PluginHostService` subscriptions.

## D. Data and rules (items 5, 8)

### D1 Smart fields (item 5)
- Manifest: `<Fields><Field key="daysSinceRead" label="Days since read" type="number|text|date|toggle"/></Fields>`. Parsed and validated at
  discovery like `<Settings>` (unknown type, duplicate key → plugin blocked with the reason). Exposed as `PluginEngine.FieldSchemas`.
- Storage: the existing `IssueCustomValue`, name **`<pluginKey>.<fieldKey>`**. Values are invariant-culture strings (number `0.##`,
  date `yyyy-MM-dd`, toggle `true`/`false`).
- `Environment.Fields` (`IPluginFields`): `Set(Issue, string field, string? value)` (null removes; the value is validated against the declared
  type, a bad value returns false and is logged), `Get(Issue, string field)`, `Clear(string field)` (all issues). Undeclared fields return false.
- Smart lists: the SmartList editor's field picker gains a "Plugin fields" group listing every declared field by label (from enabled plugins).
  Choosing one stores a `CustomValue` condition with `CustomValueName = <pluginKey>.<fieldKey>`, so **no rule-schema change**. The condition's
  operators and comparison follow the declared type: `SmartListQueryBuilder` looks the name up in a `PluginFieldTypeRegistry` (Data, filled
  by the host) and compares as number/date/bool instead of text. An unknown name (plugin removed) falls back to the text comparison it has
  today. `IRulesEngine` gets this for free because it uses the same builder.
- Sort/group axes: not in scope.

### D2 Batch writes with a dry run (item 8)
- `IMetadataWriter.BeginBatch()` returns `IMetadataWriteBatch` with the same `Set*`/`AddTag`/`RemoveTag`/`SetCustomValue` methods (recording,
  not writing) plus `Task<bool> PreviewAndApplyAsync(string title)`.
- `PreviewAndApplyAsync` computes the diff (per book: field, old, new; no-op writes dropped). Empty diff → returns false, logs "nothing to
  change", no dialog. Otherwise shows a `DialogCard` (Large): title, "N changes to M books", a scrolling list grouped by book
  (`pbTextBody` book line, `pbTextMeta` "Field: old → new" lines), footer Cancel (ghost) + **Apply** (primary). Apply writes everything on
  one context in one transaction; any failure rolls back and returns false. Cancel returns false. The usual write-back/audit log runs per field.
- Batch writes count as having asked: a `confirmWrites="true"` command may apply a batch without a separate `AskQuestion`.

## C. Providers (items 1, 2, 3)

`Environment.Providers` (`IPluginProviders`), registration lasts until the plugin is rediscovered/disabled; a second registration of the same
kind by the same plugin replaces the first.

| Method | Delegates | Appears |
|---|---|---|
| `RegisterMetadata(string displayName, search, get)` | `search(query, ct) → IReadOnlyList<PluginMetadataMatch(id, title, url?)>`, `get(id, ct) → PluginSeriesMetadata` (title, description, genres, status, publisher, start year, cover url, url) | The Detail screen's external-metadata search lists "<displayName> (plugin)" as a source. Choosing a result creates `MetadataProposal`s for the fields it has (existing proposal path); **nothing is stored as a link**. |
| `RegisterCovers(string displayName, getCovers)` | `getCovers(PluginCoverRequest(seriesName, number?, year?, issueId?, seriesId?), ct) → IReadOnlyList<PluginCover(thumbUrl, fullUrl, label)>` | The cover picker gets a "From <displayName>" group alongside the provider candidates. |
| `RegisterArcSource(string displayName, bool browsable, search, getIssues)` | `search(query, ct) → arcs(id, name, publisher?, year?)`, `getIssues(id, ct) → entries(series, number, volume?, year?)` | `ReadingListSourceRegistry` lists it as `plugin:<key>`; lists imported from it store that key, so Refresh finds it again - or says "<name> is unavailable (plugin removed or disabled)". |

- Host side: `PluginProviderRegistry` (App) holds the delegates, wraps each call in a **30 s** timeout + exception guard (a failure or
  timeout = "source unavailable" in the UI, and written to the plugin's log). The built-in seams it
  plugs into: `IReadingListSource` (adapter class `PluginReadingListSource`), the cover picker's candidate loading, and the metadata search's
  source list.

## E. UI surfaces (items 4, 14)

- **Context menus:** `<Command menus="library,detail,readinglist,books" …/>` (comma list; default for a Library-hook command is `library`, for
  a Books-hook command `books`, i.e. today's behaviour). Each `*ContextMenuBuilder` asks the host for the commands that list its menu: one →
  an inline item, two or more → a "Plugins" submenu. The command receives the same selection payload it gets from the Library menu.
- **Library toolbar:** `toolbar="true"` → the command is listed in a **Plugins** dropdown. *Correction found while writing the spec: the
  Library toolbar has no overflow menu to put a submenu in.* So the toolbar's action group (after Scan / Resync / Add issue) gets one
  `Button.iconButton` (`PuzzlePiece`, name "Plugin commands"), visible only when at least one enabled command declares `toolbar="true"`,
  opening a `Border.dropdown` of `Button.menuRow`s (`PopupFit.ToWindow`, ScrollViewer). It never appears for a user with no such plugin.
- **Issue detail panel (native):** `INativeIssueDetailUi.CreateIssueDetailView(env, Issue) → Control?` (next to `INativeSeriesDetailUi`);
  shown as a section at the bottom of the issue Details tab under a `pbTextSection` heading with the plugin name. Null declines.
- **Insights card (native):** `INativeInsightsWidget.CreateInsightsView(env) → Control?`, max one per plugin, host-drawn `Border.card` with the
  plugin name as `pbTextTitle`, at the bottom of Insights → Today.
- A view that throws while being created is skipped and logged to the plugin's log; it never breaks the host screen.
- Python: menus/toolbar/settings/HTML hooks all work from `plugin.xml`; no native views.

## F. Network declaration (item 6)

- Manifest: `<Network><Host name="api.example.com"/></Network>` (exact host, or `*.example.com`).
- **Install confirm** (new, every fresh install; the existing overwrite/update prompts gain the same body): a `DialogCard` (Medium) with name,
  version, tier and "Declared network access: api.example.com, …" or "No network access declared", footer Cancel + **Install**. The plugin's
  detail pane shows the same declared-hosts line.
- `Environment.Http` (`IPluginHttp`): `GetStringAsync`, `GetBytesAsync`, `SendAsync(HttpRequestMessage)`; a request to an undeclared host
  throws `PluginNetworkException("<host> isn't declared in plugin.xml")` and logs it. A raw `HttpClient` is **not** policed; the UI says
  "declared", never "restricted to".

## Versioning
`PluginApi.Current` = **4.5**: `Environment.Fields`, `Environment.Providers`, `Environment.Http`, `IMetadataWriter.BeginBatch`, the two hooks,
and the two native UI interfaces. Manifest additions (`<Fields>`, `<Network>`, `menus`, `toolbar`) need 4.5 to have any effect but are ignored
by an older host.

## Testing
Each slice: Plugins.Tests / Data.Tests / App.Tests for the pure logic and the host wiring (headless where a view is involved), plus the
design-language lint run for every XAML change. On-screen checks are owed for: dev-mode toggle + reload + log pane, New plugin dialog,
batch diff dialog, plugin field group in the SmartList editor, the three provider surfaces, context-menu/toolbar submenus, the detail and
Insights plugin sections, and the install confirm.

## Non-goals
Plugin-to-plugin registry (12), scoped file system (7), hot-reloading native plugins, persisted provider links, sort/group axes for plugin
fields, NuGet publishing, policing raw `HttpClient`.

## As implemented (2026-10-28)

All six slices are built and tested headlessly; nothing is committed and nothing has been looked at on screen. `PluginApi.Current` is 4.5.
New members on `IPluginEnvironment` (`Fields`, `Providers`, `Http`) and `IMetadataWriter.BeginBatch` have **default interface
implementations** that decline, so the test doubles and any older environment keep compiling; the app's environment supplies real ones.

Deviations from the text above, found while building:

- **A1** The dev-mode switch lives in `%AppData%\Paperbunkr\plugin-developer.json`, not `AppSettings`: the migration snapshot was carrying
  another session's uncommitted migrations, and a tooling preference doesn't need a column. `PluginHostService` now discovers through its
  `PluginsRoot` seam and reads overrides through `ContextFactory` (both were hard-wired to the real folder and database). A dev reload also
  re-runs the changed plugin's `Startup` commands, so a provider plugin re-registers its sources.
- **A2** The log pane doesn't auto-scroll to the end; it re-reads once a second only when the text changed.
- **A3** The sandbox's `#r` resolver (`BlockedMetadataReferenceResolver`) used to resolve every `#r` to nothing, which Roslyn reports as
  "metadata file not found" - so the template's editor lines broke compilation. It now resolves a `#r` naming `Paperbunkr.Plugins.Abstractions`
  or `Paperbunkr.Data` (any path) to the assembly the script already has; every other `#r` still resolves to nothing (`PluginSandboxTests`
  covers both, including an EF Core look-alike path).
- **A4** The fakes moved with `git mv` into `Paperbunkr.Plugins.Testing` (now public, namespace `Paperbunkr.Plugins.Testing`); Plugins.Tests
  references the project and has a global `using`.
- **B** `SeriesStatusChangedEvent` already existed (smart features, 4.4 work), raised by the context after the save - the hook only subscribes
  to it. Statuses are the `SeriesStatus` names. `ScheduledTaskRanEvent` is new on `LibraryEvents`; `SchedulerService.Events` is its seam.
- **D2** The preview is the shared confirm dialog with its scrolling item list ("Book #1 - Format: (empty) → Trade Paperback"), not a new
  dedicated view.
- **C** Plugin metadata becomes **Pending** proposals with source `Other` for Summary, Status (normalised; unknown dropped), Genre and Publisher -
  title, start year and cover URL aren't proposed (no proposal field fits them without a link). Plugin covers fill the cover picker's existing
  External tab, which now shows whenever any plugin cover source is registered. A `plugin:` key whose plugin is gone resolves to
  `PluginReadingListSource.Unavailable`, whose calls throw the existing `ReadingListSourceException` with the reason.
- **E** Menus keep the existing "Plugins" submenu even for one command (no inline single item). The toolbar's Plugins button reuses the
  Library view model's orphaned `IsLibraryPluginMenuOpen`/`RunLibraryPluginOnSelection` (the earlier toolbar redesign had dropped its button);
  its rows run on the selection and are disabled without one. The native issue panel sits in the Detail screen's existing **Plugins tab** (the
  per-focused-issue tab the ComicInfo hooks already use), not at the bottom of Details. The reading-list member menu finds the host through
  `PluginHostService.Current` (it's built in five places with nothing to inject).
- **F** The fresh-install confirm and the update/overwrite prompts all use the shared confirm dialog; the declared hosts are read straight from
  the package zip's `plugin.xml` before installing.

**Not covered by a test:** the cover picker's plugin path (its constructor reads the real database), native issue/Insights views from a real
loaded module, the toolbar button and popup, and every view on screen.
