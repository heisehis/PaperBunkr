# Changelog

All notable changes to Paperbunkr are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
