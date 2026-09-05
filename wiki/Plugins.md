# Plugins

PaperBunkr has a plugin host modeled on ComicRack CE's, for **genuinely novel automation** —
not for backfilling core features (importing, scraping, themes, and reading-list tools are
all built in).

> **Not compatible with ComicRack CE plugins as-is.** CE plugins are IronPython (`.py`) against a
> WinForms API; PaperBunkr's own abstractions are different, so a CE script can't be dropped in
> unchanged. PaperBunkr supports both **C# scripts** (`.csx`, the primary path) and **IronPython
> scripts** (`.py`, for porting CE automation logic) against those same abstractions.

## Installing a plugin

A plugin is a folder under:

```
%AppData%\Paperbunkr\plugins\<plugin-name>\
```

containing:

- `plugin.xml` — the manifest (one or more `Command` entries: `hook`, `key`, `name`,
  `description`, `script`).
- one `.csx` (C#) or `.py` (IronPython) file per command — the file extension picks which
  engine runs it; a `.py` entry also needs a `method` attribute naming the function to call.

Drop the folder in, restart PaperBunkr, and open **Preferences → Plugins** (or the
**Plugins** screen). Each command is compiled on startup; a broken script is listed with
its **compile error** rather than silently dropped, and never blocks other plugins.

**Try the real example below first** if you just want to see a working plugin before writing
your own.

### Installing from a package (.zip)

If a plugin is distributed as a `.zip` package, you don't need to unzip it by hand: on the
**Plugins** screen, use **Install Package…**, pick the `.zip` file, and it's installed and
discovered immediately — no restart needed. The same screen lists your installed packages and
lets you remove one.

## Running a plugin

On the **Plugins** screen, commands are grouped by **hook** (the event or menu they attach
to — e.g. a library-command hook, a book-context hook). Click **Run** on a command, or
trigger it from the surface its hook targets.

## Writing a plugin

You get an environment object exposing library CRUD, navigation, and thumbnails —
comparable in power to CE's `IPluginEnvironment`. A **Data Manager**-class plugin also gets:

- **`Environment.Metadata`** — read access to the relationship / continuity / story-event /
  comic-age graph (the same data the Detail and Story Events screens show).
- **`Environment.Rules`** — run the app's own Smart List matcher: evaluate a throwaway rule,
  or `EvaluateSmartList(id)` to get exactly what a saved Smart List currently matches.
- **`Environment.Writer`** — a curated, audited per-field write surface (format, book age,
  custom values, tags). Every successful write is logged to `startup.log`.
- **`Environment.GetSetting` / `SetSetting`** — persistent per-plugin key/value config,
  scoped to your plugin so two plugins can't collide on a key.

A command that writes in bulk should declare `confirmWrites="true"` on its `<Command>`
element. When it does, `Environment.Writer` calls **fail closed** (return `false`, no DB
write) until the command has shown an `Environment.App.AskQuestion(...)` prompt and the user
has chosen the primary (affirmative) button in that same run — so a bulk edit is
*structurally* required to ask first.

See the design specs in the repo (`docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md`,
`docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md`, and
`docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md`) for the full hook
list and API surface.

## Example: Duplicate Finder

A complete, working plugin lives in the repo at `sample-plugins/DuplicateFinder/`, packaged
as `sample-plugins/DuplicateFinder.zip` for the one-click install path above. It's a real
three-command plugin, not a stub:

- **Duplicate Finder Activated** (`Startup`) — logs that the plugin loaded.
- **Find Duplicates in Selection** (`Library`) — right-click a book on the Library screen;
  compares it against the whole library for a same-series, same-number copy and shows what
  it finds.
- **Possible Duplicates** (`CreateBookList`) — a dynamic Smart List entry (Smart Lists
  screen, under **Plugins**) listing every book that shares its series and number with
  another book, recomputed each time you open it.

Read its three `.csx` files for a short, real example of `Environment.App.GetLibraryBooks()`,
`Environment.App.AskQuestion(...)`, and returning a book list from a `CreateBookList` command.

### A note on the sandbox

The `.csx` compile step is fenced so a **well-meaning plugin author can't accidentally reach
past the curated environment** — the app's internal rule/graph engine types aren't
compile-visible to a script, and `#r` directives can't pull extra assemblies (an EF Core
raw-database handle, say) into scope. This is **accidental-overreach protection, not
adversarial isolation**: scripts still run in-process with no AppDomain or process boundary,
so someone *deliberately* trying to escape the reference set via reflection
(`Type.GetType` + `Activator.CreateInstance` against an internal type name) can still
technically succeed. Only run plugins you trust.

A `.py` script gets the same environment object and a comparable sandbox: static analysis
rejects a `clr.AddReference(...)` call naming anything outside the same fixed reference set
the `.csx` path uses, at discovery time rather than only at runtime.
