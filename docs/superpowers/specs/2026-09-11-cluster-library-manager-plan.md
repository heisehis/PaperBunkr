# Cluster Library Manager — Implementation Plan

*Implements: docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-design.md (v4) and
docs/superpowers/specs/2026-09-11-cluster-library-manager-design.md (CLM). Both specs are approved
and committed. This plan was written after surveying the actual current shape of every file it
touches (`PluginEngine`, `Command`/`CSharpCommand`, `PackageManager`, `IActivityService`,
`ScheduledTaskCatalog`, `Issue`/`Series`/metadata extensions, the rules engine, dialog infrastructure)
— several real gaps the specs didn't anticipate at implementation-detail level were found and are
called out inline below, and the two design docs were patched to close them before this plan was
written (see their latest commits).*

## Ground-truth corrections this plan had to make

Surveying found the v4 spec assumed slightly more scaffolding already existed than it does. These
aren't new design decisions — they're just what "build v4" concretely requires:

- **No `Paperbunkr.Plugins.Abstractions` project exists at all.** `IPluginEnvironment`, `IPluginConfig`,
  and the 17 hook-global types currently live directly inside `Paperbunkr.Plugins` (which also carries
  Roslyn scripting + IronPython). Phase 0 below is a real extraction, not a new addition.
- **`PluginEngine.Discover` only knows how to walk `plugin.xml` + `.csx`/`.py` files.** A native
  plugin's commands come from `INativeCommandRegistrar.RegisterCommands`, not manifest-declared
  scripts — `XmlPluginInitializer.BuildCommand`'s extension-based dispatch needs a third path, and a
  new `NativeCommand : Command` wraps a compiled delegate instead of a Roslyn `Script<object>`.
- **`PluginPackageService.Manager` is a single cached `commit: true` `PackageManager` instance.**
  Tier-conditional commit behavior needs restructuring this, not just flipping a constructor arg.
- **Flattening happens in two places**, not one: `Package.UnzipFile` (extraction) and
  `PackageManager.CommitInstallPackage`'s non-recursive `Directory.GetFiles` (final copy). Both need
  the tier-conditional fix.
- **No code path today applies a `commit: false` staged install at next launch.** This is new
  startup-sequencing work.
- **`PluginScreen`/`PluginCommandRowViewModel`/`PluginPackageRowViewModel` have no trust/tier UI at
  all** — genuinely new bound properties and XAML, not an extension of an existing badge.

## Phase 0 — Extract `Paperbunkr.Plugins.Abstractions` (pure refactor, zero behavior change)

Must land first and in isolation: everything else depends on this project existing, and its own
correctness gate is that nothing about the *existing* `.csx` tier changes at all.

**Step 0.1 — Create the project and move the pure-interface types**
**Files:** new `src/Paperbunkr.Plugins.Abstractions/Paperbunkr.Plugins.Abstractions.csproj` (net10.0,
`ProjectReference` to `Paperbunkr.Data` only — no Roslyn/IronPython); move (not copy)
`src/Paperbunkr.Plugins/IPluginEnvironment.cs`, `IPluginConfig.cs`, `Hooks/PluginHooks.cs`,
`Hooks/PluginGlobals.cs`, `Hooks/PluginGlobalsTypeMap.cs`, and every other pure-interface type
`IPluginEnvironment` references transitively (`IPluginHostWindow`, `IApplication`,
`IOpenBooksManager`, `IBrowser`, `IComicDisplay`, `IMetadataGraph`, `IThemePlugin`,
`Automation/IRulesEngine.cs` + `PluginCondition`/`PluginConditionGroup`, `IMetadataWriter`) into the
new project, adjusting namespaces only as needed to compile (no member/signature changes).
**What:** `src/Paperbunkr.Plugins/Paperbunkr.Plugins.csproj` gains a `ProjectReference` to the new
project. `src/Paperbunkr.App/Paperbunkr.App.csproj` (and any other consumer found via a repo-wide
reference check) keeps compiling — add a `ProjectReference` to the new Abstractions project wherever
`IPluginEnvironment`/`IPluginConfig`/hook types are used directly outside `Paperbunkr.Plugins`.
**Depends on:** none.
**Verify:** `dotnet build` the whole solution clean; run the full existing
`Paperbunkr.Plugins.Tests` suite (including `FranchiseToolsPluginTests`) unchanged — this is the
regression gate. Any red test here means the extraction leaked a behavior change, not just a move.

**Step 0.2 — Update the `.sln`**
**Files:** `Paperbunkr.sln` (or equivalent solution file).
**What:** Add the new project so it builds as part of the normal solution build.
**Depends on:** Step 0.1.
**Verify:** `dotnet build` from solution root succeeds.

## Phase 1 — Native tier core

**Step 1.1 — `INativePluginModule`/`INativePluginEnvironment`/`INativeCommandRegistrar`**
**Files:** new `src/Paperbunkr.Plugins.Abstractions/INativePluginModule.cs`,
`INativePluginEnvironment.cs`, `INativeCommandRegistrar.cs`.
**What:** Per v4 §3 (as patched): `INativePluginModule { Initialize(INativePluginEnvironment),
RegisterCommands(INativeCommandRegistrar) }`; `INativePluginEnvironment : IPluginEnvironment {
Func<PaperbunkrDbContext> CreateDbContext; Func<ActivityJobKind, string, bool, IActivityJobHandle>
StartActivityJob; }` — note `IActivityJobHandle` doesn't exist in `Paperbunkr.Plugins.Abstractions`
today (it's `Paperbunkr.App.Services`); this step also defines a small
`Paperbunkr.Plugins.Abstractions.IActivityJobHandle`-equivalent contract (mirroring the real one's
`Report`/`Begin`/`Succeed`/`Fail`/`CancellationToken` members) that the host's real
`IActivityJobHandle` will need an adapter for in Step 1.4, since the host type itself can't move
without dragging `Paperbunkr.App` dependencies into Abstractions. `INativeCommandRegistrar` exposes
one method per existing hook type needed for CLM (`OnStartup`, `OnLibrary` at minimum;
add the rest of the 17 only as consumers need them) taking a compiled delegate.
**Depends on:** Phase 0.
**Verify:** compiles; no runtime behavior yet.

**Step 1.2 — `Paperbunkr.Plugins.Abstractions.Ui` companion project**
**Files:** new `src/Paperbunkr.Plugins.Abstractions.Ui/Paperbunkr.Plugins.Abstractions.Ui.csproj`
(`ProjectReference` to `Paperbunkr.Plugins.Abstractions`, `PackageReference` to `Avalonia` matching
the host's exact pinned version — this version must be pinned and checked against the host's own
`Directory.Packages.props`/`Paperbunkr.App.csproj` Avalonia version at this step, not assumed);
`INativePluginSettingsUi.cs`, `INativePluginUiEnvironment.cs` per v4 §3.
**Depends on:** Step 1.1.
**Verify:** compiles; confirm the referenced Avalonia version is identical to `Paperbunkr.App`'s (a
mismatch here is a real, silent future bug per the type-identity concern raised during design review
— document the checked version in a code comment).

**Step 1.3 — `PluginLoadContext` and native command wrapper**
**Files:** new `src/Paperbunkr.Plugins/Native/PluginLoadContext.cs` (non-collectible
`AssemblyLoadContext`, `AssemblyDependencyResolver` pointed at the plugin's extracted folder,
falling back to the default context for shared assemblies per v4 §4); new
`src/Paperbunkr.Plugins/Native/NativeCommand.cs` (`: Command`, wraps one registered delegate instead
of a Roslyn script — `PreCompile()` is a no-op since there's nothing to compile, `OnInvokeAsync`
invokes the stored delegate directly).
**What:** `PluginLoadContext.LoadPlugin(string assemblyPath)` loads the assembly, locates the single
type implementing `INativePluginModule` (reflection scan, throw a clear error if zero or more than
one found), instantiates it, calls `Initialize(env)` then `RegisterCommands(registrar)` where the
registrar collects `(hook, key, name, delegate)` tuples and wraps each as a `NativeCommand`.
**Depends on:** Steps 1.1–1.2.
**Verify:** unit test in `Paperbunkr.Plugins.Tests` against a tiny fixture native plugin assembly
(new `SampleNativePlugins/MinimalNative/` test fixture project, built as part of the test run,
implementing `INativePluginModule` trivially) — asserts the module loads, `RegisterCommands` fires,
and the resulting `Command` is invokable through the normal `Command.InvokeAsync` path.

**Step 1.4 — `PluginEngine` native registration path**
**Files:** `src/Paperbunkr.Plugins/PluginEngine.cs` (edit).
**What:** Add `DiscoverNative(string pluginsRoot, INativePluginEnvironment baseEnvironment)` (or fold
into `Discover` with a manifest `tier` branch) that, for each `plugin.xml` with `tier="Native"`, loads
the referenced assembly via `PluginLoadContext`, gets back its `NativeCommand`s, and adds them into
the *same* `_commands` collection `Discover` already populates — this is what delivers "native and
scripted commands appear side by side" (v4 §3).
**Depends on:** Step 1.3; `PluginManifest.Tier` (add `[XmlAttribute("tier")] public string Tier { get;
set; } = "Script";` to `src/Paperbunkr.Plugins/PluginManifest.cs` as part of this step, plus a new
`assembly` attribute on `<Plugin>` naming the native plugin's main `.dll`, since `<Command
script="...">` doesn't apply to native commands at all).
**Verify:** extend Step 1.3's fixture test to go through `PluginEngine.Discover`/`InvokeAsync` end to
end, alongside a real `.csx` fixture, asserting both appear in `GetCommands(hook)` results together.

**Step 1.5 — Host-side `INativePluginUiEnvironment` implementation + generic modal host**
**Files:** new `src/Paperbunkr.App/Plugins/PaperbunkrNativePluginEnvironment.cs` (mirrors
`PaperbunkrPluginEnvironment.cs`'s shape, implementing `CreateDbContext` via `PaperbunkrDb
.CreateContext`-style factory, `StartActivityJob` closing over the real injected `IActivityService`,
`ShowModalAsync` delegating to the new modal host below); new
`src/Paperbunkr.App/ViewModels/NativePluginModalHostViewModel.cs` (`IsOpen`, `HostedContent : Control?`,
a non-generic `IPendingModalCompletion` + generic `PendingModalCompletion<TResult>` wrapper pattern, as
surveyed); edit `src/Paperbunkr.App/Views/MainWindow.axaml` to add one `OverlayShell` bound to
`NativePluginModalHost.IsOpen` hosting a bare `ContentControl` over `HostedContent` (mirrors the
existing `ConfirmDialog` wiring at `MainWindow.axaml:914-916` structurally, generic content instead of
a fixed view type); edit `MainViewModel` to own a `NativePluginModalHost` property.
**Depends on:** Steps 1.1–1.2.
**Verify:** a small `Paperbunkr.App.Tests` test that shows a trivial `Control` through
`ShowModalAsync<bool>`, resolves it, and asserts the awaited task completes with the right value and
`IsOpen` returns to `false`.

## Phase 2 — Package/install pipeline for the Native tier

**Step 2.1 — Fix both flattening sites, tier-conditionally**
**Files:** `src/Paperbunkr.Engine/PackageManager.cs` (edit `Package.UnzipFile` and
`CommitInstallPackage`), `src/Paperbunkr.App/Plugins/PluginPackageService.cs` (edit).
**What:** `PluginPackageService.Install` peeks the zip's `plugin.xml` entry via `ZipArchive` (no full
extraction) to read `tier` *before* deciding how to extract. `Script` packages: today's flattening
behavior, byte-for-byte unchanged (regression risk otherwise). `Native` packages: a new
structure-preserving extraction path (write each entry to `Path.Combine(targetPath, entry.FullName)`,
creating subdirectories as needed) and a structure-preserving `CommitInstallPackage` variant
(`Directory.GetFiles(path, "*", SearchOption.AllDirectories)` with relative-path-preserving copy).
**Depends on:** none (independent of Phase 1, can proceed in parallel).
**Verify:** new `PluginPackageServiceTests` cases: (a) install a `Script`-tier fixture zip, assert
byte-identical extracted layout to today's behavior; (b) install a `Native`-tier fixture zip containing
a `runtimes/win-x64/native/fake.dll`-shaped entry, assert it lands at that exact nested path after
both extraction and commit.

**Step 2.2 — Tier-conditional staged install + apply-at-startup**
**Files:** `src/Paperbunkr.App/Plugins/PluginPackageService.cs` (edit — no longer a single cached
`commit: true` `Manager`; branch per-install based on the peeked tier); new startup-sequencing code
(likely `src/Paperbunkr.App/App.axaml.cs` or wherever the app's existing startup sequence lives) that
checks for and applies any pending `Native`-tier package at process start, before `PluginEngine
.Discover` runs.
**Depends on:** Step 2.1.
**Verify:** test that installing a `Native` package leaves it in a pending state (not immediately
loadable), and a simulated "next launch" (re-invoking the new startup check) commits it.

**Step 2.3 — Plugin screen trust/tier UI**
**Files:** `src/Paperbunkr.App/ViewModels/PluginPackageRowViewModel.cs`,
`PluginCommandRowViewModel.cs` (add `IsNativeTier`/similar bound property, sourced from
`PluginManifest.Tier`), `src/Paperbunkr.App/Views/PluginScreen.axaml` (new markup: "Full read/write
access to your library database" notice at install time and in the installed list, per v4 §2), and
`.pbplugin` extension support in the install file picker (accept both `.zip` and `.pbplugin`).
**Depends on:** Step 2.1 (needs `Tier` flowing through the same package metadata).
**Verify:** on-screen check (per this project's UI-verification convention) — install a native fixture
package, confirm the notice renders; existing `Script` packages show no such notice.

## Phase 3 — `ClusterLibraryManager` plugin itself

Lives at `plugins/ClusterLibraryManager/` (sibling to `src/`, its own solution — deliberately **not**
added to `Paperbunkr.sln`, so nothing in the host build can accidentally `ProjectReference` it,
keeping "ships independently" real). References `Paperbunkr.Plugins.Abstractions` and
`Paperbunkr.Plugins.Abstractions.Ui` the way a genuine third party would — a relative `ProjectReference`
for this repo's own dev/build convenience is fine, but note in the plugin's own README that a real
external plugin author would reference the published contract instead (packaging/publishing those two
assemblies for external consumption is out of scope for this plan, per CLM design §12).

**Step 3.1 — Project scaffold + `OrganizerScraperPlugin`**
**Files:** `plugins/ClusterLibraryManager/ClusterLibraryManager.csproj`,
`plugins/ClusterLibraryManager/OrganizerScraperPlugin.cs`, `plugins/ClusterLibraryManager/plugin.xml`
(`tier="Native"`, `assembly="ClusterLibraryManager.dll"`).
**What:** `OrganizerScraperPlugin : INativePluginModule, INativePluginSettingsUi` — `Initialize` wires
up `ComicVineService`/`LibraryOrganizerService`/the plugin's own LiteDB connection and starts the
self-contained automation timer (CLM §9) if enabled in settings; `RegisterCommands` wires
`Library`/`Startup` hooks; `CreateSettingsView` returns the Step 3.6 view.
**Depends on:** Phase 1 complete.
**Verify:** loads through `PluginEngine` exactly like Phase 1's fixture did.

**Step 3.2 — `ComicVineService` + DTOs + `MatchScoreCalculator`**
**Files:** `ComicVineService.cs`, `ComicVineDtos.cs` (or split per-DTO), `MatchScoreCalculator.cs`,
`ComicVineMatchMemory.cs` (LiteDB document + repository).
**What:** Per CLM §3/§4 exactly — the 4 endpoints, throttle/retry, 6-term matchscore, imprint map.
**Depends on:** Step 3.1 (project exists); independent of everything else in Phase 3.
**Verify:** unit tests (new `ClusterLibraryManager.Tests` project) against a fake
`HttpMessageHandler` — endpoint URL shapes, throttle timing with a fake clock, retry-once, DTO
deserialization against fixture JSON; table-driven matchscore tests per term.

**Step 3.3 — Token engine + sanitizer**
**Files:** `Templating/TemplateParser.cs`, `Templating/TemplateNode.cs` (the `LiteralNode`/
`TokenNode`/`ConditionalGroupNode`/`InversionGroupNode` AST from the review-round revision),
`Templating/TemplateEvaluator.cs` (resolves nodes against an `Issue` via `IssueMetadataExtensions`'
`Effective*` methods, `IssueTag` filtering, `IssueCustomValue` lookup), `Sanitizer.cs`.
**Depends on:** Step 3.1; reads `Paperbunkr.Data` entities directly (already a `ProjectReference` via
Abstractions' own dependency, or add directly since this plugin is full-trust per v4 §2).
**Verify:** unit tests per token type (scalar, padded numeric, multi-value+separator, conditional
group, inversion group) plus full default-template end-to-end against a fixture `Issue`; sanitizer
table-driven test over the exact CE replace-map.

**Step 3.4 — `LibraryOrganizerService` (`PlanAsync`/`ExecuteAsync`) + `OrganizerProfile`**
**Files:** `LibraryOrganizerService.cs`, `OrganizerPlan.cs`, `OrganizerProfile.cs` (LiteDB document +
repository), `CollisionPolicy.cs` (interactive resolver delegate + `AutomationCollisionPolicy` enum).
**What:** Per CLM §5/§6/§7 — two-phase plan/execute, CE's exact rename-suffix algorithm, per-item
persist-and-continue (not a batch transaction), reuse of `IRulesEngine`/`PluginConditionGroup` for
excludes (exactly the `rated-not-checked.csx` idiom surveyed), the `isInteractive` branch for
automation.
**Depends on:** Step 3.3 (token engine), Step 3.2 (not directly, but both are prerequisites of Step
3.5's dialogs).
**Verify:** `PlanAsync` pure-function tests (no I/O) for collision detection/path generation;
`ExecuteAsync` integration tests against a temp directory covering Move/Copy/Simulate and each
collision branch via a stub resolver delegate.

**Step 3.5 — Collision + match-review dialogs**
**Files:** `Views/FileConflictDialogView.axaml` + `FileConflictDialogViewModel.cs`,
`Views/ComicVineMatchReviewDialog.axaml` + `.../ComicVineMatchReviewDialogViewModel.cs`.
**What:** Both are plain `UserControl`s (no code-behind logic beyond `InitializeComponent`, matching
this project's Avalonia build gotcha in `CLAUDE.md`), shown via `INativePluginUiEnvironment
.ShowModalAsync<TResult>` — each view's buttons invoke a callback closure (passed in when the dialog
is constructed) rather than reaching into host state directly, per the surveyed non-generic-completion
pattern.
**Depends on:** Phase 1 Step 1.5 (the modal host must exist); Step 3.4 (collision policy/plan shapes).
**Verify:** on-screen check per this project's UI-verification convention — trigger a real collision
during a test organize run, confirm Replace/Rename/Skip + "apply to all" behave as specified.

**Step 3.6 — `SettingsViewModel`/`SettingsView.axaml` + profile manager sub-view**
**Files:** `Views/SettingsView.axaml` + `SettingsViewModel.cs`, `Views/ProfileManagerView.axaml` +
`ProfileManagerViewModel.cs`.
**What:** API key field, test-connection command, template editor with insert-token command, the 20
field-scrape toggles, automation toggle+interval (Step 3.1's timer), and the profile CRUD sub-view
(CLM §7). Returned from `OrganizerScraperPlugin.CreateSettingsView`.
**Depends on:** Steps 3.2–3.4 (binds to their settings surfaces).
**Verify:** on-screen check — open via the Plugin screen's settings entry point, confirm every field
persists to the plugin's own LiteDB store and round-trips correctly.

**Step 3.7 — Undo log**
**Files:** `UndoLog.cs` (LiteDB collection + repository), wired into `LibraryOrganizerService
.ExecuteAsync`'s per-item write per CLM §8's ordering fix (core file+DB write first and authoritative,
Undo-log append after, in its own non-fatal try/catch).
**Depends on:** Step 3.4.
**Verify:** unit test simulating an Undo-log write failure after a successful move, asserting the
move/DB-update stand and only the Undo entry is missing/logged.

**Step 3.8 — `ClusterLibraryManager.Tests` project wiring**
**Files:** `plugins/ClusterLibraryManager/ClusterLibraryManager.Tests/ClusterLibraryManager.Tests.csproj`.
**What:** Standalone test project (own solution, per this phase's isolation goal), following this
project's existing test conventions in style (xUnit, fixture patterns) but built/run independently of
`Paperbunkr.sln`.
**Depends on:** Step 3.1.
**Verify:** `dotnet test` from `plugins/ClusterLibraryManager/` runs green independently of the host
solution.

## Sequencing summary

Phase 0 → Phase 1 (blocks everything native) → Phase 2 (independent of Phase 1, can run in parallel)
→ Phase 3 (needs Phase 1 fully done; Step 2.3's trust UI is nice-to-have-first but not blocking).
Within Phase 3, 3.1 gates everything else; 3.2/3.3 are independent of each other; 3.4 needs 3.3; 3.5
needs 3.4 and Phase 1 Step 1.5; 3.6 needs 3.2–3.4; 3.7 needs 3.4; 3.8 can happen any time after 3.1.
