# ComicRack CE Feature Inventory (parity audit)

*Date: 2026-08-07. Requested after the cover-thumbnails work, when a passing remark ("this is a
genuinely new feature") raised a fair concern: has every ComicRack CE feature actually been
accounted for, or has the port so far only covered what specific design docs happened to name?
Answer, honestly: no prior exhaustive inventory existed. `docs/onboarding.md` was written by working
through specific areas of CE, not by cataloguing the whole app. This document is that catalogue,
built directly from source rather than memory or assumption.*

**Method:** three parallel surveys of the decompiled CE source at `_reference/ComicRackCE`
(a full checkout of the actual WinForms app — `ComicRack/` project — not just the `Engine` layer
Paperbunkr already ported): (1) `MainForm.Designer.cs`/`.cs` for the complete menu/toolbar/context-menu
command surface, (2) every Dialog/View/Control class for its purpose, (3) `ReaderForm.cs` +
`ComicDisplay.cs` + `ComicDisplayControl.cs` for reading-specific features, `PreferencesDialog.cs`
for the settings surface, and `ComicRack.Plugins/` for the plugin hook API. Findings were then
cross-checked against `docs/onboarding.md` §1-16 (which already resolves several of these
explicitly) and Paperbunkr's actual current implementation state.

**Status tags:** ✅ built in Paperbunkr · 📋 already planned in onboarding.md, not yet built ·
🚫 already explicitly excluded (onboarding.md §14/§15) · 🔨 decided: to build ·
⏸️ deferred (repurpose/scope still undecided) · ⛔ dropped for now.

**Triage complete (2026-08-07):** every single item in this document has an explicit decision —
none left as an assumed/blanket "probably fine." Two clean drops (export, self-updater), one
genuinely deferred item (News reader — repurpose idea, not scoped yet), everything else decided:
build. That's a large confirmed backlog now; the work ahead is sequencing it into specs, not
further triage.

---

## Headline findings (read this part first)

1. **Device/portable-reader sync (`Edit > Devices`, `File > Synchronize Devices`,
   wireless discovery in Preferences > Advanced) is already decided: excluded.**
   onboarding.md §15's resolved-open-items list states the CE `Config.xml`'s WiFi-sync settings
   are "out of scope entirely (feature doesn't exist in Paperbunkr)." Nothing to decide here — just
   confirming the audit didn't surface a reason to revisit it.

2. **The entire reader-polish surface (zoom/fit modes, page-spread layout, rotation, RTL
   navigation, magnifier, page transitions, on-screen overlays, image brightness/contrast/gamma
   adjustment, touch gestures, bookmarks) is already in scope** — onboarding.md §11 states
   "CRReaderOverhaul → retired as a plugin; its feature list is now the reader canvas spec (§8),
   built natively." That's exactly the feature set the reader survey (below) itemized in detail.
   **This isn't a new gap — it's the first real itemization of what "§8, Beta" actually has to
   contain**, and it's considerably larger than §8's current text ("continuous/webtoon rendering")
   suggests on its own. Worth expanding §8 with this list before Beta reader work starts.

3. **Preferences/Settings has zero implementation.** §12 names it as one of the five wireframed
   screens, so it's not an unplanned gap — but right now there is no settings screen, no
   `PreferencesScreenViewModel`, nothing. CE's Preferences dialog is ~4700 lines across five tabs
   (Reader, Behavior, Libraries, Scripts, Advanced) covering a genuinely large settings surface
   (itemized below). This is the single largest "0% built, already planned" item found.

4. **Remote/server library sharing** — a substantial client/server subsystem, not named anywhere
   in onboarding.md §1-16. **Decided: build it** (see triage section below) — needs its own design
   spec before implementation given the size.

5. **Paperbunkr's single-screen rail-nav model is a deliberate departure from CE's
   multi-window/tabbed MDI model** (`New Tab`, `Previous/Next Tab`, "Reader in own Window",
   multiple simultaneously-open book tabs) — not a gap. §12 already points at Mihon/Komikku-style
   patterns (collapsible categories, snackbar undo, per-series overrides) as the actual design
   target, which is a single-list mobile-esque UX, not CE's desktop MDI approach. **Confirmed** in
   the triage pass — not revisiting this.

6. **`Help > News...` (built-in RSS feed reader) and the GitHub-releases-API self-updater** —
   the clearest legacy-cruft candidates in the audit. **Decided:** self-updater dropped for now
   (revisit once Paperbunkr has its own release pipeline); News reader deferred, with a live idea
   to repurpose the mechanism for something Paperbunkr-relevant rather than building or discarding
   it outright — see triage section.

---

## Decisions from triage pass (2026-08-07)

| Item | Decision | Notes |
|---|---|---|
| Remote/server library sharing | 🔨 **Build it** | Needs its own brainstorm → design spec before implementation — this is a big enough feature to warrant one, not something to improvise inline. Not yet scheduled. |
| `Help > News` RSS reader | ⏸️ **Deferred** | Not simply porting or dropping — repurposing the mechanism (feed subscription + display UI) for something Paperbunkr-relevant is on the table. Needs a real brainstorm on what that would actually be before any code gets written. |
| GitHub self-updater | ⛔ **Dropped for now** | Revisit once Paperbunkr has an actual release/distribution pipeline of its own. |
| CE's tabbed/multi-window reading model | Confirmed non-goal | Single-screen rail-nav stands, matching onboarding.md §12's Mihon/Komikku direction. |
| Fileless book entries | 🔨 **Build it** | |
| File metadata write-back (edits saved into ComicInfo.xml/tags on disk) | 🔨 **Build it** | Real risk surface (mutates user's files) — worth extra care in whatever design spec covers metadata editing. |
| Workspaces (named display-setting presets) | 🔨 **Build it** | Full preset system, not just "remember last used." |
| Minimize-to-tray | 🔨 **Build it** | |
| Crash reporter dialog | 🔨 **Build it** | |
| Backup manager | 🔨 **Build it** | Full in-app system (scheduled, on-startup/shutdown, retention, restore UI) — not just "document the SQLite file location." |
| Export comics to another format/location | ⛔ **Dropped** | Format conversion is a separate concern from reading/organizing; dedicated tools already cover it. |
| Folder-watch continuous scanning | 🔨 **Build it** | Independent of one-time CE migration — Paperbunkr becomes a first-class library manager, not just an import target. |

**Full triage pass complete (2026-08-07)** — every remaining item was walked through explicitly
rather than left as a blanket "confirmed in scope" assumption: full metadata editor, bulk edit,
copy/paste fields, templated field editor, rating UI, quick rating+review, undo/redo, per-page
type/rotation, named bookmarks (all Section A); touch gestures, remappable keyboard shortcuts,
auto-scrolling (Section B's non-CRReaderOverhaul additions); filesystem folder browsing, browse
history, list layouts, pluggable sort/group, drag-drop, recent files/Quick Open, reveal-in-Explorer
(Section C remainder); open-with associations (Section H remainder) — **all decided: build.**
Nothing in this document was left un-triaged.

---

## Detailed inventory by area

### A. Comic metadata editing
| Feature | Status |
|---|---|
| Single-book properties editor (all ComicInfo fields, cover, pages, scripts tabs) | ✅ shipped 2026-08-07, converted to a borderless overlay 2026-08-23 (docs/superpowers/specs/2026-08-23-issue-editor-borderless-overlay-design.md) |
| Bulk multi-book edit (mixed-value tracking) | ✅ shipped 2026-08-07, same overlay conversion 2026-08-23 |
| Copy/paste metadata fields between books | ✅ shipped 2026-08-23 — Copy/Paste buttons in the single-book editor header |
| Templated/token text field editor (insert `{property}`) | ✅ shipped 2026-08-23 — both editors; single-book resolves immediately, bulk expands per issue at Save |
| Rating (numeric) | ✅ `Issue.Rating` field + Favorites smart list + full-editor star UI shipped 2026-08-07 |
| Quick Rating + free-text Review in one popup | ✅ shipped 2026-08-23 — "Quick Rate…" on Library and Detail's issue tiles |
| Undo/Redo for metadata edits | ✅ shipped 2026-08-23 — multi-level history stack, rail nav Undo/Redo buttons |
| Per-page type tagging (cover/story/ad/deleted) | ✅ shipped 2026-08-23 — new `IssuePage` entity, Reader thumbnail right-click |
| Per-page persisted rotation override | ✅ shipped 2026-08-23 — same `IssuePage` entity; paged mode only, not continuous scroll |
| Named bookmarks (set/remove/prev/next) — distinct from `LastPageRead` | ✅ shipped 2026-08-23 — inline rename on existing bookmark flyout rows |

### B. Reader (§8, Beta) — the itemized "CRReaderOverhaul" checklist
**Re-verified against live code 2026-08-23 — this table had drifted badly stale (9 of 16 rows below
were marked 📋/🔨 despite already being shipped).** Verification method: direct code search, not
memory files (see [[project_paperbunkr_metadata_editing_extras]]-adjacent session notes for why
memory alone isn't trustworthy here).

| Feature | Status |
|---|---|
| Paged left-to-right reading, real page decode | ✅ shipped (Alpha) |
| Fit modes: Original / Fit All / Fit Width (+ adaptive) / Fit Height / Best Fit | ✅ shipped 2026-08-10, minus `FitWidthAdaptive` — named deviation, see docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-design.md §1 |
| Zoom: in/out, toggle, presets (100/125/150/200/400%), custom | ✅ shipped 2026-08-10 (custom zoom/pan gestures were already Alpha; presets added) |
| Page layout: single / double-page spread / adaptive spread detection | ✅ shipped — `PageLayoutMode`/`ToggleDoublePageMode`/`SpreadLayoutMath` (docs/superpowers/specs/2026-08-15-reader-double-page-spread-design.md); CE's 3 modes deliberately collapsed to 2 (`Double` behaves like CE's `DoubleAdaptive`); a per-page manual Near/Far override stays a real, separate gap (no `PagesView`-style screen exists) |
| Reading direction: RTL (manga), with flip-parts vs flip-pages sub-modes | ✅ shipped — `ReaderScreenViewModel.ToggleReadingMode`/RTL page-turn flip, per docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md |
| Rotation: relative/absolute + autorotate landscape pages | ✅ shipped 2026-08-10 — session-only (not persisted per-book), matching CE's own precedent. Per-page *persisted* rotation override also shipped separately 2026-08-23, see §A |
| Magnifier/loupe overlay tool | 📋 confirmed still not started — CE's `MagnifierStyle`/`IComicDisplayConfig` exist only as dormant ported code in `Paperbunkr.Engine`, zero references anywhere in `Paperbunkr.App` |
| Page transition animations (fade/slide/paging) + zoom-in/out-on-page-change | ✅ shipped — `PageTransitionStyle`/`PageTransitionMath`/`ReaderPageVisualHandler` (docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md) |
| Fullscreen + minimal-UI chrome-reduction mode | ✅ shipped — `ReaderScreenViewModel.IsFullscreen`/`ToggleFullscreen`/`ShowFullscreenOverlays` |
| On-screen overlays: nav scrubber bar, page/status text, part-info, clock/battery | ⚠️ partial — nav scrubber (thumbnail rail) and page/status text both real and shipped; "part-info" doesn't apply, confirmed no CE-style "Part" concept exists in Paperbunkr at all; clock/battery genuinely not built |
| Image adjustment: brightness/contrast/saturation/gamma/sharpen, live (not baked into files) | ⚠️ partial — brightness/contrast/saturation/gamma shipped and live (`Services/ImageAdjustmentMath.cs`, ported from CE's `ImageProcessing.cs`); sharpen and AutoContrast/WhitePoint explicitly skipped, real remaining gap |
| Background: solid/texture/paper-texture, page margins | ⚠️ partial — solid-color background + page margins shipped (`AppSettings.ImageBackgroundMode`/`PageMarginEnabled` etc., global-only by design, no per-issue override); paper/texture background mode genuinely not built |
| Continuous/webtoon vertical scroll | ✅ shipped — **not** a CE feature at all, confirmed genuinely new work, not parity (docs/superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md) |
| Split-page "part" navigation for zoomed pages, type-to-jump-to-page, scroll page-turn throttling | ⚠️ partial — page-turn throttling shipped (`ReaderScreenViewModel`'s rapid-paging guard); split-page part navigation doesn't apply (no Part concept, confirmed absent); type-to-jump-to-page genuinely not built (only click-to-jump via the thumbnail rail) |
| Touch gestures: 9-zone tap mapping + double-tap, flick, media keys | ✅ shipped (minus media keys) — 3×3 zone grid, double-tap, flick, pinch all real in `PageCanvas.cs` (docs/superpowers/specs/2026-08-09-reader-gestures-and-grid-navigation-design.md); media keys were an explicit scope cut in that same spec (cross-platform Avalonia support too inconsistent), not an oversight |
| Fully remappable keyboard shortcuts, import/export layout | ✅ shipped — `KeyboardCommandRegistry`/`KeyBindingService` cover ~24 Reader commands (docs/superpowers/specs/2026-08-16-remappable-reader-shortcuts-design.md); import/export of the layout specifically not independently re-verified |
| Auto-scrolling / hands-free reading mode | ✅ shipped (docs/superpowers/specs/2026-08-16-reader-auto-scroll-design.md) — deliberate deviation from CE's own differently-behaving toggle, documented as such |

### C. Library browsing
**Re-verified against live code 2026-08-23 — 7 of 12 rows below were marked 🔨 despite already
being shipped.**

| Feature | Status |
|---|---|
| Grid view with real cover art | ✅ shipped |
| List/detail row view | ✅ shipped |
| Filesystem folder browsing mode (not just library-backed) | ❌ **won't do** — dropped 2026-09-03 by decision. Paperbunkr already covers "comics not yet in the library" through drag-and-drop import, live folder-watch, and fileless entries; a second persistent browse surface wasn't worth the weight. |
| Browse history (back/forward through views) | ✅ shipped — `LibraryBrowseState`/`_browseHistory` (`CursorList<T>`) (docs/superpowers/specs/2026-08-19-library-browse-history-design.md); search query tracked as a history step, sort/group/display deliberately excluded |
| Saved "Workspaces" (display-setting presets) | ✅ shipped 2026-09-03 — `Workspace` entity + `WorkspaceService`, a toolbar switcher on both the Library and Books screens (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md). **Deviations from CE:** per-screen lists, not one global list; "Views setup" group only (no window-layout / reader-display capture); ships 3+3 read-only starter workspaces (CE ships none). One-shot apply, reuse-a-name-to-overwrite (CE's own Save dialog model). |
| Saved "List Layouts" (grid column/sort/group presets) | ✅ shipped — persists sort/group/view mode/grid density/overlay toggles/filters/sidebar selection (docs/superpowers/specs/2026-08-17-library-saved-list-layouts-design.md). This is persistence of the existing session-only UI, not named/multiple presets — that's "Workspaces" above, still open |
| Pluggable sort/group strategies (by rating, community rating, read %, custom/virtual tags) | ⚠️ partial — `IssueListSortField` covers 60+ CE-parity fields including Rating/Community Rating/Read % (docs/superpowers/specs/2026-08-18-issue-list-pluggable-sort-group-design.md); Virtual Tags are **not** a sort/group axis despite being named in this row — real, specific remaining gap |
| Drag-and-drop (files/folders/reading-list files onto the app) | 📋 confirmed still not started |
| Recent/MRU file list, Quick Open (recents+favorites overlay) | ✅ shipped 2026-09-03 — `Ctrl+P` fuzzy command palette (`Services/QuickOpenService.cs` + `QuickOpenMatcher.cs`, `ViewModels/QuickOpenViewModel.cs`), docs/superpowers/specs/2026-09-03-quick-open-command-palette-design.md. Subsequence-matches series / issues / books / lists / collections / events / screens + action verbs; pre-type list is the recently-opened comics + books. Deliberately not CE's recency cover-wall (Home covers that) or its `File ▸ Open Recent` menu. |
| Reveal-in-Explorer / copy-file-path | ✅ shipped — `Services/RevealInExplorerHelper.cs` (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §1) |
| Folder-watch continuous scanning (independent of one-time CE migration) | ✅ shipped — `Services/LiveFolderWatchService.cs` |
| File metadata write-back (save edits into ComicInfo.xml/tags) | ✅ shipped — `Services/ComicInfoWriteBackService.cs`, wired into both the single-book and bulk editors (currently scoped to Genre/Tags) |
| Fileless book entries (catalog a physical book with no file) | ✅ shipped — `LibraryScreenViewModel.CreatePlaceholderIssue`/`ReadingListMatcher.ResolveOrCreatePlaceholder` (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §2-3) |

### D. Smart Lists (cross-reference only, not a gap)
Already shipped in Paperbunkr with CE field parity. CE's dual rule-builder/raw-query editing
modes and recursive AND/OR matcher-group UI are worth a UX glance if the Smart Lists editor gets
revisited, but this is not missing functionality.

### E. Preferences / Settings
**Stale header — this whole section predates a lot of shipped work.** Appearance, Behavior,
Libraries, Reader, and Advanced tabs are all real today (`PreferencesScreenViewModel`); Scripts was
confirmed a zero-real-surface dead end via CE-source triage and deliberately has no tab. The table
below documents CE's *original* notable contents per area for reference, not a current gap list —
see each tab's own design spec for what actually shipped and what was deliberately left out.
| Tab | Notable contents |
|---|---|
| Reader | display adjustment sliders, hardware-accel toggles, mouse/scroll behavior, overlay visibility + position, keyboard shortcut editor — ✅ keyboard shortcuts + mouse/scroll speed + display shipped (docs/superpowers/specs/2026-08-10-preferences-reader-tab-design.md); magnifier/overlay/hardware-accel toggles deliberately skipped, gate unbuilt capability |
| Behavior | ~10 reflection-driven categories: startup behavior, book-opening behavior, reading behavior, RTL, browser display, app chrome, caching, scripting, network, import/export |
| Libraries | watched folders, scan behavior, sharing, server settings, **Virtual Tags** (see below) |
| Scripts | script/plugin package management |
| Advanced | wireless device discovery (🚫 excluded), Explorer file-association integration, backup manager, memory/cache limits, database backup, file write-back toggle, language pack |

**Virtual Tags** — ✅ **shipped, re-verified 2026-08-23 (this callout was stale).** Full CRUD UI
lives in `PreferencesScreen.axaml` (~lines 569-619): a list, Add/Delete, editable Name/CaptionFormat/
Enabled fields, and a live `VirtualTagPreview`. Backed by a real `VirtualTagDefinition` entity and
`VirtualTagTemplateEvaluator` (`{FieldName}` token templates). Not a "good candidate for an early
win" — there's no remaining work here.

### F. Remote/server & device sync
**Re-verified 2026-08-23**: CE's own `NetworkManager`/`ComicLibraryClient`/`ComicLibraryServer`/
`RemoteComicBookProvider` classes are already ported into `Paperbunkr.Engine` (same "ported early,
dormant until a design wires it up" pattern as other Engine-only code in this project) — but **zero
references exist anywhere in `Paperbunkr.App`**. The App-layer feature is still entirely unbuilt;
only the Engine has a head start.

| Feature | Status |
|---|---|
| Client: connect to another ComicRack instance's shared library over the network | 🔨 decided: build — needs its own design spec first; `Paperbunkr.Engine.NetworkManager`/`ComicLibraryClient` exist as dormant ported CE code, not wired to anything |
| Server: host this library for other instances to browse (password-protected, per-list sharing) | 🔨 decided: build — needs its own design spec first; `Paperbunkr.Engine.ComicLibraryServer` exists dormant, same as above |
| Background job/task monitor for server activity | 🔨 decided: build — needs its own design spec first; no code anywhere yet, and depends on the server feature above |
| Portable device sync (e-readers) | 🚫 already excluded (§15) |

### G. Plugin API (§10, Beta — already planned, now much better itemized)
Confirms the already-ported `IPluginEnvironment` shape is sound, and reveals the full hook-point
surface CE actually exposes: `CreateBookList`, `ParseComicPath`, `Library`, `Editor`, `Books`,
`NewBooks`, `BookOpened`, `ReaderResized`, `NetSearch`, `Startup`, `Shutdown`, `ConfigScript`,
`ComicInfoHtml`/`ComicInfoUI`, `QuickOpenHtml`/`QuickOpenUI`, `DrawThumbnailOverlay`. No action
needed now — this is reference detail for when §10's "functional against a real test plugin" Beta
work actually starts.

### H. App chrome / infrastructure
**Re-verified against live code 2026-08-23 — 3 of 8 rows below were marked 🔨 despite already
being shipped.**

| Feature | Status |
|---|---|
| Crash reporter dialog | ✅ shipped — `Views/CrashReportWindow.axaml`, wired into `Services/DiagnosticsService.cs`'s global exception handler, `CrashOutcome` maps the 4 CE-parity user choices |
| Backup manager (scheduled, on-startup/shutdown, retention, restore UI) | ⚠️ partial — `Services/BackupService.cs` has real manual backup/restore/retention + a restore-list UI in Preferences; scheduled on-startup/on-shutdown triggers are explicitly deferred in the code's own doc comment, manual-only today |
| Export comics to another format/location | ⛔ decided: dropped |
| External "open with" app associations | ✅ shipped — `Services/FileAssociationService.cs`/`WindowsShellFileAssociation.cs`, real UI in Preferences |
| Minimize-to-tray | ✅ shipped — `Services/TrayIconService.cs`, wired end-to-end in `MainWindow.axaml.cs` (minimize/close interception, first-time notice), not just an unused service |
| Built-in RSS "News Channels" reader | ⏸️ deferred — repurpose idea live, needs its own brainstorm |
| GitHub-releases-API self-updater (fork-specific) | ⛔ dropped for now — revisit with a real release pipeline |
| Multi-tab/multi-window book-open model | Confirmed architectural non-goal (see headline #5), not a gap |

---

## Status

**This section is a historical snapshot from the document's original 2026-08-07 authoring and was
never updated as work shipped — do not treat the sequencing plan below as current.** Sections A, B,
C, E (Virtual Tags), F, and H were all re-verified against live code on 2026-08-23; see each
section's own "re-verified" note for what actually shipped since this plan was written.

Every item in this document has an explicit decision — only the News-reader repurpose is
genuinely open-ended, and export/self-updater are the two clean drops. As of 2026-08-23, what's
**genuinely still unbuilt** across the whole document (confirmed by direct code search, not by this
stale sequencing plan):

- **Reader (§B):** magnifier/loupe, sharpen/AutoContrast/WhitePoint image adjustment, paper/texture
  background, clock/battery overlay, type-to-jump-to-page, a per-page manual Near/Far double-page
  override.
- **Library (§C):** drag-and-drop import (design done), Recent/MRU + Quick Open command palette
  (design done), Virtual Tags as a sort/group axis. (Saved "Workspaces" shipped 2026-09-03;
  filesystem folder browsing mode dropped by decision.)
- **Remote/server (§F):** the entire App-layer feature — Engine-layer CE classes are ported and
  dormant, but nothing in `Paperbunkr.App` calls them yet. Needs its own design spec before any
  App-side code.
- **App chrome (§H):** Backup manager's scheduled on-startup/on-shutdown triggers (manual
  backup/restore already works).
- **News-reader repurposing:** still genuinely open-ended, needs its own brainstorm.

Everything else this document originally listed as "🔨 decided: build" in sections A, B, C, E, F,
and H has since shipped — re-check the section itself rather than assuming from this summary alone.
