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

### Showing progress and problems in the Activity Center (API 4.1)

`Environment.Activity` lets a plugin - script or native - report background work and raise
alerts. Everything is attributed to your plugin by name.

```csharp
var job = Environment.Activity.StartJob("Syncing your reading list");
job.Report(3, 10, "3 of 10");     // progress with a fraction
job.Report("Almost done");        // or just a status line
job.Succeed("Synced 10 items");   // or job.Fail("Server unreachable")
job.Dispose();

Environment.Activity.RaiseAlert(PluginAlertSeverity.Warning, "Sync is offline",
    "Will retry on next launch", dedupeKey: "sync-offline");
```

- A job always shows as a row in the Activity Center (kind **Plugin**), can be cancelled from there
  (`job.CancellationToken`), and is kept in the history. Disposing a job without calling `Succeed` or
  `Fail` records it as cancelled.
- Alerts come in `Info`, `Warning` and `Error`. Repeats with the same `dedupeKey` collapse into one;
  the key only matters within your own plugin. Leave it out and every call is its own alert.
- You can't choose the job kind or whether it pops a toast: a job only toasts if it fails.
- In a Python plugin: `job = globals.Environment.Activity.StartJob("Working")`.

Needs API `4.1` - declare `requiresApi="4.1"` if your plugin uses it.

### Reacting to what happens in Paperbunkr (API 4.1)

Four hooks tell a plugin that something just happened. They are **notification-only** (what your
script returns is ignored) and run in the background, so a slow plugin never holds up reading or
scanning.

| Hook | Fires when | Useful values |
|---|---|---|
| `BookRead` | A comic, manga or novel is read through to the end - **every** time, so a re-read fires again | `ItemType`, `ItemId`, `SeriesId`, `PagesRead`, `FinishedUtc`, and `Issue` or `Book` (either can be `null` if the item was deleted since) |
| `LibraryScanCompleted` | A full comic/manga folder scan finishes (Scan Now, the scheduled scan) - not a live-watch or drag import, and not a Books scan | `FolderPaths`, `AddedCount`, `SeriesTouched`, `Duration`, `AddedItemIds` |
| `MissingFileDetected` | A file is **confirmed** missing (Library Health's threshold) - once when it crosses the threshold, not on every check | `ItemId`, `FilePath`, `Title` |
| `ReadingListChanged` | A reading list's items or order change - one event per action, so importing 300 issues is one event | `ListId`, `ListName`, `Kind`, `AddedIssueIds`, `RemovedIssueIds` |

`ReadingListChanged.Kind` can combine values (a refresh may add, remove and reorder at once), so test
it with `Kind.HasFlag(ReadingListChangeKind.Removed)`. Creating, renaming or deleting a whole list is not
an event. `LibraryScanCompleted` also has `UpdatedCount`/`UpdatedItemIds`, but they are reserved and always
0/empty for now.

```xml
<Command hook="ReadingListChanged" key="mirror.changed" name="Mirror list changes"
         script="changed.csx" />
```
```csharp
// changed.csx
Environment.Activity.RaiseAlert(PluginAlertSeverity.Info,
    $"{ListName}: {AddedIssueIds.Count} added, {RemovedIssueIds.Count} removed");
return null;
```

**Ground rules the host enforces**
- **One event at a time per command.** Events for your command run in order, never overlapping.
- **A short queue, not an unbounded one.** If your command falls behind, up to 16 events wait; beyond that the
  *oldest* waiting event is dropped and you get one alert saying so.
- **A 30-second limit.** Your script gets a `CancellationToken` (a global named `CancellationToken`) that is
  tripped after 30 seconds. Pass it to anything cancellable and check it in loops. It can't force a script to
  stop - if you ignore it, your command is treated as *hung* and is given no new events until it returns.
- **Failures don't repeat.** If your command throws, times out or falls behind, that is reported once per
  session as an alert naming your plugin, not once per event.

Needs API `4.1` - declare `requiresApi="4.1"`.

### Declaring settings so Paperbunkr renders them for you (API 4.1)

Instead of building a settings screen, declare your settings in `plugin.xml` and Paperbunkr shows a
settings window for them (Plugin screen → your plugin → **Configure**):

```xml
<Plugin key="my-sync" name="My Sync" requiresApi="4.1">
  <Command hook="Startup" key="my-sync.start" name="Start" script="start.csx" />
  <Settings>
    <Setting key="server"  label="Server URL" type="text"   default="https://example.com" description="Where to sync to" />
    <Setting key="mode"    label="Mode"       type="choice" default="fast">
      <Choice value="fast"     label="Fast" />
      <Choice value="thorough" label="Thorough" />
    </Setting>
    <Setting key="limit"   label="Batch size" type="number" default="50" min="1" max="500" />
    <Setting key="notify"  label="Notify me"  type="toggle" default="true" />
    <Setting key="token"   label="API token"  type="secret" />
  </Settings>
</Plugin>
```

| `type` | Shown as | Notes |
|---|---|---|
| `text` | A text box | Anything goes |
| `choice` | A pick-list | Needs `<Choice value label>` children; the stored value is the `value` |
| `number` | A number box with up/down | Optional `min` / `max`; decimals are fine |
| `toggle` | An on/off switch | Stored as `true` / `false` |
| `secret` | A masked box | Encrypted on disk; no `default` allowed |

You read them with `Environment.GetSetting("mode")` exactly as before, and the host makes sure you only
ever see a sensible value:
- **Unset** gives you the declared `default` (a secret has none, so you get `null`).
- **A stored value that breaks the rules** (a choice you no longer offer, a number out of range, a hand-edited
  database) gives you the `default` too, so you never have to validate. The settings window still shows the
  bad value and flags it so the user can fix it; it is never silently rewritten.
- **A secret** is encrypted (Windows DPAPI) when it's saved and decrypted when you read it. That protects a
  copied database file; it does **not** hide it from code running as the same Windows user, and it won't
  decrypt on another Windows account or PC - in that case it reads as unset and the user re-enters it.
- Settings you *don't* declare behave exactly as before: stored and returned verbatim.

A plugin gets **one** settings definition. If a plugin declares `<Settings>` *and* has a `ConfigScript` command
(or, for a native plugin, its own settings screen), it is not loaded and the Plugin screen says why. A
malformed `<Setting>` (unknown `type`, a duplicate `key`, a `choice` with no choices, a `default` that isn't
one of them, a `default` outside `min`/`max`) blocks the plugin the same way, naming the setting at fault.

Needs API `4.1` - declare `requiresApi="4.1"`.

#### Locking a setting, resetting, and moving settings between machines (API 4.2)

- **`locked="true"`** on a `<Setting>` (for something critical like a root path) leaves it editable until a value is
  saved, then greys it out in the settings window. The user can press **Unlock** (two clicks) to change it for that
  visit. Your own `SetSetting` calls are never blocked. Anything other than `true`/`false` blocks the plugin.
- **Reset to default** appears beside any setting that has a saved value, and **Reset all** in the window header
  resets every unlocked setting (it tells you how many locked ones it skipped). A reset secret is simply cleared.
- **Export… / Import…** in the header save and load a plugin's settings as a small JSON file. Secrets are never
  exported (they only decrypt for one Windows account), and an import skips secrets, values that break the plugin's
  rules, and locked settings that already have a value, then tells the user what it skipped. A file exported from a
  different plugin is refused.

### Logging from a plugin (API 4.2)

`Environment.Log.Info("synced 12 items")` (also `Debug`, `Warn`, and `Error("message", exception)`) writes to
`%AppData%\Paperbunkr\logs\plugins\<your-key>.log`, separate from the app log; the file rolls to `.1.log` at 1 MB.
The Plugin screen's **Open log folder** button opens it, and a failing command or hook is logged there
automatically. Logging never throws. Each command also shows a muted line on the Plugin screen with its run count,
average and slowest time, failures, timeouts and dropped events since the app started (not saved between runs).

Needs API `4.2` - declare `requiresApi="4.2"`.

### Declaring the API version you need

Add `requiresApi` to the `<Plugin>` element to say which plugin API version your plugin was
written for:

```xml
<Plugin key="my-plugin" name="My Plugin" requiresApi="4.1">
```

The version is `major.minor`, written `"4.1"` or just `"4"` (meaning `4.0`). Leave it out and
your plugin is treated as needing the original `4.0` API, so every existing plugin keeps working
without changes.

- **A different major version is blocked.** If your plugin requires API `5.0` and the app
  provides `4.x` (or the other way round), the plugin is not loaded at all - no script is
  compiled and no assembly is opened. The Plugin screen shows why, for example
  *plugin requires API 5.0, this app provides 4.0 (major version mismatch)*. A value that isn't
  `"N"` or `"N.M"` is blocked the same way.
- **A different minor version is not blocked.** A `4.9` plugin still loads and runs on an app
  that provides `4.0`. The version only matters if something then goes wrong: a compile error,
  a failed load, or a missing member or type is reported with a note such as *plugin declares
  API 4.9, this app provides 4.0*, so you can tell an out-of-date app from a bug in your code.
  The note is left off ordinary errors when your declared version isn't the likely cause.
- The Plugin screen shows "Requires API x.y" under the plugin's name as plain information.

Only the plugin's own `<Plugin>` element carries the attribute - not individual `<Command>`s.

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
  screen, under **Plugins**) grouping every book that shares its series and number with
  another book, recomputed each time you open it. Opens the **Grouped Review** overlay: pick
  which copy to keep in each group (or skip a group entirely), then **Resolve All** to bulk
  delete the rest in one pass. A library scan or import that finds *more* duplicate groups than
  last time also raises a proactive Activity Center alert with a "Review" link straight into
  this same overlay.

Read its three `.csx` files for a short, real example of `Environment.App.GetLibraryBooks()`,
`Environment.App.AskQuestion(...)`, and returning grouped results (a `PluginBookGroup[]`, one
group per duplicate cluster with a suggested copy to keep) from a `CreateBookList` command - or
return a plain flat list instead if your own list doesn't need the review-and-bulk-delete
treatment; both shapes work.

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

> **Cluster Library Manager** (ComicVine scraping and library organizing) is now built into Paperbunkr. See [Scraping and Organizing](Scraping-and-Organizing). An installed copy of the old plugin is not loaded.
