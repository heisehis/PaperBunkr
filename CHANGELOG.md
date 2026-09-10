# Changelog

All notable changes to Paperbunkr are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
