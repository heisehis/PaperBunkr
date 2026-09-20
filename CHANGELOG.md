# Changelog

All notable changes to Paperbunkr are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **Metron as an alternative to ComicVine.** Save your Metron login under Preferences → **Connections** and every place that used ComicVine can use Metron instead, per series: a series' **Missing Issues** search and the Wanted **Track a series** search gain a source selector, tracked series and wants from Metron carry a small **Metron** chip, and the daily refresh, release search and import-time details each go to the series' own source. **Scrape** starts on the source chosen under Preferences → Organize & Scrape (scheduled scrapes always use it) and the match dialog can switch a single run. Nothing is merged between the two: a series belongs to one source. Existing series stay on ComicVine; this adds a migration, so back up your database first.
- **Comic acquisition.** A Mylar-style want-list built on your own Prowlarr and qBittorrent.
  Preferences → **Acquisition** connects Prowlarr and qBittorrent (keys and passwords are stored
  encrypted), sets how often to check, how to rank results, where finished comics go, and how they are
  named (a live preview shows the result). The new **Wanted** screen tracks ComicVine series, lists what
  you're missing and what's coming, follows a series so new issues are requested automatically, and shows
  the releases Prowlarr found: **Grab** one to send it to qBittorrent, watch its progress, and Paperbunkr
  imports the finished file into your library (the original keeps seeding), or **Reject** it to never see it
  again. A series' page gains a **Missing Issues** section with covers and per-issue Request, and reading
  lists built from a story arc gain **Request missing issues** and **Follow this arc** (a daily task, off
  until you turn it on under Automation, keeps followed arcs up to date and requests what's new).
  Downloading is manual approval by default; **Download automatically** is a separate opt-in.

- **ComicVine details on downloads.** When an acquired issue is imported, Paperbunkr now fetches its credits, summary, characters and dates from ComicVine by the issue you
  asked for (no searching, nothing to confirm) and writes them into the file. If it can't, the issue stays in your library as it is, the attempt is recorded, and it is retried
  on its own; anything that needs you (no ComicVine key, an issue ComicVine no longer has) appears under **Wanted → Needs attention** with Retry and Dismiss, one at a time or all at once.
  It can be turned off in Preferences → Acquisition.

- **ComicVine scraping and library organizing are built in** (they were the Cluster Library Manager plugin). Right-click comics or a series and choose **Scrape with ComicVine…** to match
  them (series, then issue, with a progress header and one Activity Center job), or **Organize…** to move or copy files by an organizer profile's templates, with collision handling and
  **Undo last organize**. A series' page gains a ComicVine panel. Settings and profiles are under **Preferences → Organize & Scrape**; two new tasks under Automation (scrape unscraped comics,
  organize library) are off until you turn them on. An installed Cluster Library Manager plugin is no longer loaded.
- Prowlarr and qBittorrent settings now live under **Preferences → Connections** with every other key and password; Acquisition keeps its behavior settings.

### Changed

- Naming templates for downloaded issues now use the same `{<token>}` syntax as the organizer. Your saved template is converted once and the original is kept; if it can't be converted exactly, the default is used and Preferences shows what you had. A colon in a name becomes " - " (as in ComicRack) instead of a dash.
- API keys and passwords saved in Preferences → Connections are now encrypted on disk (Windows DPAPI).
  Existing keys are upgraded the first time they are read. A database copied to another Windows account
  or machine will ask for them again.
- All ComicVine requests now share one rate limit (about one per 1.1 s, 200 an hour), with room held back so
  a background search can never starve what you're doing in the app.

## [0.6.4-beta] - 2026-09-19

### Added

- **Themes** (Preferences → Appearance). Skins are now Themes, each with a Light or Dark variant.
  New built-in themes: Daylight, Overcast, Matrix, Maximum Contrast, and a colour-blind-safe theme.
  Theme options: **True black** for OLED screens (suspended automatically while you read),
  **Auto switch** (follow the system Light/Dark setting, or switch on a schedule you set),
  an **accent color override**, a **font family** picker, and a **Matrix rain** toggle for the
  Matrix theme's falling-glyph background (also throttled on battery and under Reduced Motion).
- **Smooth scrolling** toggle in the Library view popup (default on): mouse-wheel notches ease to
  their target instead of jumping. Touchpads, the scrollbar, keyboard and touch are unaffected, and
  it stays off when Windows asks for reduced motion.
- **Suggested Story Events and Continuities.** Paperbunkr now proposes Story Events (issues that
  share a Story Arc, optionally checked against ComicVine/Metron) and shared-universe Continuities
  (matched through Wikidata and your series' recurring characters). Nothing is created until you
  click Accept, and anything you Dismiss stays dismissed. Suggestions refresh on a schedule, and
  there is a **Check Wikidata** button in the Story Events sidebar, a per-issue look-up button in
  Issue Properties, and **Look up on Wikidata** on a series' Continuities tab.
- **Tracker improvements.** MangaDex joins the supported trackers, each tracker link now shows
  its own score and finish date, and Preferences → Connections → **Tracking behavior** has five
  toggles for automatic progress updates (progress after reading, on mark-as-read, auto-pull from
  trackers, and more). Every automatic update reports through the Activity Center. Setup steps for
  all eight trackers are on the wiki's Trackers page.
- **External metadata does more.** A linked external record can now bring in its related titles,
  tags and a series rating, not only the basic fields.
- **Reader options** (Preferences → Reader): **Auto-hide toolbar when idle**, and a **Chrome reveal
  style** that chooses between each corner control popping in as you hover over it and the whole
  toolbar appearing on any mouse movement. **Save Page As** in the page right-click menu saves the
  current page as PNG or JPEG, and **Change Cover** on a series can pick a cover from another
  comic in the series or from a reading list, not only from a file.
- **Book file associations** now include EPUB, FB2 and MOBI in the installer and in
  Preferences → Advanced → File Association.

### Changed

- **Library search and scrolling were rebuilt for large libraries** (see Fixed). Searching by a
  field in the per-comic grid (e.g. `writer:miller`) now lists only the comics that match, as
  ComicRack does, instead of every comic in any series that had one match; searching a series name
  still lists all of that series' comics.
- Search and filter changes no longer replay the staggered card fade-in (it still plays on opening
  the library, changing view mode, and changing sort/group), and a new search result set starts
  scrolled to the top.
- Hover and focus rings now also appear on Panorama covers and on List, Tiles and Details rows.
  Poster-grid publisher badges show the logo only.

### Fixed

- **Library search no longer lags while you type.** Results now update ~150 ms after you pause
  (instantly when you clear the box), computed off the UI thread from a cached snapshot instead of
  rebuilding every card and row on every keystroke, and swapped in with a single refresh. Typing in
  a 3,000-comic library dropped from roughly 0.7–1.9 s per keystroke to a few milliseconds of
  UI-thread work. Search text is still remembered across restarts.
- **Library scrolling is smoother.** The staggered card fade-in now plays only when a screen opens
  or its view/sort/group changes, not on every scroll. Poster and Tiles cards build their optional
  parts (dog-ear peek, selection box, plugin overlay, rating badge) only when needed, the grid stops
  re-measuring on every scroll step, and covers come from a display-size cache (about 300 MB budget)
  with a newest-first decode queue that prefetches about two screens ahead, so covers no longer pop
  in while you scroll.
- Opening a comic or book by double-clicking it (or through a file association) could hang the app
  on launch.
- Book reader drawers (contents, bookmarks, highlights, search, font and theme) showed only their
  header, with the rest hidden behind the page.
- Panorama grid covers no longer leave gaps: the minimum card width now fits a standard cover.

## [0.6.0-beta] - 2026-09-15

### Added

- **Library screen master-detail redesign.** Library moves from a single grid+sidebar layout to a
  3-pane view — sidebar, list, and a live preview panel that shows details for the selected series
  or issue without leaving the list. View modes consolidate from 5 down to 3 (Grid, List,
  Details/Table); Grid absorbs the old Poster/Panorama split as a cover-fit toggle, List absorbs
  Tiles as a density toggle. Tile overlays (publisher/language chips, unread dot, rating badge,
  dog-ear peek, selection checkbox) now follow a single priority-ordered corner system instead of
  stacking ad-hoc, and the toolbar/tiles spend more of the app's existing skin palette instead of a
  hardcoded color.
- **Cosmetic thumbnail toggles** (Preferences → Library → View & Sort): fade-in on genuine cover
  decode, a hover/selected dog-ear peek at a comic's real second page, hover tooltips with a small
  thumbnail plus title/writer/penciller/summary/file info, and a numeric rating badge. Preferences →
  Advanced gained "Exported reading lists contain filenames" for `.cbl` export.
- **Open a comic/manga file on launch.** Double-clicking a supported file (or an OS file-association
  launch) now opens it directly — importing it into the library first if it isn't there already —
  instead of just restoring the last screen.
- **Plugin context menu.** Library and Detail right-click menus now show a "Plugins" submenu listing
  every enabled Library-hook plugin command, replacing a single hardcoded "Find Duplicates" entry.
- **Native plugin API:** `BeginModalBatch` keeps a persistent progress header mounted across a
  sequence of modal calls (e.g. a scrape batch), instead of the dialog shell tearing down between
  each item.

### Fixed

- Book reader could fail to open with an "Access is denied" error on a per-machine (Program Files)
  install — the embedded browser's data folder now lives under the user's own AppData instead of
  next to the installed EXE.
- A downloaded update installer could vanish before "Restart" was clicked — it's now saved to
  `%AppData%\Paperbunkr\updates` instead of the OS's temp folder, which can be swept at any time.
- Clicking non-interactive content inside a dialog (e.g. a decorative label) could unintentionally
  close the whole dialog — only a genuine click on the dialog's own backdrop dismisses it now.
- Picking a suggestion from a search/autocomplete field could occasionally crash the app.

## [0.5.0-beta] - 2026-09-14

### Added

- **Update-available Activity Center alert.** Checking for updates (Preferences → About →
  "Check Now", or the automatic startup check) now raises an Activity Center alert with a "View
  release" link straight to the GitHub releases page, alongside the existing in-app download flow.
- **"Confirm before closing" toggle** (Preferences → General → Window). When on, clicking the
  window's close button asks before actually quitting — only when the close button would really
  exit the app, not when Minimize to tray is already catching it.

## [0.4.2-beta] - 2026-09-14

### Added

- **Reader chrome redesign.** The comic/manga reader's floating toolbar clusters, drawer, and page
  thumbnail rail now use a frosted-glass look consistent with the rest of the app. Reading-mode and
  fit-mode pickers are icon-labeled pill menus instead of plain text lists; zoom is a single control
  that works the same way in paged and continuous/long-strip modes. The Reader Tools drawer is
  reorganized — rotate/auto-rotate/double-page as one icon grid, color adjustments tucked behind a
  collapsible section, bookmarks always visible at the bottom instead of scrolling away. The page
  thumbnail strip now shows a hover-magnify effect (like the macOS Dock) so individual pages stay
  easy to pick out even in long collected editions, and never grows wider than its panel regardless
  of page count.
- **Issue Properties and Bulk Issue Properties redesign.** Both editors match the newer card-based
  look, with a leading icon on every field label, a friendlier grouped layout for the single-issue
  editor's Details tab (Core Details / Credits), and a proper multi-option picker for tag weight
  (Unset/Incidental/Recurrent/Defining/Core) in place of a free-typed box.
- **Detail screen redesign.** Comic and manga detail pages, the issues grid, and the Details/
  Related/Activity tabs share the same visual language now; Details gained more credit and metadata
  fields, and the Activity tab's event log covers more of what happens to a series.
- **Native plugin support.** Plugins can now ship native (non-.NET) code, with automatic updates, a
  trust notice and picker for installing a `.pbplugin` package, and a new extension point for adding
  custom panels to the comic detail screen. The Plugin Management screen itself was redesigned
  around a master-detail layout.
- **Reader: page background textures.** A textured page background option (matching CE) with a
  matching page drop-shadow.
- **Reader: page spread position tracking.** Manually mark a page as the leading or trailing side of
  a two-page spread, via a new context-menu submenu and a thumbnail-rail glyph; imported from
  ComicInfo.xml's `<Pages>` data when present.
- **Reader: smoother long-strip (webtoon) scrolling.** Very tall continuous-mode pages now decode in
  bands instead of all at once, cutting memory use and improving scroll smoothness.
- **Reader: cursor-anchored zoom in continuous mode.** Ctrl+scroll-wheel and pinch-zoom now zoom
  toward the cursor/pinch point instead of the canvas center.
- **Library: more sort/group options.** Group by Virtual Tags, a "Needs Review" grouper, group by
  open count, and a three-state Is-Final-Issue filter.
- **Library: keyboard navigation.** Type-ahead jump-to-item, Shift+Arrow range selection, and Ctrl+Q
  to quit.
- **Smoother screen-entrance animation** for the Books, Smart Lists, and Reading Lists grids,
  matching the Library grid's existing entrance motion.
- **`Issue.AlternateCount`** — the ComicInfo.xml crossover-issue count field is now read and
  editable, closing a CE-parity gap.

### Fixed

- **Reader:** the page-position scrubber no longer stretches the whole toolbar edge-to-edge (and
  never shows a scrollbar) on books with very high page counts, like trade paperback compilations —
  the page dots shrink to fit instead.
- **Reader:** the textured page background's drop-shadow no longer blanks out the whole page in
  paged mode.
- **A rare startup crash in native-plugin modals** (e.g. mid-scrape review dialogs), caused by a
  plugin's background work occasionally resolving on a non-UI thread.
- **Library plugin commands** (right-click "Plugins ▸" on a tile, or the bulk-action dropdown) were
  silently broken since the native plugin tier landed — a leftover call to a since-renamed method
  meant no enabled library plugin command could run. Fixed.
- A form-field auto-complete box (`SuggestBox`, used throughout the metadata editors) in strict mode
  could show a field's own last-typed value instead of the real current one.
- The Library grid's collection tiles now correctly participate in the cover-flight transition when
  opening a collection, instead of jumping straight to the destination screen.

## [0.3.1-beta] - 2026-09-10

### Fixed

- **App would not launch on some machines after installing 0.3.0-beta.** Setup completed
  normally, but the application never opened a window. The 0.3.0-beta build used ahead-of-time
  (ReadyToRun) compilation, and those precompiled images could crash during startup on CPUs
  different from the one that built the release — before any window or error could appear.
  0.3.1-beta disables ReadyToRun; startup is a fraction of a second slower on a cold launch, but
  reliable. If you hit this, install 0.3.1-beta over the top — no data is affected.
- **Recovery from a failed start.** If Paperbunkr ever crashes before its window appears (a bad
  graphics driver, for example), the next launch now automatically retries with hardware
  acceleration turned off and tells you, instead of silently failing to open.
- **"Check for updates" never found anything.** The published update feed was missing its
  signature file, so the app — which verifies it — always reported "up to date" regardless of
  what was released. Fixed in the release pipeline; this and future releases carry the signature,
  so 0.3.0-beta installs will now see 0.3.1-beta.

## [0.3.0-beta] - 2026-09-10

Everything below shipped since
[0.2.0-beta](https://github.com/heisehis/PaperBunkr/releases/tag/v0.2.0-beta).

### Added

- **Insights & Stats** — a reading-habit dashboard (new "Insights" item in the nav rail) and a
  Stats view with library and reading analytics, both backed by a running log of your reading
  activity.
- **Quick Open (`Ctrl+P`)** — a command palette to jump to any screen, series, or action by
  typing.
- **Activity Center** — a status bar with a pop-out panel showing background jobs (scans, cover
  work, scheduled tasks) and alerts.
- **Library Health** — a Preferences dashboard that finds missing files, flags likely duplicates,
  and collects everything needing attention in one "Needs Review" list.
- **Automation** — background maintenance tasks (library rescan, cover verification, backups, and
  more) on a schedule you control from Preferences → Automation.
- **Virtual Tags** — rule-based tags that apply themselves to matching issues, editable from
  Preferences.
- **Saved Workspaces** — save and restore a full Library or Books view (filters, sort, grouping,
  columns) by name.
- **FB2 and MOBI/AZW3 books** — both formats now import and read alongside EPUB and PDF.
- **Reworked Books/PDF reader** — a reflow renderer for EPUB, a shared reading HUD, in-text
  highlights and notes, and accessibility support.
- **More trackers** — MangaUpdates, MangaDex, and Kitsu, plus two-way sync that can push your
  progress and ratings back to a provider.
- **Python plugin commands** — plugins can now script commands in Python, alongside the existing
  plugin API, which also gained new UI extension points and bulk operations.
- **Duplicate Finder** — shipped as a built-in installable plugin.
- **Drag-and-drop import** — drop files, folders, or `.cbl` lists onto the Library or a Reading
  List to import them.
- **File metadata write-back** — optionally write your edits back into the comic file as
  `ComicInfo.xml` and/or a sidecar (off by default).
- **Reader extras** — split-page part-by-part navigation, an on-screen clock and battery
  indicator, and tap-to-toggle chrome on touch.
- **Redesigned installer** — brand artwork, a combined license + terms page, per-format
  file-association checkboxes, a prompt to close Paperbunkr before installing, and a
  repair/uninstall path when re-run over an existing install.
- **Startup splash and Welcome screen** — a branded splash while the app loads, a redesigned
  first-run Welcome screen, and a "What's New" panel that shows this changelog the first time you
  open a new version (also available any time from Preferences → About).

### Changed

- **Animated navigation** — covers and cards fly between the Library and detail screens,
  drill-downs slide in and out, and screen/content transitions were polished throughout.
- **Faster, lighter Library** — every view mode is now virtualized, so large libraries use far
  less memory and scroll smoothly. The separate "Comic List" mode was removed and its
  sorting/grouping folded into the other modes.
- **Panorama view** shows each cover at its real shape (portrait, landscape, square) without
  loading every image up front.
- **Redesigned Preferences** — every area reworked around a tile-based hub: Appearance, Libraries,
  Library Health, Folder Management, Virtual Tags, Connections, Keyboard Shortcuts, Advanced,
  About, and Automation.
- **Better metadata editors** — autocomplete, dropdowns, and steppers matching ComicRack's
  editing feel.
- **Consistent feedback** — unified toasts, confirmation dialogs, busy indicators, and status
  badges across the app.
- **Nav rail** — hover-to-expand is now a preference; the Undo/Redo buttons were removed.
- **Faster startup** — the main window now appears while your library finishes loading in the
  background instead of after it, and the heavy editor overlays build the first time you open
  them rather than at launch.
- **Smoother reading** — the page decode, cache, and prefetch pipeline was rebuilt; pages load
  faster, memory use is bounded, and flipping quickly through a book no longer stutters. A
  performance overlay is available with `Ctrl+Shift+P`.
- **Home masthead** — the blurred cover wall behind the spotlight now picks up the featured
  book's colour and the active theme.
- **About** — shows the release version (`0.3.0-beta`) with the exact build alongside it.

### Fixed

- Comic files could be flagged "missing" immediately after a metadata write-back touched them; a
  startup self-heal now repoints any that were affected.
- A database-migration rollback chain that could fail when downgrading.
- The reader settings sheet rendering oversized on a non-maximized window.
- Unregistering a file association left an orphaned registry key behind.
- The startup splash could freeze and show "Not Responding" on a large library before the main
  window appeared.
- Flipping pages rapidly could crash the reader.

## [0.2.0-beta] - 2026-09-01

Paperbunkr moves from alpha to beta with this release. Everything below shipped since
[0.1.1-alpha](https://github.com/heisehis/PaperBunkr/releases/tag/v0.1.1-alpha).

### Added

- **Collections** — group series, issues, and books manually or with rule-based Smart
  Collections, browsable from the Library sidebar.
- **Plugin ecosystem** — a package manager to install, update, and remove plugins from
  Preferences → Plugins, backed by a new plugin API for reading metadata, running rules, and
  writing changes under user confirmation.
- **Smart Lists v2** — nested AND/OR condition groups and new text operators (list-contains,
  regex, case sensitivity).
- **Story Events & Continuity** — bulk selection, continuity editing and merging, cross-event
  relations, format-based grouping suggestions, and an age/appearance timeline.
- **First-run onboarding** — a welcome screen with an optional guided tour for new installs.
- **Full keyboard navigation** — arrow-key movement through every grid and sidebar, keyboard
  access to context menus, Back/Forward through screen history, and new shortcuts: `Ctrl+,` for
  Preferences, `Ctrl+Tab` / `Ctrl+Shift+Tab` to cycle screens, and `Ctrl+A` / `Delete` / `/` in
  the Library grid.
- **Database safeguards** — startup integrity checks, a recovery flow for a corrupted database,
  crash-safe WAL mode, and automatic backups.
- **Library search suggestions** as you type.
- **Auto-update** — Paperbunkr checks for new releases on startup (and on demand from
  Preferences → About) and can download and apply updates in-app.
- **In-app changelog**, viewable from Preferences → About.
- **Installer**: optional "Launch at Windows startup" and "Associate comic/manga files" tasks, a
  pre-install what's-new page, and an opt-in prompt to delete your library data on uninstall.

### Changed

- **Redesigned Home, Detail, and Library screens** — a shared layout across Comic, Manga, and
  Book detail screens, a cover-forward Home dashboard, and a reworked Library toolbar with a
  View & Sort panel and filter chips.
- **Redesigned Preferences**, reorganized into Appearance, Behavior, Libraries, Reader, Advanced,
  Plugins, and About.
- New iconography for publisher, format, age-rating, and language marks across library, detail,
  and metadata screens.
- Library and Smart Lists load noticeably faster on large libraries.
- Upgraded to .NET 10 under the hood.

### Fixed

- ComicRack CE migration: series-identity matching and embedded-metadata precedence.
- Trade-paperback series folding and anthology series auto-splitting during library scans.
- A "database is locked" crash when Library search raced another database write.
