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
| Single-book properties editor (all ComicInfo fields, cover, pages, scripts tabs) | 🔨 decided: build — Detail screen only edits ContentType today |
| Bulk multi-book edit (mixed-value tracking) | 🔨 decided: build |
| Copy/paste metadata fields between books | 🔨 decided: build |
| Templated/token text field editor (insert `{property}`) | 🔨 decided: build |
| Rating (numeric) | ✅ `Issue.Rating` field + Favorites smart list exist; 🔨 decided: build the UI |
| Quick Rating + free-text Review in one popup | 🔨 decided: build (Review text isn't even a schema field yet) |
| Undo/Redo for metadata edits | 🔨 decided: build |
| Per-page type tagging (cover/story/ad/deleted) | 🔨 decided: build |
| Per-page persisted rotation override | 🔨 decided: build |
| Named bookmarks (set/remove/prev/next) — distinct from `LastPageRead` | 🔨 decided: build |

### B. Reader (§8, Beta) — the itemized "CRReaderOverhaul" checklist
| Feature | Status |
|---|---|
| Paged left-to-right reading, real page decode | ✅ shipped (Alpha) |
| Fit modes: Original / Fit All / Fit Width (+ adaptive) / Fit Height / Best Fit | ✅ shipped 2026-08-10, minus `FitWidthAdaptive` — named deviation, see docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-design.md §1 |
| Zoom: in/out, toggle, presets (100/125/150/200/400%), custom | ✅ shipped 2026-08-10 (custom zoom/pan gestures were already Alpha; presets added) |
| Page layout: single / double-page spread / adaptive spread detection | 📋 |
| Reading direction: RTL (manga), with flip-parts vs flip-pages sub-modes | ✅ shipped — `ReaderScreenViewModel.ToggleReadingMode`/RTL page-turn flip, per docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md |
| Rotation: relative/absolute + autorotate landscape pages | ✅ shipped 2026-08-10 — session-only (not persisted per-book), matching CE's own precedent |
| Magnifier/loupe overlay tool | 📋 |
| Page transition animations (fade/slide/paging) + zoom-in/out-on-page-change | 📋 |
| Fullscreen + minimal-UI chrome-reduction mode | 📋 |
| On-screen overlays: nav scrubber bar, page/status text, part-info, clock/battery | 📋 |
| Image adjustment: brightness/contrast/saturation/gamma/sharpen, live (not baked into files) | 📋 |
| Background: solid/texture/paper-texture, page margins | 📋 |
| Continuous/webtoon vertical scroll | 📋 — **not** a CE feature at all; confirmed genuinely new work, not parity |
| Split-page "part" navigation for zoomed pages, type-to-jump-to-page, scroll page-turn throttling | 📋 |
| Touch gestures: 9-zone tap mapping + double-tap, flick, media keys | 🔨 decided: build (not previously named even implicitly) |
| Fully remappable keyboard shortcuts, import/export layout | 🔨 decided: build |
| Auto-scrolling / hands-free reading mode | 🔨 decided: build |

### C. Library browsing
| Feature | Status |
|---|---|
| Grid view with real cover art | ✅ shipped tonight |
| List/detail row view | ✅ shipped tonight |
| Filesystem folder browsing mode (not just library-backed) | 🔨 decided: build |
| Browse history (back/forward through views) | 🔨 decided: build |
| Saved "Workspaces" (display-setting presets) | 🔨 decided: build (full preset system) |
| Saved "List Layouts" (grid column/sort/group presets) | 🔨 decided: build — directly relevant: `LibraryScreen.axaml`'s "Display ▾" dropdown already has decorative grid-density/sort UI stubbed in for this exact concept |
| Pluggable sort/group strategies (by rating, community rating, read %, custom/virtual tags) | 🔨 decided: build — current sort is fixed by `SortName` |
| Drag-and-drop (files/folders/reading-list files onto the app) | 🔨 decided: build |
| Recent/MRU file list, Quick Open (recents+favorites overlay) | 🔨 decided: build |
| Reveal-in-Explorer / copy-file-path | 🔨 decided: build (small) |
| Folder-watch continuous scanning (independent of one-time CE migration) | 🔨 decided: build |
| File metadata write-back (save edits into ComicInfo.xml/tags) | 🔨 decided: build — needs careful design (mutates user's files) |
| Fileless book entries (catalog a physical book with no file) | 🔨 decided: build |

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

**Virtual Tags** deserves its own callout: user-defined computed/templated metadata fields
(name + prefix/caption/suffix rich-text template with an insert-field picker, live preview). Not
mentioned anywhere in onboarding.md. Small, self-contained, and genuinely useful — a good
candidate to scope independently of the full Preferences screen if you want an early win here.

### F. Remote/server & device sync
| Feature | Status |
|---|---|
| Client: connect to another ComicRack instance's shared library over the network | 🔨 decided: build — needs its own design spec first |
| Server: host this library for other instances to browse (password-protected, per-list sharing) | 🔨 decided: build — needs its own design spec first |
| Background job/task monitor for server activity | 🔨 decided: build — needs its own design spec first |
| Portable device sync (e-readers) | 🚫 already excluded (§15) |

### G. Plugin API (§10, Beta — already planned, now much better itemized)
Confirms the already-ported `IPluginEnvironment` shape is sound, and reveals the full hook-point
surface CE actually exposes: `CreateBookList`, `ParseComicPath`, `Library`, `Editor`, `Books`,
`NewBooks`, `BookOpened`, `ReaderResized`, `NetSearch`, `Startup`, `Shutdown`, `ConfigScript`,
`ComicInfoHtml`/`ComicInfoUI`, `QuickOpenHtml`/`QuickOpenUI`, `DrawThumbnailOverlay`. No action
needed now — this is reference detail for when §10's "functional against a real test plugin" Beta
work actually starts.

### H. App chrome / infrastructure
| Feature | Status |
|---|---|
| Crash reporter dialog | 🔨 decided: build |
| Backup manager (scheduled, on-startup/shutdown, retention, restore UI) | 🔨 decided: build — full in-app system, not just guidance |
| Export comics to another format/location | ⛔ decided: dropped |
| External "open with" app associations | 🔨 decided: build |
| Minimize-to-tray | 🔨 decided: build |
| Built-in RSS "News Channels" reader | ⏸️ deferred — repurpose idea live, needs its own brainstorm |
| GitHub-releases-API self-updater (fork-specific) | ⛔ dropped for now — revisit with a real release pipeline |
| Multi-tab/multi-window book-open model | Confirmed architectural non-goal (see headline #5), not a gap |

---

## Status

Every item in this document has an explicit decision — only the News-reader repurpose is
genuinely open-ended, and export/self-updater are the two clean drops. Everything else is decided:
build, which is a large confirmed backlog. Sequencing plan so far:

1. **In progress:** Preferences screen, sub-project 1 (settings infrastructure + skin/theme
   system) — see docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md.
2. **Queued, each its own future spec:** Preferences' remaining tabs (Reader/Behavior/Libraries/
   Scripts/Advanced — Workspaces, backup manager, Virtual Tags, and folder-watch scanning config
   all live inside these), reader-polish itemization (Beta-blocking), remote/server sharing (needs
   real design work before any code), metadata editing (properties editor + bulk edit + file
   write-back — touches real risk, worth its own careful spec), library-browsing extras, News-reader
   repurposing (needs its own brainstorm first, since "repurpose" isn't a spec yet).
