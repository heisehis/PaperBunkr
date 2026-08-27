# Plugins

PaperBunkr has a plugin host modeled on ComicRack CE's, for **genuinely novel automation** —
not for backfilling core features (importing, scraping, themes, and reading-list tools are
all built in).

> **Not compatible with ComicRack CE plugins.** CE plugins are IronPython (`.py`) against a
> WinForms API. PaperBunkr plugins are **C# scripts** (`.csx`) against PaperBunkr's own
> abstractions. This is deliberate.

## Installing a plugin

A plugin is a folder under:

```
%AppData%\Paperbunkr\plugins\<plugin-name>\
```

containing:

- `plugin.xml` — the manifest (one or more `Command` entries: hook, name, description,
  icon, parameter count).
- one `.csx` C# file per command.

Drop the folder in, restart PaperBunkr, and open **Preferences → Plugins** (or the
**Plugins** screen). Each command is compiled on startup; a broken script is listed with
its **compile error** rather than silently dropped, and never blocks other plugins.

## Running a plugin

On the **Plugins** screen, commands are grouped by **hook** (the event or menu they attach
to — e.g. a library-command hook, a book-context hook). Click **Run** on a command, or
trigger it from the surface its hook targets.

## Writing a plugin

You get an environment object exposing library CRUD, navigation, and thumbnails —
comparable in power to CE's `IPluginEnvironment`. The reference test plugin is a
**Duplicate Finder**. See the design spec in the repo
(`docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md`) for the full hook list and
API surface.

Python interop (`pythonnet`) is a possible future addition but is not in the current
build.
