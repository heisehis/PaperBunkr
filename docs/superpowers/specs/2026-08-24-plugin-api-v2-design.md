# Plugin API v2 — Design Spec

*Date: 2026-08-24. Scope: docs/onboarding.md §10/§11 — the full Beta-scoped plugin host: engine,
`IPluginEnvironment` + adapters, all 17 CE hook types wired to real invocation sites, the Plugin
screen UI, and the three net-new UI surfaces (info panel, Quick Open palette, thumbnail overlay)
that three of those hooks require. Proven against one real test plugin ("Duplicate Finder").
Supersedes the empty-state placeholder shipped in
docs/superpowers/specs/2026-08-09-plugin-screen-cleanup-design.md, which explicitly deferred this
work.*

## 1. Goals and non-goals

**Goal:** a working plugin host, modeled on CE's `IPluginEnvironment`/`PluginEngine` shape and
hook taxonomy, that lets third parties write genuinely novel automation against Paperbunkr without
recompiling it — "a healthier model (VS Code/Obsidian-style) than CE's plugin system had to be"
(onboarding.md §11).

**Non-goal: backward compatibility with existing ComicRack CE plugins.** Two independent breaks,
both deliberate:
- **Script language.** CE plugins ship IronPython (`.py`). This pass ships a C# scripting
  initializer only; `pythonnet` interop is an explicitly deferred follow-on spike (onboarding.md
  §10), and even that wouldn't guarantee literal compatibility with IronPython-era scripts.
- **API surface.** CE's `IPluginEnvironment` is WinForms/GDI+-shaped (`IWin32Window`,
  `System.Drawing.Bitmap`, `Keys` shortcuts, the old `ComicBook` type). Paperbunkr's adapters are
  native to its own Avalonia/EF Core world. "Keep the shape" (§10) means preserve the *level of
  capability* — library CRUD, navigation, thumbnails — not byte-for-byte compatibility.

Per §11, CE's actual plugins (Duplicate Finder, CBL Manager, ThemeFramework, Comic Vine Scraper,
CRReaderOverhaul) are being retired and absorbed as core Paperbunkr features, not ported as
plugins. This spec's one test plugin is written fresh against Paperbunkr's own abstractions.

## 2. Plugin package format and discovery

A plugin is a folder under `%AppData%\Paperbunkr\plugins\<plugin-key>\` (same convention as
`SkinPaths` for installed skins) containing:
- `plugin.xml` — a ported `CommandCollection` manifest (`Hook`, `Key`, `Name`, `Description`,
  `Image`, `PCount`, `Enabled` per `Command`, same XML shape as CE's `XmlPluginInitializer`
  produces).
- One `.csx` C# source file per `Command`, referenced by relative path from the manifest.

At startup, `PluginEngine.Discover()` walks the plugins directory off the UI thread:
1. `XmlPluginInitializer.GetCommands(file)` parses each `plugin.xml` into `Command` instances.
2. Each `Command` becomes a `CSharpCommand`, which `PreCompile()`s its script eagerly via
   `Microsoft.CodeAnalysis.CSharp.Scripting`, against a fixed reference set (core BCL +
   `Paperbunkr.Data` entities + the new `Paperbunkr.Plugins.Abstractions` assembly).
3. Commands register into `PluginEngine`'s `CommandCollection`, keyed by `Hook`.

Compiling eagerly (not lazily on first invoke) means a broken plugin surfaces its compile error on
the Plugin screen immediately, matching CE's `PreCompile` intent. A script that fails to compile is
registered but flagged broken/disabled — never silently dropped, never aborts discovery of other
plugins (mirrors CE's per-command `try {} catch {}` isolation in `PluginEngine.Initialize`).

## 3. Core engine components

**`PluginEngine`** — near-direct port of CE's: a `CommandCollection` registry, `GetCommands(hook)`
filtering by `Enabled` + hook match, `Invoke(hook, payload)` dispatching to every matching command,
and the 17 hook-name constants (renamed to Paperbunkr conventions) with their `ValidHooks`
description map feeding the Plugin screen's grouping/labels.

**`Command` (abstract) → `CSharpCommand`** — `Command` ports CE's shape close to unchanged: `Hook`,
`Key`, `Name`, `Description`, `Image`, `Enabled`, `Environment`, abstract `OnInvoke`. `CSharpCommand`
replaces `PythonCommand`: `PreCompile()` wraps the script text in a
`Script<object>` compiled against a `PluginGlobals`-derived base type; `Invoke` runs the compiled
delegate against a per-hook globals instance. Compile failures are caught and stored as a
`CompileError` string on the command rather than thrown.

**Typed globals and payloads** — `PluginGlobals` (base) exposes `Environment` (`IPluginEnvironment`).
Each of the 17 hooks gets a derived globals type carrying its typed payload — e.g.
`BooksHookGlobals { IReadOnlyList<Issue> Books }`, `ReaderResizedHookGlobals { Size NewSize }`,
`BookOpenedHookGlobals { Issue Book }`, down to payload-less lifecycle hooks
(`StartupHookGlobals`, `ShutdownHookGlobals`, carrying only `Environment`). All 17 live in
`Paperbunkr.Plugins.Abstractions`.

**Enable/disable persistence** — new `PluginCommandState` EF entity (`Id`, `PluginKey`,
`CommandKey`, `Enabled`), following `KeyBinding.cs`'s sparse-table convention: a command with no row
uses its manifest default; only user-toggled overrides get a row. No per-plugin migration needed.

## 4. `IPluginEnvironment` and its five sub-interfaces

Adaptations, not straight ports — Paperbunkr's app shape differs from CE's in ways that change
these interfaces, not just their implementations:

- **`IApplication`** — ports onto existing services: `GetLibraryBooks`/`ReadDatabaseBooks` →
  `PaperbunkrDb` queries; `ScanFolders` → `LibraryFolderScanner`/`BookFolderScanService`;
  `SetCustomBookThumbnail`/`GetComicThumbnail` → `CoverThumbnailService`/`BookCoverThumbnailService`;
  `GetComicPage` → `PageDecodeService`; `AddNewBook`/`RemoveBook` → EF CRUD +
  `LibraryDeletionHelper`; `ReadInternet` → a plain `HttpClient` wrapper; `AskQuestion`/
  `ShowComicInfo` → dialog service calls. `SynchronizeDevices` is dropped — portable device sync is
  already excluded from Paperbunkr entirely (CE feature inventory §15).

- **`IOpenBooksManager` — real behavioral difference, not a rename.** CE is MDI-style: multiple
  books can be open in separate slots at once, hence `Open(cb, inNewSlot, page)`. Paperbunkr's
  `MainViewModel` is single-screen (`CurrentScreen` string switch — `"reader"` replaces whatever was
  showing; there is no second slot). `inNewSlot` has no meaning here and is dropped. Ships as
  `Open(Issue, page)` / `OpenFile(path, page)` / `IsOpen(Issue)` (true only for whatever's currently
  in `CurrentScreen == "reader"`). Worth calling out in plugin-authoring docs — a CE author would
  expect the old three-arg signature.

- **`IBrowser`** — `OpenNextComic`/`OpenPrevComic`/`OpenRandomComic`/`SelectComics` wrap the Library
  screen's existing navigation/selection state directly.

- **`IComicDisplay` — deliberately not a full port.** CE's version is a ~30-member GDI+ interface
  tied to the old rendering pipeline. Scoped down to what the shipped reader canvas actually
  exposes: `CurrentBook` (`Issue?`), `CurrentPageIndex`/`PageCount`,
  `event Action<int> CurrentPageIndexChanged` (already on `ReaderScreenViewModel`),
  `NextPage()`/`PreviousPage()`/`GoToPage(int)`.

- **`IThemePlugin`** — CE's version is WinForms-only (`ToolStripRenderer`, `ITheme`). Replaced with
  `CurrentSkinKey` (from `AppSettings.ActiveSkinKey` via `SkinService`) only. No dark-mode flag —
  Paperbunkr's skin system doesn't track a light/dark axis; skins are arbitrary token sets, not a
  binary.

- **`Localize(resourceKey, elementKey, text)`** — Paperbunkr has no localization pipeline yet.
  Ships as a documented pass-through (`return text`); the interface shape is preserved for
  future-proofing only.

- **`MainWindow`** — not the raw Avalonia `Window`; a thin `IPluginHostWindow` exposing just an
  owner reference for modal dialogs, same insulation instinct as CE's own `IWin32Window` choice.

## 5. Hook wiring map

**Book-operand commands (Library / Editor / Books / NewBooks / CreateBookList)** — CE treats these
as one command family surfaced at different sites:
- `Library` → right-click context menu on selected Issues in the Library grid. Payload = selected
  `Issue[]`.
- `Books` → same context-menu treatment on the separate Books screen (novels/EPUB/PDF). Payload =
  selected `Issue[]`.
- `Editor` → a toolbar slot inside the Issue Properties / Bulk Editing overlay. Payload = the
  Issue(s) being edited.
- `NewBooks` → the "Add New Book" entry point on the Library screen. Returns a new `Issue` draft.
- `CreateBookList` → maps onto the existing Smart Lists sidebar infrastructure — a plugin-backed
  entry appears alongside user smart lists, computed by invoking the command instead of evaluating
  stored rule conditions.

**Path/metadata extension points**
- `ParseComicPath` → fires from `LibraryFolderScanner` during a folder scan/migration, letting a
  plugin override filename→metadata parsing before the built-in parser runs.
- `NetSearch` → registers additional metadata search providers, appearing alongside AniList/
  MangaBaka in the existing Apply-from-Provider search/match UI.

**Lifecycle**
- `Startup` → fires once from `App.axaml.cs` after the DB is ready, before the first screen shows.
- `Shutdown` → fires on app exit, before the process closes.
- `BookOpened` → fires from `ReaderScreenViewModel`'s open sequence once a book is loaded.
- `ReaderResized` → fires from the reader canvas's size-changed event.

**Special**
- `ConfigScript` → not user-menu-triggered; a command can declare a paired config command (via
  `Command.Configure`, same `Key`) that opens a settings dialog from a gear icon next to that
  command's row on the Plugin screen.

**Net-new UI surfaces** (no existing anchor — built as part of this spec)
- `ComicInfoHtml`/`ComicInfoUI` → a new "Plugins" tab in the Detail screen's tab strip (same
  pattern as the existing Related/Activity tabs). `ComicInfoUI` commands return native content
  (plain text/simple structured data) rendered directly into the tab. `ComicInfoHtml` commands
  return an HTML string — since Paperbunkr has no WebView/HTML-rendering surface (retired per
  §12/§13 of onboarding.md, no equivalent exists), that string is *not* rendered as real HTML; it's
  displayed as plain text with tags stripped. The `Html` hook variant exists for CE-shape parity and
  future-proofing, not because it renders HTML today — worth flagging clearly in plugin-authoring
  docs so authors don't expect a live browser surface.
- `QuickOpenHtml`/`QuickOpenUI` → a net-new global command palette (`Ctrl+P`-style overlay) listing
  and invoking commands registered under this hook. Same `Html`-is-plain-text caveat as above
  applies to any `QuickOpenHtml` result rendered in the palette.
- `DrawThumbnailOverlay` → an extra paint pass in the Library grid's per-issue tile template,
  drawing a plugin-supplied overlay on top of the cover thumbnail.

## 6. Plugin screen UI

Replaces the current empty-state-only view (docs/superpowers/specs/2026-08-09-plugin-screen-cleanup-design.md)
with a real list: plugins grouped by folder, each command shown as a row (Name, Hook badge, Enabled
toggle, compile-error indicator with tooltip when `CompileError` is set, gear icon for `Configure`
when a command declares a paired `ConfigScript`). The existing empty state is kept for the
zero-plugins case.

## 7. Real test plugin — "Duplicate Finder"

The current empty-state comment in `PluginScreen.axaml` explicitly notes it replaced a *fake*
hardcoded Duplicate Finder demo. A real, plugin-powered one closes that loop and exercises three
hook categories in one coherent plugin:
- `Startup` — logs plugin activation.
- `Library` — a "Find Duplicates in Selection" context-menu command comparing selected Issues by
  series+number, surfacing matches via `IApplication.AskQuestion`.
- `CreateBookList` — a dynamic "Possible Duplicates" entry in the Smart Lists sidebar, computed by
  scanning the whole library each time it's opened.

## 8. Error handling

- **Discovery-time:** compile errors are caught per-command and stored (surfaced on the Plugin
  screen); never aborts loading other plugins.
- **Invoke-time:** exceptions in `OnInvoke` are caught at the `PluginEngine.Invoke` boundary, logged
  via `DiagnosticsService`, and surfaced as a non-blocking toast (same pattern as the existing Book
  Folders scan toast). A broken plugin command never crashes the host.

## 9. Testing

- Unit tests: `PluginEngine` dispatch/enable-disable filtering; `Command`/`CSharpCommand` lifecycle
  (`Initialize`/`Clone`); `CSharpCommand` compile success/failure paths; `PluginCommandState`
  persistence (only override rows written).
- Integration test: compile-and-invoke a real minimal C# script end-to-end against a fake
  `IPluginEnvironment`, verifying typed payloads arrive correctly.
- The Duplicate Finder plugin as an end-to-end fixture: `PluginEngine.Discover()` against a fixture
  folder, assert commands register under the expected hooks and produce correct results against
  fixture Issues.
- UI automation (existing FlaUI/UIA3 infra): confirm the Plugin screen lists the test plugin's
  commands, toggling enable/disable persists and actually adds/removes the context-menu item on the
  Library screen.
