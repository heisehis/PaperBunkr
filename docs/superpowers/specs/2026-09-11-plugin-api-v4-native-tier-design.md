# Plugin API v4 — Native Plugin Tier

*Date: 2026-09-11. Scope: a second, additive plugin tier alongside the existing `.csx`-scripted one
from `docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md` (v2) and
`docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md` (v3) — real compiled .NET
assemblies, loaded at runtime, with full library-database access and the ability to supply their own
compiled Avalonia UI. Produced via the same `/grilling` pass as
`docs/superpowers/specs/2026-09-11-cluster-library-manager-design.md` (CLM), which is this tier's
first real consumer, once this spec's decisions superseded that document's original
sandboxed-extension architecture (its §2/§2.1).*

## 1. Why this exists

Paperbunkr's plugin system was built, per the project owner directly, so "other creators/devs could
add something to the app" — Paperbunkr as a modular whole with plugins as genuinely independent
extra pieces, the way ComicRack CE's `.crplugin` bundles worked. The existing `.csx` tier (v2/v3)
doesn't deliver that for anything beyond simple scripted automation: no typed `HttpClient`, no
compiled Avalonia views, no way to ship a plugin as a binary a third party can install without the
Paperbunkr source tree. This tier is what actually closes that gap.

**Additive, not a replacement** (grilling Q19=A): the `.csx` tier keeps working unchanged for simple
plugins (the existing `FranchiseTools` sample plugin needs zero changes). Native is for plugins
complex enough to need real compiled code and UI.

## 2. Trust model (grilling Q20=B)

Native plugins are **full-trust**, not capability-scoped to `IPluginEnvironment` the way `.csx`
scripts are. This was a deliberate call, made explicitly against my own recommendation: rather than
constraining native plugins to the same narrow surface as scripts (which would have made "native"
just a compiled version of the same sandbox), a native plugin gets its own `PaperbunkrDbContext` and
writes directly — no `IMetadataWriter` audit gate.

This means the two tiers now have genuinely different trust postures, and that has to be visible to
users, not just a manifest attribute:
- `plugin.xml`'s `<Plugin>` root gains `tier="Native"` (default `"Script"`, preserving every existing
  manifest unchanged).
- The Plugin screen shows a distinct, unambiguous notice for `Native`-tier plugins at install time
  and in the installed-plugins list — "Full read/write access to your library database" — separate
  from and in addition to the existing per-command `confirmWrites` flag, which continues to apply
  only to the audited `.csx` tier.

**What still doesn't change**: `Paperbunkr.Data`'s query-builder/resolver internals stay `internal`
with `[InternalsVisibleTo("Paperbunkr.App")]` (v3 §7) — not because native plugins need sandboxing
from them (they don't, per this trust model), but because those types were never designed as a
stable, versioned public contract for *any* external caller, native or scripted. A native plugin
gets the public surface of `Paperbunkr.Data` (the `DbContext`, public entities, `DbSet`s) — the same
public API Paperbunkr.App itself uses — not the internal helpers.

## 3. `INativePluginModule` — the entry-point contract

New interface in `Paperbunkr.Plugins.Abstractions` (which gains an Avalonia dependency to support
`CreateSettingsView`, described below — a deliberate, contained addition, not a general dependency
bloat):

```csharp
public interface INativePluginModule
{
    void Initialize(INativePluginEnvironment environment);
    void RegisterCommands(INativeCommandRegistrar registrar);
    Avalonia.Controls.Control? CreateSettingsView(INativePluginEnvironment environment);
}
```

- `RegisterCommands` lets a native plugin register compiled delegates against the same 17 hook types
  `.csx` commands use (`registrar.OnLibrary(key, name, (env, books) => {...})`, etc.) — native and
  scripted commands appear side by side in the same hook-grouped list on the Plugin screen; a user
  can't tell which tier a command came from just by looking at the list, only via the plugin-level
  trust notice from §2.
- `CreateSettingsView` returns a real, plugin-compiled `Control` — this is what actually delivers a
  literal, plugin-authored settings view (grilling Q21=A), not the declarative-schema mechanism CLM's
  original draft proposed before this tier existed. Returning `null` means the plugin has no settings
  UI (equivalent to no `ConfigScript` pairing today).

**`INativePluginEnvironment : IPluginEnvironment`** — extends the *existing* environment interface
scripts already get, adding exactly what full trust requires:
```csharp
public interface INativePluginEnvironment : IPluginEnvironment
{
    Func<PaperbunkrDbContext> CreateDbContext { get; }
    Task<TResult> ShowModalAsync<TResult>(Control content);
}
```
Critically, this is a **separate, wider interface** — the base `IPluginEnvironment` that `.csx`
scripts receive is untouched, so nothing about this tier loosens the existing script sandbox.
`CreateDbContext` mirrors the existing `PaperbunkrDb.CreateContext` factory idiom already used
elsewhere (e.g. `PreferencesScreenViewModel`'s internal test-seam constructor) rather than handing
out one shared long-lived context. `ShowModalAsync` is the connective tissue a plugin needs to
actually show `CreateSettingsView`'s `Control` (and any other plugin-authored dialog, e.g. CLM's
collision/match-review dialogs) as a real overlay — the plugin can't touch `MainWindow.axaml` itself,
so the host provides one generic "host this Control as a modal, await a typed result" primitive
instead of one bespoke hosting mechanism per plugin.

## 4. Package format and install (reuses existing, CE-ported infrastructure)

**No new install pipeline for `Script`-tier packages** — `PluginPackageService.Install(zipFile)`
(`src/Paperbunkr.App/Plugins/PluginPackageService.cs`) keeps extracting those exactly as today (flat,
CE's own "Script Archive|\*.zip" format) into `%AppData%\Paperbunkr\plugins\<key>\`.

**`Native`-tier packages need real subfolder structure preserved on extraction — this was wrong in an
earlier draft of this spec** and is fixed here rather than shipped broken. A compiled plugin project
that references `Microsoft.Data.Sqlite` (or any package with a native/`P/Invoke` dependency)
publishes its native binary under `runtimes/<rid>/native/...` in its own build output, and
`AssemblyDependencyResolver.ResolveUnmanagedDllToPath` (used by `PluginLoadContext`, below) resolves
native libraries by looking up that exact relative path against the plugin's `.deps.json`. Flattening
the zip on extraction — as `Package.UnzipFile` does today for `Script` packages, done blindly to a
`Native` package's ZIP entries too, in the draft this corrects — puts `e_sqlite3.dll` at the plugin's
root instead of `runtimes/win-x64/native/e_sqlite3.dll` underneath it; the resolver looks for the
latter, doesn't find it, and native SQLite fails to load at plugin startup. Fix: before extracting,
`PluginPackageService` peeks the zip's `plugin.xml` entry directly via `ZipArchive` (no full
extraction needed just to read one small XML entry) to read `tier`; `Script` packages extract via
today's flattening `Package.UnzipFile` path unchanged, `Native` packages extract via a new path that
preserves each entry's full relative directory structure exactly as authored in the zip. This is a
general, durable fix — it protects *any* future native plugin with *any* native dependency, not a
one-off patch for this one plugin's SQLite choice.

**Canonical extension: `.pbplugin`** (grilling Q18=B), superseding the generic `.zip` label in the
install picker for *all* plugin packages going forward, script or native — bare `.zip` stays accepted
for backward compatibility with packages already in circulation.

**Staged install for native packages** (resolves Q22=B's "always require restart"): `PackageManager`
already supports this — its constructor (`PackageManager(string path, string tempPath, bool commit)`,
`src/Paperbunkr.Engine/PackageManager.cs:270`) has a real `commit: false` mode that stages into
`PendingPackagePath` instead of installing immediately, exactly the mechanism CE itself built for the
same reason (the existing `PluginPackageService` doc comment: CE gated installs behind a restart
"because CE's Python engine... hold[s] process-wide state a live reload could conflict with" — a
loaded, locked native assembly is the identical problem). `PluginPackageService`/`PluginScreenViewModel`
branch on the manifest's `tier`: `Script` packages keep today's immediate `commit: true` (no restart,
matches current UX exactly); `Native` packages install via `commit: false`, take effect on next
launch, and the Plugin screen shows a "restart to finish installing" state for anything pending.

This also **simplifies loading**, since Q22 ruled out live hot-unload entirely: native assemblies
load once at startup via a `PluginLoadContext : AssemblyLoadContext` per plugin (non-collectible —
there's no attempt to unload mid-process, so the complexity/pitfalls of collectible ALCs don't apply
here) using `AssemblyDependencyResolver` pointed at the plugin's own extracted folder for its
dependencies, falling back to the default context for shared assemblies (BCL, Avalonia,
`Paperbunkr.Plugins.Abstractions`, `Paperbunkr.Data`). Disabling or uninstalling a native plugin marks
it and requires the same restart-to-take-effect as install, for the same reason.

## 5. Versioning

Once third parties compile against `Paperbunkr.Plugins.Abstractions`, its public shape becomes a real
external contract, not something to casually reshape release to release the way `.csx` scripts (which
just get recompiled fresh against whatever the host currently ships) tolerate today. This tier adopts
semantic versioning for that assembly starting now, and `PluginLoadContext` checks the plugin's
referenced Abstractions version against the host's at load time, refusing (with a clear message on
the Plugin screen) rather than loading and failing unpredictably on a real mismatch.

## 6. What CLM changes because of this

`docs/superpowers/specs/2026-09-11-cluster-library-manager-design.md` predates this spec and
describes a sandboxed-extension architecture (declarative settings schema + host-service bridge) that
this tier supersedes. See that document's revised §2 for the concrete mapping — in short,
`OrganizerScraperPlugin` becomes a real `INativePluginModule` implementation, `ComicVineService` and
`LibraryOrganizerService` ship inside the plugin's own compiled project (not `Paperbunkr.Data`), and
`SettingsViewModel`/`SettingsView.axaml` become the plugin's own compiled Avalonia view/viewmodel
returned from `CreateSettingsView` — the original ask, now actually deliverable as specified.
