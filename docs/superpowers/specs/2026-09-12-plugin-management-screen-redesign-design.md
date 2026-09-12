# Plugin Management Screen Redesign — Design

*Follows: docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md,
docs/superpowers/specs/2026-09-11-cluster-library-manager-design.md (first real native-tier
plugin, ClusterLibraryManager, is the concrete case this redesign was validated against).*

## 1. Problem

The Preferences → Plugins screen (`src/Paperbunkr.App/Views/PluginScreen.axaml` +
`PluginScreenViewModel.cs`) predates the native tier. Surveying it while installing
ClusterLibraryManager surfaced real, verified defects, not style nits:

1. **No entry point exists for a native plugin's settings UI at all.** `INativePluginSettingsUi.
   CreateSettingsView` has zero callers anywhere in `src/Paperbunkr.App` (grepped). A plugin like
   ClusterLibraryManager, whose `OrganizerScraperPlugin.CreateSettingsView` returns a real
   `SettingsRootView` (API key, scrape-field toggles, organizer profiles, token editor), cannot be
   configured from inside the app at all.
2. **A native plugin that throws while loading vanishes silently.** `PluginEngine.DiscoverNative`
   ([PluginEngine.cs:133](../../../src/Paperbunkr.Plugins/PluginEngine.cs)) catches and discards
   the exception with no record kept anywhere. The Packages panel still lists the package as
   plainly "Installed" — indistinguishable from a package that loaded fine and simply registered
   zero commands. This is the concrete mechanism behind "sometimes misleading."
3. **The loaded `INativePluginModule` instance is discarded.** `PluginLoadContext.LoadPlugin`
   already returns `(INativePluginModule Module, IReadOnlyList<NativeCommand> Commands)` —
   `PluginEngine.DiscoverNative` throws the module away (`(_, commands) = ...`). There is currently
   nowhere to call `CreateSettingsView` on, even if a UI existed to call it from.
4. **Package identity is three uncoordinated systems that happen to agree by luck of folder
   naming**, not by a real key:
   - `PackageManager.Package.Name` ([PackageManager.cs:126](../../../src/Paperbunkr.Engine/PackageManager.cs)) —
     from `package.ini`'s `Name`, falling back to a folder-name heuristic (`FileToName`).
   - `Command.PluginKey` — from `plugin.xml`'s root `<Plugin key="...">` attribute
     ([PluginManifest.cs:17-18](../../../src/Paperbunkr.Plugins/PluginManifest.cs)).
   - `PluginCommandRowViewModel.Package` (the "Package" column shown per command row) — read live
     from a *third* place, `package.ini`'s `Name` again but independently
     ([PluginCommandRowViewModel.cs:46-48](../../../src/Paperbunkr.App/ViewModels/PluginCommandRowViewModel.cs)),
     falling back to the literal string `"Other"` if no `package.ini` exists.
   - Confirmed concretely: ClusterLibraryManager ships no `package.ini` (native packages have no
     reason to — `plugin.xml` already carries `name`/`key`). Its commands' "Package" column
     currently shows **"Other"**, even though the Packages panel above correctly shows "Cluster
     Library Manager". Same plugin, two different displayed identities, in the same screen.
5. **Commands are grouped by hook, not by plugin**, so one plugin's own commands (e.g.
   ClusterLibraryManager's Startup + two Library-hook commands) scatter across three separate
   list sections, with no visual grouping tying them back to the plugin that owns them or the
   Packages-panel row that installed them.
6. **No per-package enable/disable** — only per-command, via the existing
   `PluginCommandState` table (`PluginHostService.SetCommandEnabled`,
   [PluginHostService.cs:224-245](../../../src/Paperbunkr.App/Plugins/PluginHostService.cs)).
7. **Script-tier's existing per-command Configure gear** (`PluginCommandRowViewModel.
   OpenConfigure`) is a real, working, separate mechanism from native settings UI — the redesign
   needs to make both reachable through one consistent visual language without conflating their
   genuinely different scopes (native settings UI is package-wide; a script's Configure script is
   per-command, and one package can have several).

## 2. Goals

- One place to see, per installed plugin: identity, health (loaded OK / broken / restart-pending),
  its own commands, and a way to configure it (native settings UI or script-tier Configure,
  whichever applies) — all as one visible unit.
- Fix the underlying identity split so the Packages panel and the commands list are provably the
  same plugin, not a name-string coincidence.
- Surface native load failures instead of swallowing them.
- Master enable/disable per package, layered on the existing `PluginCommandState` mechanism (no
  new persisted state).
- Preserve every existing behavior that already works: script-tier install/remove (no restart),
  native install/remove (restart-to-apply, unchanged), per-command enable toggle, manual Run for
  `CreateBookList`-hook commands, per-command Configure for script-tier.

## 3. Non-goals

- No change to script-tier's Configure mechanism itself (still invokes a paired `.csx`/`.py`
  ConfigScript the same way) — only where its trigger is displayed.
- No `INativePluginModule` shutdown/stop-hook. Disabling a native package gates future command
  invocation immediately; it cannot retroactively halt something the module already started in
  `Initialize` (e.g. ClusterLibraryManager's automation `Timer`) — that still requires a restart,
  consistent with the native tier's existing install/remove model. Adding a real stop lifecycle is
  separate future work if a plugin ever needs it.
- No change to the install/remove flow itself (file picker, overwrite confirmation, staged native
  install) — only where those actions live on screen.

## 4. Architecture

### 4.1 Identity fix: read `plugin.xml`'s root key/name as the single source of truth

`plugin.xml`'s root `<Plugin key="..." name="...">` already exists for every package, script-tier
included (pre-dates v4 — confirmed in `PluginManifest.cs`). `PackageManager.Package` gains a `Key`
property, read the same way `IsNativeTier` already is (a raw `XDocument` peek — `PackageManager`
deliberately has no dependency on `Paperbunkr.Plugins`, per its own existing comment, and this
doesn't change that):

```csharp
public string Key { get; private set; } = string.Empty;

private static string DetectKey(string packagePath)
{
    string manifestPath = Path.Combine(packagePath, "plugin.xml");
    try
    {
        XDocument manifest = XDocument.Load(manifestPath);
        string? key = manifest.Root?.Attribute("key")?.Value;
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }
    }
    catch
    {
        // fall through to the folder-name fallback below
    }

    // Never string.Empty (external review round 1) - and never just the bare folder name either
    // (external review round 3): Paperbunkr's own Install-Package flow always names the install
    // folder after the source file, never a generic internal path segment
    // (PackageManager.GetPackagePath -> Path.Combine(..., package.Name), package.Name from
    // FileToName(sourceFile) - verified, no "dist"/"Release"-shaped folder is ever produced by it).
    // But the empty-state hint text already documents a second, user-driven path - "drop a plugin
    // folder into %AppData%\Paperbunkr\plugins" - where the user picks the folder name themselves,
    // and two such folders (from different plugin authors) could plausibly share a generic name.
    // A hash of the full absolute path is trivially unique across the whole plugins root, with no
    // heuristic list of "generic-sounding" names to guess and maintain:
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Path.GetFullPath(packagePath).ToUpperInvariant())))[..16];
}
```

(Note: this fallback only ever fires for a package with no valid `plugin.xml` - and without one,
`PluginEngine.Discover`'s own `Directory.EnumerateFiles(pluginsRoot, "plugin.xml", AllDirectories)`
never finds anything to discover for that folder at all, so it produces zero commands and no
`NativeLoadResults` entry regardless of what its `Key` resolves to. This fallback exists purely so
the Packages panel can still list and remove a manifest-less folder - existing CE-parity behavior,
untouched by this redesign - not to correlate it against anything else. Sidebar selection in §4.4
is by list-item object reference (`SelectedItem`), not a dictionary keyed on `Package.Key`, so even
two manifest-less packages colliding on the same fallback key select and remove independently.)

`Package.Name` itself now prefers `plugin.xml`'s `name` attribute (same read), falling back to
`package.ini`'s `Name` then the existing `FileToName` folder heuristic only if neither manifest
attribute is present (defensive fallback for any pre-v2 package with no `plugin.xml` name — should
not occur in practice, kept for safety, not because a real case is expected).

`PluginCommandRowViewModel.Package` (the per-row "Package" column) is deleted as a separate
concept — the redesigned screen groups commands under their owning package already (§4.4), so the
column has no reason to exist once every command is already shown under its real package.

Result: `Package.Key` and `Command.PluginKey` are now the same value, read from the same
attribute, for every installed plugin. That's the correlation key everything else in this design
uses.

### 4.2 `PluginEngine` retains native load outcome, not just commands

`DiscoverNative` currently discards the returned module and swallows any exception. Both become
first-class, keyed by `pluginKey`:

```csharp
public sealed record NativePluginLoadResult(INativePluginModule? Module, string? LoadError);

private readonly Dictionary<string, NativePluginLoadResult> _nativeLoadResults = new();
public IReadOnlyDictionary<string, NativePluginLoadResult> NativeLoadResults => _nativeLoadResults;
```

`DiscoverNative` populates one entry per native package it attempts, on both the success and
`catch` paths — the `catch` path now records `ex.Message` instead of doing nothing. Script-tier
packages simply have no entry (native-only concept). Writes use the indexer
(`_nativeLoadResults[pluginKey] = result`), never `Dictionary.Add` - `Add` throws
`ArgumentException` on a duplicate key, an indexer-set just overwrites, so this alone cannot crash
the engine (external review round 2 described this as a crash risk; it isn't one as specified, and
`PackageManager`/§4.1's `Package.Key` lives in a plain `List<Package>` with no dictionary at all -
`GetPackages()` never indexes by key, before or after this design, so that half of the claim
doesn't apply either).

What the indexer-overwrite *would* do silently, though, is exactly the kind of silent data loss
this whole redesign exists to stop: if a user copies a plugin's install folder as a backup (both
folders then share the identical `plugin.xml` key), the second one processed would quietly
overwrite the first's healthy `NativePluginLoadResult` with its own - one of the two native modules
effectively vanishes from `NativeLoadResults`, indistinguishable from having never loaded, with no
signal anywhere. `DiscoverNative` therefore checks before writing: if `pluginKey` is already present
in `_nativeLoadResults`, the *second* package gets `LoadError = $"Duplicate plugin key '{pluginKey}'
- a plugin with this key was already loaded from {existingPath}."` instead of overwriting the
first's real result. The first-discovered package is unaffected. (Commands already have equivalent,
pre-existing protection one level down - `_commands.Any(c => c.Key == cmd.Key)` at
[PluginEngine.cs:125](../../../src/Paperbunkr.Plugins/PluginEngine.cs) already first-wins-skips a
second package's identically-keyed commands today, silently; this native-load-result guard is the
same scenario at the package level, made visible instead of silent, consistent with §4.3's health
model.)

### 4.3 Package-level health

A small computed status, not new persisted state:

- **Broken** — a native package with a `NativeLoadResults` entry whose `LoadError` is non-null, OR
  a package (either tier) whose discovered commands are all `IsBroken` (or it discovered zero
  commands at all, while other same-tier packages in the same run did produce commands — i.e. it
  isn't just "a plugin with no commands by design", which script-tier already allows for a
  config-only package). Shows the error/compile-error text inline.
- **Healthy** — otherwise.
- **Pending** — reuses the existing `IsPending`/`PackageType.PendingInstall` /
  `PendingRemove` concept unchanged; pending takes visual precedence over broken/healthy since the
  package hasn't actually taken effect yet.

### 4.4 Master-detail screen layout

Two-column layout (`avalonia-pro-max/layout-patterns`'s Master-Detail pattern — a plain two-column
`Grid`, matching this app's existing hand-rolled panels rather than adopting FluentAvalonia's
`NavigationView`/`SplitView` for a single sub-screen):

- **Left (sidebar, ~220px)** — a filter `TextBox` above the list (external review round 1 -
  matters once someone has more than a handful of packages installed; filters `Packages` by name,
  client-side, no new persistence), then one row per installed package: health dot, name,
  tier/pending badges. "Install Package…" action at the bottom of this column (moved from the
  current top-of-screen button). Click selects a package into the detail pane.
- **Right (detail pane)** — for the selected package:
  - Header: name, version, author, tier badge ("Native"), "Full read/write access" badge (native
    only), "Restart to apply" badge (pending only), a "Failed to load: `<message>`" banner (broken
    native only, replacing the current silent vanish) with a "Copy error" button next to it
    (external review round 1 - reduces friction reporting a stack trace to a plugin's developer;
    just `TopLevel.Clipboard.SetTextAsync(loadError)`, no new mechanism).
  - **Reload** button (script-tier packages only) — calls the existing, already-shipped
    `PluginHostService.RediscoverPlugins()` (today only called after Install/Uninstall,
    [PluginScreenViewModel.cs:126](../../../src/Paperbunkr.App/ViewModels/PluginScreenViewModel.cs)),
    then `Refresh()`. External review round 1 claimed there's "no specified mechanism to refresh
    ... without restarting the engine" after fixing a broken script via its Configure gear - that
    overstates it: `RediscoverPlugins()` already re-scans and recompiles every script-tier command
    with no app restart involved, it's just never been exposed as a user-facing action outside
    Install/Remove. The real gap is exposing a button for it, not building new file-watching
    machinery - a `FileSystemWatcher` (as also suggested) is unnecessary complexity for what's
    already a one-line call to something that exists. Native packages don't get this button - a
    native package's `LoadError` can only be re-attempted by actually reloading its
    `AssemblyLoadContext`, which is exactly what "restart to apply" already covers; a fresh
    "Reload" button that can't do anything a restart doesn't already promise would be its own new
    kind of misleading.
  - Master **Enabled** toggle — a `CheckBox` with `IsThreeState="True"` (not the `ToggleSwitch`
    per-command rows use — `ToggleSwitch` has no indeterminate concept, `CheckBox.IsChecked` is
    `bool?` and already supports it natively). Bound to a computed tri-state: `true` when every
    child command is enabled, `false` when every child command is disabled, `null` (indeterminate)
    when mixed (external review round 1: an always-unchecked mixed state was ambiguous about what
    clicking it would do).

    **Bound entirely through a ViewModel `Command`, no code-behind event handler** (external review
    round 3 correctly flagged an earlier draft's `OnMasterEnabledClick(object? sender,
    RoutedEventArgs e)` code-behind snippet as contradicting §4.5's own `MasterEnabledClickCommand`
    - fixed by actually writing it as the `[RelayCommand]` it was always described as):

    ```xml
    <CheckBox IsThreeState="True"
              IsChecked="{Binding EnabledState, Mode=OneWay}"
              Command="{Binding MasterEnabledClickCommand}" />
    ```

    ```csharp
    [RelayCommand]
    private void MasterEnabledClick()
    {
        bool next = EnabledState != true; // true only when already fully enabled; anything else
                                           // (false OR indeterminate) resolves to enabling all
        _host.SetPackageEnabled(PluginKey, next);
        Refresh(); // recomputes EnabledState from the now-updated child commands
    }
    ```

    **Binding a `Command` is necessary but, on its own, does not fix the underlying bug** - verified
    directly against `ToggleButton`'s real source
    (https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Controls/Primitives/ToggleButton.cs):
    `OnClick()` is `Toggle(); base.OnClick();` - unconditional, with no check for whether a `Command`
    is bound - and `Toggle()`'s own transition table is `true → null`, `null → false`, `false →
    true`. So `Toggle()` still runs, and still calls `SetCurrentValue(IsCheckedProperty, ...)` with
    its own (here, wrong) next value, on *every* click regardless of the bound `Command` - external
    review round 3's proposed fix ("bind `Command` directly... keep UI routing out of code-behind")
    would, by itself, still hand a click starting from indeterminate straight to `Toggle()`'s own
    `false`. What actually neutralizes that: `IsChecked`'s `Mode=OneWay` binding (kept from the
    previous round's fix, and still load-bearing, not optional) means the control's local value set
    by `Toggle()` is *transient* - `base.OnClick()` invokes the bound `Command` synchronously, in the
    same UI-thread call, immediately after `Toggle()` runs and before any render pass; `Refresh()`
    re-raises `EnabledState`'s `PropertyChanged`, and the one-way binding re-pushes the correct value
    over whatever `Toggle()` just set - all before the frame that would have shown the wrong value
    ever paints. `Mode=OneWay` and the bound `Command` are both required together; either alone is
    insufficient (`OneWay` alone leaves the trigger in code-behind, which is the actual MVVM
    complaint; `Command` alone doesn't stop `Toggle()`'s own always-wrong-from-indeterminate write).
  - **⚙ Configure** button in the header — shown only when this package's `NativeLoadResults`
    entry has a `Module` that also implements `INativePluginSettingsUi`. Clicking it calls
    `CreateSettingsView` and hosts the result through the same shared
    `NativePluginModalHostViewModel` + `MainWindow.axaml`'s `OverlayShell` every native plugin
    dialog already uses. **Dismissal already has a real answer, reused as-is, not invented here:**
    `OverlayShell` ships its own corner close button (`ShowCloseButton`, default `true`) and
    scrim-click dismiss (`AllowDismiss`, default `true` —
    [OverlayShell.cs:58,37](../../../src/Paperbunkr.App/Controls/OverlayShell.cs)), both wired to
    `NativePluginModalHostViewModel.DismissCommand`
    ([NativePluginModalHostViewModel.cs:66-71](../../../src/Paperbunkr.App/ViewModels/NativePluginModalHostViewModel.cs)).
    `PluginHostService.OpenPluginSettingsAsync` calls the existing generic `ShowAsync<TResult>`
    with a `TResult` of `object?` that's never explicitly resolved from inside the settings view -
    the only way this modal closes is that shared X/scrim, which is already there for free.
    Closing then throws `TaskCanceledException` on the awaited task exactly the way any other
    scrim-dismissed native dialog already does today (documented on `ShowAsync` itself) -
    `OpenPluginSettingsAsync` just catches `OperationCanceledException` and returns, no special
    case needed. External review round 1 raised a real gap here (no documented way to close it),
    and the fix is "the mechanism already exists, use it" - not a new `SaveCommand`/`CancelCommand`
    contract on `INativePluginSettingsUi`.

    **Explicit contract, stated once and not contradicted elsewhere (external review round 2 read
    an earlier version of this paragraph as self-contradictory - it wasn't a logic error, the
    wording just juxtaposed two different moments badly):** persistence is entirely the plugin's
    own business, and the host's Close (X/scrim) never rolls anything back, because it has nothing
    to roll back - there is no host-managed transaction. `ClusterLibraryManager`'s own
    `SettingsViewModel` already has its own `SaveCommand` that writes straight to its LiteDB store
    the moment it's clicked
    ([SettingsView.axaml:62](../../../plugins/ClusterLibraryManager/Settings/SettingsView.axaml)).
    So concretely: an edit typed but never followed by clicking that plugin's own Save button was
    never persisted anywhere, and closing the modal (by any means) simply loses that unsaved edit,
    same as abandoning any form you never submitted. An edit that *was* already Saved is already
    committed to the plugin's own storage before Close is even possible to reach - Close cannot
    "discard" it, because there is nothing left in memory to discard by that point. These are two
    different points in time, not two conflicting behaviors for the same click. A plugin author who
    wants cancel-with-rollback semantics is free to build that entirely within their own settings
    view model (keep an in-memory draft, only write on an explicit Save) - the host deliberately
    imposes no transaction boundary of its own. One plugin's settings might reasonably want
    live-apply-per-toggle (arguably true of several of `ClusterLibraryManager`'s own checkboxes
    already) and another might want batch-everything-on-Save; picking one and forcing it onto every
    future plugin author via a required interface member is prescriptive scope creep the host has
    no business enforcing.

    **Opportunistic disposal, not a mandate** (external review round 3 asked what happens to any
    event subscription/background task/lock a settings view might hold when the modal is abandoned
    via scrim/X). Verified against the actual shipped consumer: grepped
    `plugins/ClusterLibraryManager/Settings/` for `Timer`/`Subscribe`/`IDisposable`/event-handler
    registrations - none exist; its `PluginDatabase` connection is opened once in
    `OrganizerScraperPlugin.Initialize` and lives for the plugin's whole session, not per-settings-
    view, so there is nothing for the settings view itself to leak today. Requiring every future
    `INativePluginSettingsUi` implementation to also implement `IDisposable` would be a new mandatory
    member on an interface that already ships with a working consumer, for a need nothing currently
    has - real scope creep past what this redesign is fixing. Instead, purely additively:
    `NativePluginModalHostViewModel.Advance()` (called on every dismiss/complete path) checks
    `(HostedContent?.DataContext as IDisposable)?.Dispose()` then `(HostedContent as
    IDisposable)?.Dispose()` before clearing `HostedContent`. Costs nothing for a plugin that
    implements neither (`ClusterLibraryManager`, today), and closes the gap for free the moment a
    future plugin's settings view model ever does hold something real.
  - Commands list, scoped to this package only (previously scattered across hook-grouped
    sections): each row keeps its existing shape (Name, enabled toggle, Run button if
    `CanRunManually`, compile-error text if `IsBroken`) plus, for a script-tier command that has a
    paired Configure script, its existing per-command "⚙" gear inline on that row — unchanged
    behavior (`OpenConfigure`), just relocated from a hook-grouped list into this package-scoped
    one. Native and script-tier "Configure" therefore both use a gear-icon-opens-modal visual
    language, but placed at the scope each genuinely has (whole-package vs. per-command) rather
    than forced into one identical control.
  - Remove button — moves from an inline row action in the current Packages panel into this
    header (a single clear place per package, rather than a button embedded in a list row).

Empty-package-list state (`!HasPlugins`) is unchanged in spirit — same icon/copy, just now the
right-hand pane's "nothing selected" content instead of the whole screen's content.

### 4.5 ViewModel changes

- `PluginPackageRowViewModel` — sidebar row only now: health dot state, name, badges,
  `SelectCommand`. Loses the inline `DeleteConfirm` (moved to detail header).
- New `PluginPackageDetailViewModel` — header fields, `EnabledState` (`bool?`, tri-state per §4.4,
  one-way display value recomputed by `Refresh()` from child commands - never written to directly
  by the view), a `MasterEnabledClickCommand` that reads the current `EnabledState` and bulk-writes
  its negation-of-`true` per §4.4's explicit click logic, `ConfigureCommand` (visible only if
  applicable), `ReloadCommand` (script-tier only), `DeleteConfirm`, and its own
  `ObservableCollection<PluginCommandRowViewModel>` scoped to this package's commands (reuses the
  existing row VM unchanged, just filtered by `PluginKey` instead of grouped by hook).
- `PluginScreenViewModel` — owns `Packages` (sidebar) + `SelectedPackage` (drives
  `PluginPackageDetailViewModel` construction) instead of the current flat `Groups`. `Refresh()`
  rebuilds both from `_host.Engine` the same way it does today, plus now reading
  `_host.Engine.NativeLoadResults` for health/Configure-eligibility.
- `PluginHostService` gains `OpenPluginSettingsAsync(string pluginKey)` (looks up
  `NativeLoadResults[pluginKey].Module` as `INativePluginSettingsUi`, calls `CreateSettingsView`,
  hosts via the existing modal mechanism) and `SetPackageEnabled(string pluginKey, bool enabled)`
  (bulk-loops `Engine.AllCommands.Where(c => c.PluginKey == pluginKey)` through the existing
  `SetCommandEnabled`).

### 4.6 Explicitly rejected

External review round 1:

- **Plugin metadata links** (`<Url>` tag, clickable Author) — `PluginManifest`'s root element has
  no `Url` attribute and never has ([PluginManifest.cs](../../../src/Paperbunkr.Plugins/PluginManifest.cs) -
  just `key`/`name`/`tier`/`assembly`). Adding one is a real schema change unrelated to a screen
  redesign - out of scope for this pass, and speculative until a real plugin author asks for it.
- **"Required Engine Version" surface** — same issue: no version-compatibility field exists
  anywhere in the manifest or the native tier's loading path today, and Paperbunkr has no version-
  gating mechanism for a plugin to declare against. Building UI for a field that can't exist yet is
  scope creep past what this redesign needs to fix. Real follow-up if/when a plugin actually breaks
  across an engine upgrade and the project decides that's worth solving structurally - not a
  reactive UI addition now.

External review round 3:

- **Require `CreateSettingsView` to return a ViewModel, host maps it via a `ViewLocator`** — this
  is the v4 native-tier design's own already-made, already-shipped decision, not something open to
  relitigate from a screen redesign. `INativePluginSettingsUi.CreateSettingsView` returning a
  `Control` directly (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md) was
  deliberate: a third-party plugin's compiled assembly should never have to match Paperbunkr's own
  internal View-naming/registration convention just to show its own UI. It already has a real,
  working consumer (`ClusterLibraryManager`). Changing it now is a breaking interface change to
  shipped plugin infrastructure for a stylistic preference, not a fix for anything broken.
- **Debounce the sidebar filter `TextBox`** — the suggested mechanism (`ReactiveUI`'s `.Throttle()`)
  isn't available in this codebase at all (Paperbunkr is CommunityToolkit.Mvvm throughout, no
  ReactiveUI/DynamicData dependency - established project convention). The underlying concern also
  doesn't apply at this list's actual scale: it filters the *sidebar's installed-package rows*
  (realistically single digits to a few dozen), not the larger per-package *commands* list -
  re-filtering that on every keystroke is microseconds of work, not a stutter risk. Not adding
  debounce machinery for a problem this list is never going to have; a `DispatcherTimer`-restart
  debounce (the idiomatic non-Rx equivalent here) is a one-line addition later if this app ever
  legitimately has hundreds of installed plugins.

## 5. Testing

- `Paperbunkr.Plugins.Tests`: `PluginEngine.DiscoverNative` records a `NativePluginLoadResult` with
  a non-null `LoadError` when the module constructor throws (new fixture plugin that throws in its
  constructor, alongside the existing `MinimalNative` happy-path fixture); confirms a healthy load
  still records `LoadError: null` with the real module instance; two fixture folders sharing an
  identical manifest key produce a healthy result for the first and a "Duplicate plugin key"
  `LoadError` for the second, with neither entry silently overwritten.
- `PackageManager` currently has no dedicated test project (verified — no `Paperbunkr.Engine.Tests`
  or equivalent exists, and nothing under an existing test project references it). New tests for
  `Package.Key`/`Package.Name` (`plugin.xml` root attributes take priority over `package.ini`/
  folder heuristic; falls back to the path-hash when `plugin.xml` is missing, and that hash is
  stable across two `Package` instances built from the same path but differs for two different
  paths, including two folders that share a bare name like `dist` at different parents) land in a
  new `Paperbunkr.Engine.Tests` project, matching this repo's one-test-project-per-library
  convention.
- `Paperbunkr.App.Tests`: `PluginScreenViewModelTests` — selecting a package populates
  `SelectedPackage`'s commands filtered to that `PluginKey` only; `EnabledState` computes to
  `true`/`false`/`null` correctly for all-enabled/all-disabled/mixed child commands; invoking
  `MasterEnabledClickCommand` (as a command, not by setting `IsChecked` directly - the whole point
  being verified is that the click path never relies on `Toggle()`'s own transition) from `false`
  bulk-enables and from `null` (indeterminate) *also* bulk-enables (external review round 2's case),
  and from `true` bulk-disables; `ConfigureCommand` visibility is true only for a module
  implementing `INativePluginSettingsUi`; `NativePluginModalHostViewModel` calls `Dispose()` on a
  hosted `DataContext`/`Control` that implements `IDisposable` when dismissed, and does nothing
  (no exception, no-op) when neither does - covering both `ClusterLibraryManager` today and a
  fixture that does implement it; a broken native package's detail pane shows its `LoadError` text
  (including the "Duplicate plugin key" case) and its "Copy error" action copies that exact string;
  the Reload button (script-tier only) calls `RediscoverPlugins()` and a previously-broken
  command's `IsBroken` clears after the underlying script is fixed on disk between calls; the
  sidebar filter `TextBox` narrows `Packages` by a case-insensitive name substring.
- Manual on-screen check (this is UI-rearrangement work): install ClusterLibraryManager (already
  done), open Preferences → Plugins, select it in the sidebar, confirm Configure opens its real
  `SettingsRootView` in the existing modal overlay, toggle master Enabled off/on and confirm its
  commands' individual toggles follow, and confirm Duplicate Finder (the other real installed
  package) still renders correctly in the same screen.
