# Plugin API 4.1 — versioning, Activity reporter, domain hooks, settings schema

*Date: 2026-09-20. Scope: four additive pieces on top of the v2 (script tier), v3 (data-manager
facades) and v4 (native tier) plugin API — `docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md`,
`2026-08-28-plugin-api-v3-data-manager-design.md`, `2026-09-11-plugin-api-v4-native-tier-design.md`.
Produced via a three-round `/grilling` pass against a plugin-API pitch list; every decision below is
a settled answer from that pass. Status: **all four slices implemented and tested 2026-09-20**;
on-screen verification pending. Each slice has an "as implemented" note where it departed from this
draft (§3.2, §4.4, §5.3–§5.4, §6.5) - where they disagree with earlier text, the notes are right.*

## 1. Why this exists

Plugins can pull a lot from the host (`IMetadataGraph`, `IRulesEngine`, `IMetadataWriter`,
per-plugin settings) but get almost no push events, cannot report background work through the
Activity Center, and hand-build every settings screen. There is also no way for the host to tell a
plugin written against a newer API from one that is simply broken. This spec closes those four gaps
and nothing else; the wider pitch list is deferred (§9).

**Deliberate deviation from CE:** ComicRackCE's plugin code has no API-version concept, no
domain-event hooks, and no declarative settings (CE's `ConfigScript` hook is its only settings
path). Everything here is Paperbunkr-only. Per the standing rule, the CE plugin sources were checked
for a version gate and none was found; that search was a grep, not a full read, so treat "CE has
none" as strong evidence rather than proof.

## 2. Delivery order

One spec, four slices, in this order, each shippable on its own:

1. **Versioning** — first, because the hooks slice tags each hook with a `Since` minor.
2. **Activity reporter.**
3. **Domain event hooks.**
4. **Settings schema.**

The API constant moves `4.0` → `4.1` in the first slice that ships anything a plugin can **call**,
and only then. Slice 1 adds a manifest attribute and metadata but no callable member (and an older
host ignores an unknown XML attribute), so **`PluginApi.Current` stays `4.0` through slice 1 and the
bump lands in slice 2** with the reporter.

## 3. Slice 1 — API versioning

### 3.1 Scheme

`major.minor`. Major = breaking change to existing members; minor = purely additive (new hook, new
interface, new member). A plugin declares the minimum it needs.

- `PluginApi.Current` — a hand-maintained `Version` constant in `Paperbunkr.Plugins.Abstractions`,
  namespace `Paperbunkr.Plugins`, bumped deliberately in the same commit as any API addition. It is
  not derived from the assembly version, which tracks app releases and would drift from real API
  changes.
- Manifest: `requiresApi="4.1"` on the `<Plugin>` root only, not per `<Command>`.
- **Absent attribute = baseline `4.0`**, i.e. the v4 native-tier API. Every existing manifest keeps
  working unchanged.

### 3.2 Hard block on a major mismatch; lenient on minor

*Revised after an external review of this spec (2026-09-20): the first draft was lenient about major
mismatches too. A major bump is by definition a breaking change to existing members, so running a
plugin built against a different major is not worth the risk; a minor difference is additive by
construction and stays lenient.*

- **Major differs from `PluginApi.Current`'s major, in either direction** (a v1 plugin on a v2 host,
  or a v2 plugin on a v1 host): the plugin is a **load error**. It is marked broken with a reason
  like `plugin requires API 5.0, this app provides 4.1 (major version mismatch)`, and **its code is
  never loaded** — for a native plugin `PluginLoadContext.LoadPlugin` is not called; for a script
  plugin nothing is compiled. This is a compatibility guard, not a security boundary: native
  plugins are full-trust either way (v4 native-tier spec §2).
- **Same major, any minor** (a `4.2` plugin on a `4.1` host, or a `4.0` plugin on a `4.1` host): the
  host **does not block.** The plugin is loaded and run, and the version is used only if it actually
  fails, to explain the failure.
- **No `requiresApi`** is the `4.0` baseline, so it always matches while the major is 4.
- **A malformed `requiresApi`** (anything other than `"N"` or `"N.M"`, e.g. `"abc"` or `"4.1.2"`) is
  blocked the same way as a major mismatch, with a reason like
  `plugin declares an invalid requiresApi 'abc' (expected "N" or "N.M", e.g. "4.1")`. It tells the
  host nothing about compatibility, and a malformed manifest entry is broken-with-a-reason elsewhere
  in this spec (§6.1). *(Decided in the slice 1 plan; the first draft was silent.)*
- A blocked **script** plugin registers no commands, so it is surfaced through a per-package record
  (`PluginEngine.PackageApiInfo`), not a command's `CompileError`; a blocked **native** plugin gets a
  normal `NativeLoadResults` load error carrying the same reason.

For the lenient (same-major) case, two kinds of failure both get the hint:

- **Load/compile failures** (native `LoadPlugin` throws; a `.csx` or `.py` file does not compile):
  these already set `IsBroken` today, and keep doing so.
- **Invoke-time failures** (`MissingMemberException` — which covers `MissingMethodException` — or
  `TypeLoadException`): these are recorded in the existing `PluginInvocationResult` error today and
  **keep only reporting** — they do not start disabling the command, so one bad call never silently
  turns a command off. *(An earlier draft also listed "a hook name this host does not know" here;
  corrected 2026-09-20 — an unknown hook is never invoked, so no failure exists to attach a hint to.
  Such a command loads and simply never fires, as today.)*

The hint is appended to the error text, worded like
`plugin declares API 4.2, this app provides 4.1`. **When it applies** (refined in implementation):
- **A declared minor higher than the host's:** always, on any failure of either kind.
- **No `requiresApi`:** only for a **drift-shaped** failure — a `MissingMemberException`,
  `TypeLoadException`, `ReflectionTypeLoadException`, `FileLoadException` or `BadImageFormatException`
  — and then worded `plugin declares no requiresApi; if it uses API members added after 4.0, declare
  one (this app provides 4.1)`. The first draft said "or the attribute is absent" for every failure.
  That would have attached a version note to every ordinary script syntax error in every legacy
  plugin, and an absent declaration is the 4.0 baseline, which can never exceed the host's version, so
  it isn't information in that case.
- **A declared minor at or below the host's:** never; the version can't be the explanation.

It is never shown for a failure with no version angle. (A major mismatch never reaches this path; it
is blocked at load, above.) An invoke-time failure that gets a hint reaches callers as a
`PluginApiMismatchException` wrapping the original, so every place that already formats
`result.Error?.Message` shows it without being edited.

### 3.3 Per-hook `Since`

Each hook in `PluginHooks.ValidHooks` gains a `Since` minor (all 17 CE-parity hooks = `4.0`; the four
new hooks = `4.1`). This is **metadata only**: it feeds the hint text and the docs. It is *not*
enforced — a command bound to a `4.1` hook with no `requiresApi` is not broken, because on a host
that has the hook it simply works, and on an older host an unknown hook name never fires.

### 3.4 Plugin manager

The declared `requiresApi` appears in `PluginScreen`'s detail pane as plain information. No warning
badge, no alert, when a plugin that runs fine declares a higher **minor** than the host provides. A
failure's hint, and a major-mismatch block reason, show wherever `LoadError`/`IsBroken` reason text
already shows.

## 4. Slice 2 — Activity Center reporter

### 4.1 Layering

`IActivityService` lives in `Paperbunkr.App`; plugins only see `Paperbunkr.Plugins.Abstractions`. So:
a new small interface in Abstractions, an adapter in `Paperbunkr.App/Plugins/` that wraps
`IActivityService`, exposed as **`IPluginEnvironment.Activity`** of type **`IPluginActivity`**. Final
names may shift in implementation as long as the shape holds.

### 4.2 Shape

Mirrors the existing job vocabulary — `StartJob(title, cancellable)` → a handle with
`Report(done, total, detail)`, `Report(detail)`, `Succeed(summary)`, `Fail(summary)`, and a
`CancellationToken`. Plus `RaiseAlert(severity, title, detail, dedupeKey)`.

- **Jobs and alerts only.** No upkeep row (`RegisterUpkeep`) for plugins. A plugin can never run a
  job without a visible row.
- **Alert severity:** `Info` / `Warning` / `Error`, mapped one-to-one onto the existing
  `ActivityAlertSeverity` (`Info`, `Warning`, `Error` — verified in
  `Paperbunkr.App/Models/ActivityAlertSeverity.cs`).
- **Toast policy is host-controlled** and not exposed to plugins, so a noisy plugin cannot spam
  toasts.
- Available to **both tiers** — it is non-UI, the same reasoning that put `IMetadataGraph` on
  `IPluginEnvironment`. The existing `ShowToast` path stays as-is.

### 4.3 Attribution and history

- Append `ActivityJobKind.Plugin`. `ActivityTrigger.Plugin` already exists and is used for the
  trigger. Enum values are stored as strings (`HasConversion<string>()`, per the note in
  `PaperbunkrDbContext`), so appending is safe and needs no migration; the implementer must still
  confirm `ActivityRun.Kind`'s column length fits `"Plugin"` and that the conversion covers that
  column, since that was inferred from the file's stated policy rather than read line by line.
- Job titles are prefixed with the plugin's name so it is clear who is running.
- Plugin jobs persist into the same `ActivityRun` history and pruning as every other job.

### 4.4 As implemented (2026-09-20)

Plan: `docs/superpowers/specs/2026-09-20-plugin-api-4-1-slice2-activity-reporter-plan.md`.
- **A native-only activity API already existed** — `INativePluginEnvironment.StartActivityJob` returning
  `IPluginActivityHandle` (`Paperbunkr.Plugins.Abstractions.Native`), which lets a full-trust plugin pick
  any `ActivityJobKind`. It is **unchanged**. The new `IPluginActivity.StartJob` **reuses that same
  handle type** rather than adding a second one, and the handle gained `Report(string detail)`.
- `IPluginActivity` / `PluginAlertSeverity` live in namespace `Paperbunkr.Plugins`. `.csx` scripts now
  also import `Paperbunkr.Plugins.Abstractions.Native` so the handle type is nameable.
- `ActivityRun.Kind` **was** verified: `HasConversion<string>().HasMaxLength(32)`, so `"Plugin"` fits and
  no migration is needed. `ActivityJobKind.Plugin` is appended; the Activity Center gets a puzzle-piece
  icon for it and a "Plugin" history-filter option.
- **Toast policy is fixed to failures-only** by the host (a successful plugin job does not toast).
- **Alert dedupe keys are namespaced** `plugin:<key>:<dedupeKey>`, so a plugin can never suppress another
  plugin's or the app's alerts; with no key each call is its own alert. Titles are prefixed with the
  plugin's name.
- The environment is shallow-cloned per command, so `Activity` is **built per access** from the current
  clone's `PluginKey`; a cached adapter would stay bound to the original clone's key. The plugin's
  display name comes from `PluginEngine.PackageNames` (manifest `name`), falling back to the key.
- `PluginApi.Current` is now **`4.1`**.

## 5. Slice 3 — Domain event hooks

Four new hooks, added to `PluginHooks` and `ValidHooks` and mapped in `PluginGlobalsTypeMap`, each
with its own typed globals class (the existing pattern).

| Hook | Since | Fires |
|---|---|---|
| `BookRead` | 4.1 | On every `ReadingEventRecorder.RecordFinished` |
| `LibraryScanCompleted` | 4.1 | Once per completed scan, including scheduled scans |
| `MissingFileDetected` | 4.1 | Once per item when confirmed missing |
| `ReadingListChanged` | 4.1 | Once per user action or import, coalesced |

### 5.1 Dispatch semantics (all four)

- **Notification-only**: return values are ignored.
- **Fire-and-forget, `async`, off the calling thread**, so a slow plugin never blocks reading or
  scanning.
- **A `CancellationToken` is passed to every invocation**, exposed as a `CancellationToken` property
  on `PluginGlobals` (non-`required`, default `None`, so existing globals and their tests compile
  unchanged). A script or native plugin reads it from its globals.
- **Per-command timeout: 30 s**, implemented by cancelling that token. Cancellation is
  **cooperative**: a plugin that ignores the token or blocks synchronously still holds its thread,
  so the token alone does not protect the thread pool (next bullet does).
- **Bounded serial queue per command.** Each command runs one domain-hook invocation at a time, with
  up to **16** further events queued behind it. When the queue is full the **oldest** queued event is
  dropped and one deduped Activity alert (§4) reports it. A command whose invocation is still
  running **after its timeout** is treated as **hung**: it gets no new invocations until that call
  returns, its queue keeps filling and dropping as above, and one deduped alert names it. So a hung
  plugin costs at most one blocked thread and a bounded queue, not an unbounded number of threads.
  (Queue, not coalesce: `BookRead` and `MissingFileDetected` events are each meaningful, so dropping
  the newest to keep the latest would lose reads.)
- **Failure** is captured in a `PluginInvocationResult` error, and raises one **deduped Activity
  alert per command per session** (via the §4 machinery), not one per failed event.
- Only enabled, non-broken commands run (existing `PluginEngine.GetCommands` behaviour).

### 5.2 Emit mechanism

The recorder and the other emitting services stay plugin-unaware. `PluginHostService` subscribes to
events and owns dispatch for all four hooks. For `BookRead` this needs a **new event on the recorder
that carries the finished `ReadingEvent` row** — the existing `ReadingEventRecorded` event takes no
arguments, so it cannot serve. The finished-threshold decision lives in the three reader
view-models, not the recorder, so this hook needs no threshold rule of its own.

The other three hooks' emit points, located in the codebase (there is **no aggregated repository**;
every service and view-model opens its own short-lived `PaperbunkrDbContext` and writes directly):

- **`LibraryScanCompleted`** — one chokepoint: the scanner behind
  `_libraryScanner.ScanAllAsync`, whose result already carries `IssuesAdded`, `SeriesTouched` and
  `AddedIssueIds`. Emit from the scanner (or a wrapper around it), **not** from
  `PreferencesScreenViewModel.ScanNow`, or scans started by the scheduled task and the folder
  watcher would be missed. The scanner does not track *updated* issues — see §5.3.
- **`MissingFileDetected`** — one chokepoint: `LibraryHealthService` (~lines 82–87) is the only place
  that increments `MissingVerificationCount` and checks it against
  `LibraryHealthConfirmedMissingThreshold` and `!MissingAcknowledged`.
- **`ReadingListChanged`** — **genuinely scattered today**: direct `ReadingListItems.Add`/`Remove` in
  `ReadingScreenViewModel` (at least seven sites), `LibraryScreenViewModel` (~line 3036),
  `ArcReadingListBuilder` (Data layer), `LibraryDeletionHelper`, and the daemon's import. Resolved by
  consolidating them behind one `ReadingListManager` — see §5.4.

### 5.3 Payloads

Split rule: hooks about **live items** pass the entity; hooks whose subject may be **gone** by the
time the plugin runs pass lightweight records only.

- **`BookRead`** — one hook, one globals class: `ItemType`, `ItemId`, `SeriesId`, `PagesRead`,
  `FinishedUtc`, and nullable `Issue? Issue` / `Book? Book` with exactly one set according to
  `ItemType`. Mirrors `ReadingEvent`'s own shape (comics and manga are `Issue`, novels are `Book`,
  no shared base). Fires on **every** finish, so a re-read counts; a plugin wanting once-only can
  dedupe itself using `FinishedUtc`.
- **`LibraryScanCompleted`** — `FolderPaths` (the watched folders that actually exist and were scanned),
  `AddedCount`, `UpdatedCount`, `SeriesTouched`, `Duration`, `AddedItemIds`, `UpdatedItemIds`. Ids are
  lightweight `int` lists so a plugin never has to query the whole graph and diff it (even a huge first
  scan is a few hundred KB of ints); `AddedItemIds` is the scan result's own `AddedIssueIds`.
  **Correction to an earlier draft of this spec:** it said the scanner already counts updates. It doesn't —
  the `IssuesUpdated` counter belongs to `SyncMetadataAsync`, a different pass. `ScanAll` returns only
  added counts and ids. Per the decision not to expand the scanner's scope, **`UpdatedCount` is `0` and
  `UpdatedItemIds` is an empty reserved collection**, documented as such on the globals type. The earlier
  "missing / removed counts" were also dropped: the scanner doesn't compute them, and a missing file has
  its own hook. **Comic/manga scans only** — Books are scanned by a separate service
  (`BookFolderScanService`) and don't fire it. Raised from `LibraryFolderScanner.ScanAll`, the one
  chokepoint Scan Now, the scheduled task and the plugin API's `ScanFolders` all share; a live-watch or
  drag import (`ImportNewFilesAsync`) is not a scan, and a cancelled or failed scan raises nothing.
- **`MissingFileDetected`** — `ItemType` (always `Comic` today), `ItemId`, `FilePath`, `Title` (the issue's
  title, else the file name). Announced on the Verify pass where `MissingVerificationCount` **crosses**
  the confirmed-missing threshold (previous count below it, new count at or above it) and the issue
  isn't acknowledged — not on a single failed check (a disconnected drive would be noisy) and **not again
  on later passes** while it stays missing. A file that reappears (count resets) and goes missing again
  crosses again and is announced again. Raised after the counts are saved.
- **`ReadingListChanged`** — `ListId`, `ListName`, `Kind` (flags), `AddedIssueIds`, `RemovedIssueIds`;
  see §5.4.

### 5.4 `ReadingListChanged`: consolidate writes behind a `ReadingListManager`

*Revised three times. First draft: a central notifier every write site calls, plus a source-scanning
guard test. Rejected in external review as brittle — text-scanning C# for `ReadingListItems.Add` is
easily defeated by aliases, refactoring and line breaks, and a Roslyn analyzer would itself have to
guess what "adjacent to a notifier call" means and would need scoping so it did not fire on the many
tests that write reading-list rows directly. Second draft: a manager that owns its own `SaveChanges`.
That doesn't survive contact with the code: `LibraryDeletionHelper`, `ArcReadingListBuilder` and
`AddIssuesToReadingList` all work inside a **caller-owned context whose `SaveChanges` belongs to a
larger unit of work**. What shipped is below.*

**Mechanism.** `ReadingListManager` (`Paperbunkr.Data/ReadingLists`) mutates the caller's context and
**stages** an announcement with `PaperbunkrDbContext.RunAfterSave(Action)`. The context runs its staged
actions **only after a successful `SaveChanges`** (sync and async) and drops them if the context is
disposed unsaved or the save throws. So the caller keeps ownership of the save, a failed or abandoned
unit of work announces nothing, and a later save on the same context doesn't re-announce. The staged
action raises on `LibraryEvents` (`Paperbunkr.Data/Events`), the app-wide hub the scanner, Library
Health and the manager all raise on and `PluginHostService` subscribes to. Producers take an optional
`LibraryEvents` and default to `LibraryEvents.Default` (the same convention as their `contextFactory`
parameters); a throwing subscriber never reaches a producer.

**Operations** (all caller-saves; one operation = one announcement; a 300-issue import is one event):
`AddIssues` (dedupes, appends after the last order, bumps `UpdatedAt` only if something was added),
`RemoveItems`, `MoveItem`, `RemoveIssueFromAllLists` (one `Removed` per affected list — for issue
deletion), `RecordCreatedWithItems` (a list created with its items: `Imported` for CBL/CSV/arc/continuity,
`Added` for a list built from a story event) and `Record` (a bespoke compound change).

**Sites migrated:** `ReadingScreenViewModel` (add-selected, drop-import, add-series, add-one, remove-
selected, remove-one, reorder), `LibraryScreenViewModel.AddIssuesToReadingList`,
`NewReadingListViewModel.CreateFromEvent`, `LibraryDeletionHelper.RemoveIssue`, and in `Paperbunkr.Data`
the CBL, CSV, continuity and arc builders plus `ArcReadingListBuilder.RefreshAsync` (which reports one
compound `Added | Removed | Reordered` change). A final audit found no remaining direct
`ReadingListItems.Add/Remove` outside code that announces its own change.

**Payload.** `Kind` is a **flags** enum (`Added`, `Removed`, `Reordered`, `Imported`) because an arc
refresh can do several at once; `AddedIssueIds` and `RemovedIssueIds` carry the ids (empty for a pure
reorder). Ids only — the items may be gone by the time a plugin runs.

**Deliberately not events:** relinking a placeholder to a real issue (the membership set is unchanged),
and creating/renaming/deleting a whole list — a blank new list announces nothing until items go into it.

**Small behaviour changes this caused** (each an improvement, none tested against by an existing test):
adding an issue already in the list is now a no-op instead of creating a duplicate row; `UpdatedAt` is
bumped only when something changed; new items append after `max(SortOrder) + 1` everywhere (one site
used `Count`, which collides when there are gaps).

**The daemon** runs in the app's process (`Paperbunkr.Daemon` is a class library the app references and
`AcquisitionActivityBridge` consumes its events in-process; grep evidence, not a full read), so its arc
follow refreshes — which go through `ArcReadingListBuilder.RefreshAsync` — reach the same hub. If it is
ever also launched as a separate process, that process's changes cannot reach the app's in-process hub.

**The residual weakness, stated plainly:** nothing *enforces* that a future contributor uses the manager;
a new direct `ReadingListItems.Add` compiles and silently skips the hook. The manager's XML doc names it
as the only sanctioned write path. If that bites, the backstops are an EF `SaveChangesInterceptor` or a
Roslyn analyzer (roadmap backlog item 20).

## 6. Slice 4 — Settings schema

### 6.1 Declaration

A `<Settings>` element under `<Plugin>`, both tiers:

```xml
<Settings>
  <Setting key="api-url" label="API URL" type="text" default="https://example.com" description="…"/>
  <Setting key="mode" label="Mode" type="choice" default="fast">
    <Choice value="fast" label="Fast"/>
    <Choice value="thorough" label="Thorough"/>
  </Setting>
  <Setting key="limit" label="Limit" type="number" default="10" min="1" max="100"/>
</Settings>
```

`type` is one of `toggle | text | choice | number | secret`. Keys are unique per plugin. A malformed
entry (unknown type, duplicate key, `choice` with no choices, `default` not among the choices)
marks the plugin broken with a reason.

### 6.2 Storage and reads

Values still live in the existing per-plugin string store (`IPluginConfig.GetSetting`/`SetSetting`);
the schema adds typing and rendering only.

- For a **declared** key with no stored value, `GetSetting` returns the declared default.
  Undeclared keys behave exactly as today.
- **The host is the sanitization layer** (revised after the 2026-09-20 external review; the first
  draft returned invalid values as stored, pushing validation onto every plugin author). If a stored
  value violates its declared schema — a `choice` value not among the choices, a `number` that is
  unparseable or outside `min`/`max`, a `toggle` that is not a boolean — `GetSetting` returns the
  **declared default** instead, so a plugin never sees a schema-violating value. The **raw stored
  string is preserved and never rewritten**: the settings UI still shows it and flags it inline so the
  user can correct it. `text` and `secret` have nothing to validate. Orphaned keys (no longer in the
  schema) are left alone.
- **Secrets are encrypted at rest** (also revised: the first draft stored them in plain text). A
  `type="secret"` value is protected with **Windows DPAPI** — `ProtectedData.Protect` with
  `DataProtectionScope.CurrentUser` — before it is written to the string store, stored with a
  distinguishing prefix (e.g. `dpapi:` + Base64), and decrypted on read. `SetSetting`/`GetSetting`
  keep their string signatures; the host knows which keys are secret from the schema, so undeclared
  keys behave exactly as today, and a plugin sees plaintext on `GetSetting` and never handles
  ciphertext. Consequences the docs must state:
  - It protects **data at rest** (a copied settings file or database), **not** against code running
    as the same user — a native plugin is full-trust and can call `Unprotect` itself.
  - Ciphertext is bound to **the same Windows user on the same machine**. A restored backup or a
    moved profile will not decrypt; that reads as *unset* (the declared default, normally empty) and
    the user re-enters it. Any future settings export/import feature must account for this.
  - `ProtectedData` is **Windows-only**. The app is Windows-first today; encryption goes behind a
    small protector abstraction so a non-Windows build would need a per-platform implementation, and
    on a platform with none, secret settings fail closed (unavailable, flagged in the UI) rather than
    silently falling back to plain text.
  - A value that fails to decrypt (corrupt, wrong user) is treated as unset, never thrown to the
    plugin.
  - **Reuse what exists:** `Paperbunkr.Data/Credentials/CredentialStore.cs` already wraps
    `ProtectedData.Protect`/`Unprotect` with `DataProtectionScope.CurrentUser` (found while
    implementing slice 1). The implementer reads it first and builds the protector abstraction on it
    instead of writing a second DPAPI wrapper.
- **No change-notification callback** for now. Script commands are short-lived and read settings on
  each call; native modules can re-read. Add an event only if a real plugin needs one.

### 6.3 One source of truth

A plugin that declares a `<Settings>` schema **and** implements `INativePluginSettingsUi` (native)
or has a `ConfigScript` command (script) is a **load error** — broken with a reason — so there is
exactly one settings definition per plugin.

### 6.4 Rendering

Reuse the existing **`PluginHostService.OpenPluginSettingsAsync` overlay path**, so schema plugins
get the same "Settings" entry point native ones have and no new inline editing surface is built into
`PluginScreen`'s list-heavy detail pane. It appears only when a schema exists. Control constraints:

- `choice` fields use **`Controls/SuggestBox`, never `ComboBox`** (the app-wide dropdown-freeze
  migration).
- Changes save on edit; validation errors show inline.
- Removing/closing anything from inside a row's own event follows the "defer one dispatcher tick"
  rule in `CLAUDE.md`.
- New XAML uses **theme resources**, not hard-coded hex. `PluginScreen.axaml` currently hard-codes
  `#4CAF50` / `#E06060` for its status dots; do not copy that.

Load the `avalonia` skill and run `avalonia-pro-max/review-checklist` before calling this slice's UI
done, per `CLAUDE.md`.

### 6.5 As implemented (2026-09-20)

Plan: `docs/superpowers/specs/2026-09-20-plugin-api-4-1-slice4-settings-schema-plan.md`.
- **DPAPI reuse.** `CredentialStore`'s crypto was factored into a shared `Paperbunkr.Data.Credentials.DpapiSecrets`
  (`Protect` / `TryUnprotect` / `IsProtected`, `dpapi1:` + base64, `CurrentUser`); `CredentialStore` now delegates to
  it with its own entropy (behaviour identical - its existing tests are the regression check). Plugin settings use
  the same class with **their own entropy** (`Paperbunkr.PluginSettings.v1`), so ciphertext isn't interchangeable
  between the two stores. `DpapiSecretProtector` (App) adapts it to the plugin-facing `ISecretProtector`.
- **Where the logic lives.** `PluginSettingsSchema` (`Paperbunkr.Plugins`) is pure: `Parse` (declaration),
  `Validate` (a value), `Resolve` (what a plugin sees). `PluginSettingsAccess` (App) applies it to the existing
  `PluginSettingState` table and is shared by the plugin-facing `GetSetting`/`SetSetting` and the overlay.
  Storage is unchanged, so a plugin with no schema behaves exactly as before.
- **Blocking.** A malformed `<Settings>`, or a schema plus a `ConfigScript` command, blocks the plugin at discovery
  through the same mechanism as a `requiresApi` block (reason shown in the Plugin screen). The native "schema **and**
  `INativePluginSettingsUi`" conflict can only be checked after the module loads, and `INativePluginSettingsUi` lives
  in the Avalonia-dependent project the engine doesn't reference - so `PluginHostService` checks it after discovery
  and calls the new `PluginEngine.RejectPackage`. Consequence worth knowing: the offending native module *has been
  loaded and initialised* before it's rejected.
- **What a plugin can write.** `SetSetting` from a plugin is not validated (a plugin can write a schema-violating
  value); it simply reads back as the default. Validation on the write path is the overlay's job, where the user is.
- **Secrets.** Encrypted on write, decrypted on read; one that won't decrypt reads as unset; a secret found stored as
  plain text (written before the schema existed) is encrypted in place on first read; where DPAPI isn't available a
  secret can't be written (`InvalidOperationException`) and the overlay shows those rows read-only with one note.
  Documented limits: protects data at rest only, bound to one Windows user on one machine, and a full-trust native
  plugin can unprotect its own values.
- **The overlay.** `PluginSettingsSchemaView` + `PluginSettingsSchemaViewModel`, opened by the existing
  `OpenPluginSettingsAsync` path (and the Plugin screen's Configure button is now also shown for schema plugins). Each
  setting is a shared `SettingsRow`; a choice is a `SuggestBox` (never a `ComboBox`); a number is a `TextBox` with the
  `TextSpinner` behaviour and `ClampTyped`; a secret is a masked `TextBox`; a toggle is a `ToggleSwitch`. Edits save
  immediately when valid; an invalid edit is refused with an inline message and not saved; an invalid value *already
  stored* is shown as it is, flagged with what the plugin is using meanwhile, and never rewritten. Theme resources
  only, no hard-coded colours, an `AutomationProperties.Name` on every input and a polite live region on errors.
- **Deliberately not built:** a change-notification callback (spec §6.2), reset-to-default buttons, and grouping/
  ordering controls beyond declaration order.

## 7. Python and script plugins

All four pieces are non-UI on the plugin's side and work for `.csx` and `.py` alike: the reporter is
on `IPluginEnvironment`, the hooks are bound by `plugin.xml` (`.py` needs `method=` as today), and
the schema is declarative. What Python still lacks is native UI surfaces — out of scope (§9).

## 8. Testing and docs

- Each new hook gets a `HookCoveragePluginTests` case backed by a sample-plugin fixture that
  actually fires it.
- The reporter and the settings schema each get sample-plugin coverage; the `PythonHello` sample
  gains a reporter call.
- Versioning tests cover: absent attribute = `4.0`; a higher declared **minor** that runs fine is
  left alone; the same plugin failing produces the hint; a **major mismatch in either direction is
  blocked at load** (broken with the reason, native `LoadPlugin` never called, script never
  compiled).
- Dispatch tests cover: the `CancellationToken` is tripped at the 30 s timeout (use a short test
  timeout); a hung command gets no new invocations; the 16-slot queue drops the oldest and raises
  one deduped alert; one failing command does not stop others.
- Settings tests cover: invalid stored `choice`/`number`/`toggle` returns the default while the raw
  string is preserved; secret round-trip through DPAPI (Windows-only test, skipped elsewhere); a
  ciphertext that will not decrypt reads as unset; an undeclared key is untouched.
- `ReadingListManager` tests (§5.4): each operation raises exactly one change with the right `Kind`
  and ids; a 300-item import raises one; a failed save raises none; issue deletion notifies
  `Removed`. **No source-scanning guard test** — rejected as brittle.
- The plugin-developer wiki page documents the version-hint behaviour, the hooks, the reporter and
  the schema. `docs/Paperbunkr-Roadmap.md` is updated when each slice ships, by hand, with what was
  verified — not just what the commit claims.
- On-screen verification of the settings overlay and of alerts in the Activity Center is a
  separate, explicit step; automated tests alone do not cover it.

## 9. Non-goals (deferred — tracked in `docs/Paperbunkr-Roadmap.md`)

- **Plugin-registered data sources**: metadata provider, cover provider, extensible arc/CBL lookup
  source. `IMetadataProvider` lives in `Paperbunkr.Data`, not plugin-facing today.
- **UI surfaces**: issue-detail panel, Library toolbar action, context-menu contributions, custom
  Insights widget.
- **Smart-list custom fields/operators.**
- **Capability gates**: network-host manifest, scoped `IFileSystem` facade, `IMetadataWriter`
  batch/dry-run API.
- **Developer experience**: dev mode + hot reload, scaffolding command, `.d.cs` reference stub,
  reusable `Paperbunkr.Plugins.Testing` package.
- **Inter-plugin**: plugin-to-plugin service registry.
- **Other domain hooks**: `SeriesStatusChanged` (tracker sync has many write paths),
  `ContinuityCompleted` / `EventCompleted` (wait for their own feature to settle),
  `ScheduledTaskRan`.
- **Native UI surfaces for Python plugins.**
- **From the 2026-09-20 external review, not adopted here** (five new ideas are in the roadmap
  backlog; the rest overlap the list above or were rejected — see that section).

## 10. Revision history

- **2026-09-20, initial draft** — from the three-round grilling pass.
- **2026-09-20, external-review revision** — (1) major version mismatch is now a hard block, minor
  stays lenient (§3.2); (2) `secret` settings encrypted with DPAPI (§6.2); (3) schema-violating stored
  values return the declared default to the plugin, raw string preserved (§6.2); (4)
  `LibraryScanCompleted` carries `AddedItemIds`/`UpdatedItemIds` (§5.3); (5) hook dispatch passes a
  `CancellationToken`, and gains a bounded per-command queue because the token alone is cooperative
  and does not protect the thread pool (§5.1); (6) emit points located (§5.2).
- **2026-09-20, second review pass** — the source-scanning guard test and the interim central
  notifier are replaced by a `ReadingListManager` that consolidates all production reading-list
  writes (§5.4); `LibraryScanCompleted` documented as comic/manga-only, with `UpdatedItemIds`
  populated only if cheap, else an empty reserved collection (§5.3); the daemon verified as
  in-process, so its changes do fire `ReadingListChanged`. A Roslyn analyzer was considered and
  deferred.
