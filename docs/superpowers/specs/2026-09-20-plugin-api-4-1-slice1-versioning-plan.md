# Plugin API 4.1 — Slice 1 (API Versioning) — Implementation Plan

> **Status: implemented 2026-09-20.** All seven steps done. Two deviations from this plan as written,
> both recorded in the spec (§3.2): the absent-`requiresApi` failure hint applies only to
> drift-shaped failures (a version note on every syntax error in every legacy plugin would be noise),
> and the hint signature therefore takes a `driftShapedFailure` flag. Verified: Plugins.Tests 105/105,
> App.Tests plugin subset 63/63, App builds with 0 errors after a forced recompile (XAML weave
> confirmed), and the gate tests fail when the gate is disabled. **Not verified:** on-screen look of
> the Plugin screen (the "Requires API" line and the blocked-plugin banner).
*Implements: docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §3 (slice 1 only — the reporter, hooks and settings schema are slices 2–4 and get their own plans).*

Surveyed against the current code, not from memory: `PluginManifest`, `XmlPluginInitializer`,
`PluginEngine` (`Discover`, `DiscoverNative`, `InvokeAsync`), `Command`, `PluginHooks`,
`PluginScreenViewModel`, `PluginPackageDetailViewModel`, `PluginEngineTests`.

## Decisions this plan makes where the spec was silent — please check these

1. **`PluginApi.Current` stays `4.0` in this slice.** The spec says the constant moves to `4.1` "in
   the first slice that ships anything new". Slice 1 adds a manifest attribute and metadata but no
   member a plugin can *call*, and older hosts ignore an unknown XML attribute, so nothing here needs
   a 4.1 host. The bump therefore lands in **slice 2** (the reporter is the first callable addition).
   Tests inject a host version instead of reading `Current`, so they don't depend on this.
2. **A malformed `requiresApi` (e.g. `"abc"`, `"4.1.2"`) blocks the plugin** as a malformed manifest,
   with a reason, rather than being ignored. Accepted forms are `"N"` (= `N.0`) and `"N.M"`. An
   unparseable value tells the host nothing about compatibility, and the spec already treats a
   malformed manifest entry as broken-with-a-reason elsewhere (§6.1).
3. **Spec correction (applied to the spec in Step 7):** §3.2 listed "a hook name this host does not
   know" as an invoke-time failure. A hook the host doesn't know is never invoked, so no failure
   exists to attach a hint to. Slice 1 leaves such commands as today (loaded, never fired). A visible
   note on the command row would be a small follow-up if wanted.

## Step 1: `PluginApi` constant and compatibility evaluator
**Files:** `src/Paperbunkr.Plugins.Abstractions/PluginApi.cs` (new),
`src/Paperbunkr.Plugins.Abstractions/Hooks/PluginHooks.cs` (edit)
**What:**
- `PluginApi.Current` — `static readonly Version` `4.0`, namespace `Paperbunkr.Plugins`, with an XML
  doc saying it is bumped by hand in the same commit as any plugin-visible API addition.
- `PluginApiCompatibility` (static, same file or sibling) with two pure functions taking the **host
  version as a parameter** (default `PluginApi.Current`) so tests can simulate any host:
  - `Evaluate(string? requiresApi, Version host)` → a small record: `Kind` (`Compatible`,
    `MajorMismatch`, `Malformed`), the parsed `Declared` (`Version?`, null when the attribute is
    absent), and a human `Reason` for the two blocking kinds, worded like
    `plugin requires API 5.0, this app provides 4.0 (major version mismatch)`. Absent = compatible
    (baseline `4.0`, major 4). Major compared in **both directions**; any minor difference is
    `Compatible`.
  - `FailureHint(Version? declared, Version host)` → the string
    `plugin declares API 4.2, this app provides 4.0` (or `plugin declares no API version (assumed
    4.0), this app provides …`), or **null** when no hint applies: hint only when `declared` is
    absent or its minor is higher than the host's (same major is guaranteed by the gate).
- `PluginHooks.Since(string hook)` — a `Since` table alongside `ValidHooks`; all 17 CE-parity hooks
  = `4.0`; unknown hook → null. Metadata only, not enforced (spec §3.3). `ValidHooks`'s type stays
  unchanged so `PluginCommandRowViewModel.HookGroupLabel` and any other reader is untouched.
**Depends on:** none
**Verify:** `dotnet build src/Paperbunkr.Plugins.Abstractions`; unit tests in Step 6.

## Step 2: `requiresApi` on the manifest
**Files:** `src/Paperbunkr.Plugins/PluginManifest.cs` (edit)
**What:** `[XmlAttribute("requiresApi")] public string? RequiresApi { get; set; }` on `PluginManifest`
(root only, per spec §3.1). Purely additive — every existing manifest deserializes unchanged.
**Depends on:** none
**Verify:** existing `PluginEngineTests` still pass; Step 6 covers the new attribute.

## Step 3: Discovery-time gate for both tiers
**Files:** `src/Paperbunkr.Plugins/PluginEngine.cs` (edit), `src/Paperbunkr.Plugins/Command.cs` (edit),
`src/Paperbunkr.Plugins/XmlPluginInitializer.cs` (read-only reference)
**What:**
- `Command` gains `public Version? DeclaredApi { get; set; }` (null = attribute absent).
- `PluginEngine` gains a per-plugin record and a read-only dictionary, e.g.
  `PackageApiInfo : IReadOnlyDictionary<string, PluginApiInfo>` where `PluginApiInfo` = (raw
  `RequiresApi` text, parsed `Declared`, `BlockedReason`). Cleared at the top of `Discover` like the
  other collections. Populated for **both tiers** whenever the manifest was read.
- **Script tier**, in `Discover`'s loop after `ReadManifest`: evaluate; if `MajorMismatch` or
  `Malformed`, record the `BlockedReason`, **skip `GetCommands` / `Initialize` / `PreCompile`
  entirely** (nothing is compiled) and `continue`. The plugin key used for the record is the same
  one `GetCommands` derives (`manifest.Key`, else the folder name). Otherwise proceed as today and
  set each command's `DeclaredApi` before `Initialize`.
- **Native tier**, in `DiscoverNative`: after the existing `.remove` and duplicate-key checks and
  **before** `PluginLoadContext.LoadPlugin`, evaluate; if blocked, write
  `_nativeLoadResults[pluginKey] = new NativePluginLoadResult(null, reason)` and return, so
  `LoadPlugin` is never called and the assembly is never loaded. On a load, set `DeclaredApi` on each
  returned `NativeCommand` (it is created inside `LoadPlugin`, so set it in the existing loop).
**Depends on:** Steps 1, 2
**Verify:** Step 6 tests (blocked in both directions, both tiers, code never touched; same-major
higher minor still loads and runs).

## Step 4: Failure hints (lenient path only)
**Files:** `src/Paperbunkr.Plugins/PluginEngine.cs` (edit), `src/Paperbunkr.Plugins/Command.cs` (edit)
**What:**
- **Load/compile failures:** after `cmd.PreCompile()` in `Discover`, if `cmd.IsBroken` and
  `FailureHint(...)` is non-null, append the hint to `CompileError` via a small `internal` method on
  `Command` (its setter is `protected`). In `DiscoverNative`'s `catch`, append the hint to the
  recorded `LoadError` the same way.
- **Invoke-time failures:** in `PluginEngine.InvokeAsync`'s `catch`, unwrap
  `TargetInvocationException` / `AggregateException`; if the inner exception is a
  `MissingMemberException` (covers `MissingMethodException`/`MissingFieldException`) or a
  `TypeLoadException` **and** a hint applies, replace `Error` with a small
  `PluginApiMismatchException` (message = original message + the hint, inner = original). This is
  deliberate: `result.Error?.Message` is formatted at about six call sites
  (`PluginCommandRowViewModel`, `PluginHostService` ×3, `QuickOpenViewModel`, `DetailTabsViewModel`),
  and wrapping in the engine means **none of them need editing** to show the hint. Unrelated errors
  are passed through untouched, and the command is **not** disabled (spec §3.2).
- Hint is never produced for a failure with no version angle.
**Depends on:** Step 3
**Verify:** Step 6 tests; `PluginEngineTests`' existing `Contains("boom", result.Error?.Message)`
assertion still passes (an ordinary exception is not wrapped).

## Step 5: Plugin manager display
**Files:** `src/Paperbunkr.App/ViewModels/PluginScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/PluginPackageDetailViewModel.cs` (edit),
`src/Paperbunkr.App/Views/PluginScreen.axaml` (edit — one added text line, **no new view**, so the
new-`x:Class` build gotcha in `CLAUDE.md` does not apply)
**What:**
- `SelectPackage`: `loadError` becomes the native `LoadError` **or**, for a script package, the
  engine's `BlockedReason` — so a blocked script plugin shows the same banner a broken native one
  does. Pass the declared `requiresApi` text to the detail VM as a new constructor argument
  (`requiresApi`), and expose `RequiresApiText` / `HasRequiresApi` (null when absent).
- `IsPackageBroken`: also true when the engine has a `BlockedReason` for the package. This is needed
  because a blocked script package registers **zero commands**, and the existing rule deliberately
  never flags a zero-command package broken.
- `PluginScreen.axaml`: one plain informational line, e.g. "Requires API 4.1", in the detail header.
  Per spec §3.4: **no warning badge, no alert**, even when the declared minor exceeds the host's.
  Use theme resources, not hard-coded hex (the file's existing `#4CAF50`/`#E06060` dots are not to
  be copied). Load the `avalonia` skill and read
  `~/.claude/skills/avalonia/avalonia-pro-max/review-checklist/SKILL.md` before calling this done
  (`CLAUDE.md`).
**Depends on:** Step 3
**Verify:** `PluginScreenViewModelTests` additions (Step 6); build the App project; **on-screen
verification is a separate explicit step** — automated tests do not cover how the line and banner
look.

## Step 6: Tests
**Files:** `src/Paperbunkr.Plugins.Tests/PluginApiCompatibilityTests.cs` (new),
`src/Paperbunkr.Plugins.Tests/PluginApiVersioningTests.cs` (new — mirrors `PluginEngineTests`'
`WritePlugin` temp-dir helper and `FakePluginEnvironment`),
`src/Paperbunkr.App.Tests/PluginScreenViewModelTests.cs` (edit)
**What** (xUnit, matching the project's conventions; no live network):
- **Compatibility unit tests** (pure): absent → compatible and `Declared` null; `"4.0"`, `"4"`,
  `"4.7"` compatible against host `4.0`; `"5.0"` and `"3.9"` → `MajorMismatch` (both directions);
  `"abc"`, `""`-with-whitespace, `"4.1.2"` → `Malformed`; `FailureHint` is null for a lower-or-equal
  minor and non-null for absent or higher minor; hook `Since` returns `4.0` for a known hook, null
  for an unknown one.
- **Engine, script tier:** major-mismatch plugin → no commands, `PackageApiInfo` carries the reason,
  and a `.csx` with **invalid C#** proves it was never compiled (no `CompileError` anywhere) while a
  sibling valid plugin still loads; same-major higher-minor plugin loads and `InvokeAsync` succeeds;
  a higher-minor plugin whose script has a compile error gets the hint in `CompileError`; a plugin
  with a compile error and **no** `requiresApi` also gets the "declares no API version" hint; a
  plugin with a compile error and an explicit `"4.0"` does **not**.
- **Engine, invoke-time:** a `.py`/`.csx` command that throws `MissingMethodException` surfaces the
  hint in `result.Error.Message`; one that throws `InvalidOperationException` is not wrapped; the
  command remains enabled and not broken afterwards.
- **Engine, native tier:** a copy of the `MinimalNative` sample manifest with `requiresApi="5.0"` →
  `NativeLoadResults[key].LoadError` is the mismatch reason, `Module` is null, no commands, and the
  assembly was never loaded (assert via the absence of the module, not by reflection tricks).
- **App:** `PluginScreenViewModelTests` — a blocked script package is flagged broken and its detail
  VM shows the reason as `LoadError`; a plugin declaring a higher minor that runs fine is **not**
  flagged and shows `RequiresApiText` only.
**Depends on:** Steps 1–5
**Verify:** `dotnet test src/Paperbunkr.Plugins.Tests/Paperbunkr.Plugins.Tests.csproj` (full) and
`dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter FullyQualifiedName~Plugin`.
App tests that drive a view-model needing the dispatcher use `TestDispatcher.Drain()` (the resolved
headless-flake convention).

## Step 7: Docs
**Files:** `docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md` (edit),
the plugin-developer wiki page under `wiki/` (**locate first with a grep; do not guess the name** —
if none exists, put the section in `docs/onboarding.md` §10 instead)
**What:**
- Spec §3.2: fix the invoke-time bullet (Decision 3 above) and note `Current` stays `4.0` until
  slice 2 (Decision 1).
- Wiki/onboarding: document `requiresApi` (forms accepted, major blocks / minor is lenient, absent
  = 4.0) and the failure hint. `docs/Paperbunkr-Roadmap.md` gets a slice-1 status note **only once
  it has actually shipped**, by hand, saying what was verified — not before.
**Depends on:** Steps 1–6
**Verify:** read-through against the shipped behavior.

## Notes for whoever picks this up
- The working tree currently has **uncommitted changes from other sessions** (per `git status`:
  `TextSpinner`, several Preferences views, two other specs). Run `git status` before starting and
  keep this slice's edits to the files named here; do not stage or touch those.
- Step 3's ordering matters: the gate must run **before** `PreCompile` (script) and before
  `LoadPlugin` (native), otherwise "never loads the code" is false.
- On-screen verification of the plugin manager is not something the test suite can give you; state
  it as pending rather than done.
