# Plugin API v2 — remaining hook wiring + net-new UI surfaces — Plan

2026-09-05

Closes the rest of docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §5's hook wiring map.
4 of 17 hooks already had a live trigger claimed (Startup, Shutdown, BookOpened, Library) — audit
found `BookOpened` was actually a dead wire (`ReaderScreenViewModel.IssueOpened` fires, nothing
subscribes), so that's really 3 done + 1 to fix, plus `plugin-api-gap-closure`'s merge closed the
three `IApplication`/`IBrowser` automation gaps and added Python scripting separately. This plan
covers the remaining 8 hooks (Editor/Books/NewBooks/CreateBookList/ParseComicPath/NetSearch/
ReaderResized/ConfigScript) and the 3 net-new UI surfaces (ComicInfoHtml/UI, QuickOpenHtml/UI,
DrawThumbnailOverlay), plus the BookOpened fix.

CE source cross-check (`_reference/ComicRackCE`) done for each item below, per the standing rule.

## Already in place (no work needed)

- Every hook's typed `*HookGlobals` payload and `PluginGlobalsTypeMap` entry already exist
  (`src/Paperbunkr.Plugins/Hooks/`).
- `PluginEngine.GetCommands(hook)` already returns enabled, non-broken commands for a hook.
- `Command.Configure` pairing is already fully wired at discovery time (`PluginEngine.Discover`) —
  ConfigScript only needs a UI trigger (gear icon), not new pairing logic.
- Paperbunkr already has its own real Ctrl+P command palette (`QuickOpenService`/
  `QuickOpenViewModel`/`QuickOpenOverlay`) built independently of the plugin API — extending it is
  far less work than CE's own literal QuickOpen (a "recently opened books" grid with attached info
  panels, verified in `_reference/.../Views/QuickOpenView.cs` — doesn't map cleanly onto
  Paperbunkr's UI anyway, so the v2 spec's "Ctrl+P-style overlay" adaptation is the right call, now
  doubly justified since that overlay already exists for an unrelated reason).

## 1. Fix: BookOpened dead wire

`ReaderScreenViewModel.IssueOpened` (fires from `LoadIssue`) has no subscriber. Add
`PluginHostService.RunBookOpenedHookAsync(Issue)`; `Reader.AttachHost(pluginHost)` pattern (same
shape as `Library`/`Plugin`) subscribes `Reader.IssueOpened += async issue => await
_host.RunBookOpenedHookAsync(issue)` in `App.axaml.cs`.

## 2. ReaderResized

Reader canvas's `SizeChanged`. New `RunReaderResizedHookAsync(int width, int height)`, fired
alongside the existing size-changed handling. Fire-and-forget (no UI feedback expected, matches
CE's own silent per-resize hook).

## 3. Editor / 4. Books — same "run enabled commands" family as Library

CE (`MainForm.cs` InitializeToolbars) surfaces Library/Editor/Books/NewBooks all as one family via
`ScriptUtility.CreateToolItems`: one menu entry **per enabled command**, labelled by the command's
own `Name` — not Paperbunkr's existing Library-hook wiring, which hardcodes a single "Find
Duplicates" label (fine when only one plugin exists, but not the general shape). New hooks
(Editor/Books/NewBooks) get the more correct per-command-name treatment; Library's already-shipped,
tested wiring is left untouched (out of scope, no regression risk taken for its own sake).

- **Editor** — Issue Properties overlay (`IssuePropertiesScreenViewModel`/`.axaml`) and Bulk Editing
  overlay (`BulkIssuePropertiesScreenViewModel`/`.axaml`) toolbar. Payload = the issue(s) being
  edited (single issue for Issue Properties, the full set for Bulk Editing).
- **Books** — `BooksContextMenuBuilder` (novels/EPUB/PDF screen), mirroring
  `LibraryContextMenuBuilder`'s existing plugin-entry pattern but per-command. Payload = selected
  `Issue[]` (Books screen's `Book` entities map onto the same `Issue` table).

## 5. NewBooks

CE inserts one File-menu item per enabled `NewBooks` command, right after "New Comic" — a peer
entry point, not a takeover of the built-in add flow. Paperbunkr equivalent: Library's "Add issue to
library" overlay (`LibraryScreen.axaml`, `IsAddIssueOpen`) gets an additional "Add via <plugin
command name>" button per enabled command, visible only when any exist. Invokes the command with
`NewBooksHookGlobals`; a returned `Issue` closes the overlay and opens Issue Properties for it
(reusing the same `GoNewIssuePropertiesForPlaceholder` flow the manual "Add" button already uses).

## 6. CreateBookList

Genuinely different from the others — CE computes an ad-hoc book list from a script, not a stored
query. Doesn't fit `SmartList`'s DB-row/`SmartListQueryBuilder` model at all. New
`SmartScreenViewModel.PluginLists` sidebar section (parallel to the existing `MaintenanceLists`),
one `SmartListSummary`-shaped row per enabled `CreateBookList` command (negative synthetic ids to
never collide with real `SmartList.Id`s). Selecting one runs the command fresh (spec: "computed by
scanning the whole library each time it's opened") via `PluginHostService.RunCommandAsync` and
displays its returned `Issue[]` directly, bypassing `SmartListQueryBuilder` entirely for that
selection.

## 7. ParseComicPath

`LibraryFolderScanner`'s filename→metadata parse step. Given an enabled `ParseComicPath` command,
invoke it with the raw path before the built-in parser runs; if the script returns a non-null
result, it wins over the built-in guess (matches spec's "letting a plugin override... before the
built-in parser runs"). No commands exist today that implement this, so behavior is unchanged until
one does — verified via a fixture plugin test, not a live sample.

## 8. NetSearch

`IMetadataProvider`'s existing registration point (alongside AniList/MangaBaka) gets a thin adapter
wrapping enabled `NetSearch` commands as additional providers in the Apply-from-Provider UI. Query
text flows in via `NetSearchHookGlobals.Query`; the script returns provider-shaped match results.

## 9. ConfigScript

Plugin screen's command row (`PluginCommandRowViewModel`/its row template) gets a gear icon,
visible when `Command.Configure is not null`, invoking that paired command via
`PluginHostService.RunCommandAsync` with `ConfigScriptHookGlobals`.

## 10. ComicInfoHtml/UI

CE anchors this as a comic-info sidebar panel on the Library explorer view (verified in
`_reference/.../Views/MainView.cs`: `ScriptUtility.CreateComicInfoPages()` → `ISidebar.AddInfo`),
not literally a "Detail screen tab" — but Paperbunkr has no such sidebar and does have a
single-issue-focused Detail screen (`Tabs.SelectedIssueIds`/focused-issue concept from the
book-centric redesign), so the already-approved v2 spec's adaptation (a "Plugins" tab in
`DetailTabs.axaml`, same pattern as the existing Related/Activity tabs) is the right shape for this
codebase. Visible only when the focused issue has any enabled `ComicInfoHtml`/`ComicInfoUI`
commands; `Html` variant results are shown as plain text (tags stripped) per spec §5's explicit
no-WebView caveat — never rendered as real HTML.

## 11. QuickOpenHtml/UI

Extend `QuickOpenService.BuildIndex()` with a `QuickOpenKind.PluginCommand` entry per enabled
`QuickOpenHtml`/`QuickOpenUI` command; `MainViewModel.ActivateQuickOpenEntry` invokes it with
`QuickOpenHookGlobals { Query = <the palette's current search text> }` when activated. Same
plain-text-for-Html caveat as ComicInfo.

## 12. DrawThumbnailOverlay

CE invokes this live, per-paint, with a raw GDI+ `Graphics` callback
(`CoverViewItem.DrawCustomThumbnailOverlay`) — no Avalonia equivalent exists, and firing a Roslyn
script synchronously on every tile repaint in a virtualized grid would be a real perf hazard (this
codebase already fought hard to get thumbnail decode off the UI thread, see cover-thumbnail
identity/virtualization work). Paperbunkr adaptation, consistent with every other icon method's
existing `byte[]?` PNG convention (`GetComicPage`/icon methods): the script returns overlay PNG
bytes (with alpha) once per issue, computed off the UI thread and cached the same way
`AsyncCoverImage` already caches decoded covers, then composited on top of the cover thumbnail in
the Library tile template. Null return = no overlay, same null-means-nothing convention as the icon
methods.

## Testing

Unit tests per hook: `PluginHostService`'s new `RunXHookAsync` methods (dispatch happens, toast on
failure); one fixture-plugin end-to-end test per hook family (mirrors `DuplicateFinderPluginTests`'
existing style) confirming a script registered under the hook actually gets invoked with the right
payload from the real UI-adjacent call site (ViewModel command), not just `PluginEngine` directly.
`QuickOpenService`/`SmartScreenViewModel` get unit coverage for the new plugin-backed entries
(index/sidebar row shape, correct command invoked on selection). No new fixture plugin scripts are
required to *implement* the hooks (the existing `FakePluginEnvironment`/ad-hoc `Command` test
doubles cover invocation), but ParseComicPath/NetSearch/ConfigScript get one minimal end-to-end
fixture each since "no live sample plugin exercises this hook" was explicitly why they were
flagged as gaps in the first place — a wiring change with zero test proving the wire actually
carries a signal isn't done.

On-screen verification: out of scope for this pass (same standing caveat as every other backlog
item merged without a GUI session) — flagged in the roadmap doc's on-screen-verification list.
