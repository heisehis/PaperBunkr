# Plugin API 4.1 — Slice 2 (Activity reporter) — Implementation Plan

> **Status: implemented 2026-09-20.** Plugins.Tests and the App adapter/Activity tests pass. Not verified on screen.
*Implements: docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §4.*

Surveyed: `IPluginEnvironment` and its four implementers (`PaperbunkrPluginEnvironment`,
`PaperbunkrNativePluginEnvironment`, plus test fakes `FakePluginEnvironment`,
`FakeNativePluginEnvironment`, `TestPluginEnvironment`), `IActivityService`/`ActivityService`,
`ActivityJobKind`, `ActivityConverters`, `ActivityCenterViewModel`'s history-filter enum,
`PluginHostService.Initialize`.

## Facts found that change the design a little
- **Native plugins already have an activity API**: `INativePluginEnvironment.StartActivityJob`
  returning `IPluginActivityHandle` (`Paperbunkr.Plugins.Abstractions.Native`), adapted in App by
  `PluginActivityHandleAdapter`. It lets a full-trust plugin pick any `ActivityJobKind`. **Kept
  unchanged.** The new `Activity` reuses `IPluginActivityHandle` as its job handle (one handle type,
  not two), and adds `Report(string detail)` to it — the spec's handle has that overload.
- `ActivityRun.Kind` is stored via `HasConversion<string>().HasMaxLength(32)` (verified in
  `PaperbunkrDbContext`), so appending `ActivityJobKind.Plugin` needs no migration and `"Plugin"` fits.
  `ActivityTrigger.Plugin` already exists.
- The environment is **shallow-cloned per command** (`MemberwiseClone`), so an adapter cached on the
  environment would stay bound to the *original* clone's `PluginKey`. `Activity` must therefore be
  built per access from the current clone's key, not cached.
- Plugin "name" for the title prefix: `Command` only knows `PluginKey`. The engine records each
  manifest's `name`, and the environment resolves key → name (falling back to the key).

## Step 1: Abstractions
**Files:** `src/Paperbunkr.Plugins.Abstractions/IPluginActivity.cs` (new),
`Native/INativePluginEnvironment.cs` (edit), `IPluginEnvironment.cs` (edit),
`src/Paperbunkr.Plugins/CSharpCommand.cs` (edit — script import), `PluginApi.cs` (edit — 4.0→4.1)
**What:** `IPluginActivity { StartJob(title, cancellable = true); RaiseAlert(severity, title, detail,
dedupeKey) }` and `PluginAlertSeverity { Info, Warning, Error }` in `Paperbunkr.Plugins`;
`IPluginEnvironment.Activity`; `IPluginActivityHandle.Report(string)`; `.csx` scripts import
`Paperbunkr.Plugins.Abstractions.Native` so the handle type is nameable; `PluginApi.Current` → `4.1`
(first callable addition).
**Verify:** builds; Step 4 tests.

## Step 2: Data + App wiring
**Files:** `src/Paperbunkr.Data/Entities/ActivityJobKind.cs` (append `Plugin`),
`src/Paperbunkr.App/Plugins/PluginActivityAdapter.cs` (new), `PluginActivityHandleAdapter.cs` (edit),
`PaperbunkrPluginEnvironment.cs` / `PaperbunkrNativePluginEnvironment.cs` (edit),
`PluginHostService.cs` (edit), `src/Paperbunkr.Plugins/PluginEngine.cs` (edit — record names),
`src/Paperbunkr.App/Views/ActivityConverters.cs` and `ActivityCenterViewModel.cs` (edit — icon +
history filter option)
**What:** `PluginActivityAdapter` wraps `IActivityService`. `StartJob` → `StartJob(Plugin,
"<Plugin name>: <title>", cancellable, ActivityTrigger.Plugin, toastPolicy: host-controlled)`;
`RaiseAlert` maps severity 1:1, prefixes the title with the plugin name, and namespaces the dedupe
key `plugin:<key>:<dedupeKey>` so one plugin can never suppress another plugin's or the app's alerts
(no dedupe key → one alert per call, the `ActivityAlert` default). Toast policy is **not** exposed
to plugins.
**Depends on:** Step 1
**Verify:** builds with 0 errors after a forced recompile (XAML touched? — no, C# only, but the App
project is rebuilt anyway).

## Step 3: Test doubles
**Files:** `src/Paperbunkr.Plugins.Tests/FakePluginEnvironment.cs`, `FakeNativePluginEnvironment.cs`,
`src/Paperbunkr.App.Tests/TestPluginEnvironment.cs`
**What:** each gets an `Activity`. The Plugins.Tests fake records started jobs and raised alerts so a
`.csx` script can be asserted against; the App.Tests one is a no-op.
**Depends on:** Step 1

## Step 4: Tests
**Files:** `src/Paperbunkr.Plugins.Tests/PluginActivityTests.cs` (new),
`src/Paperbunkr.App.Tests/PluginActivityAdapterTests.cs` (new)
**What:** a real `.csx` plugin calls `Environment.Activity.StartJob(...)`/`RaiseAlert(...)` through the
real `PluginEngine` (both tiers' environment shapes); the adapter, against a real `ActivityService`,
creates a `Plugin`-kind job whose title carries the plugin name, reports progress, succeeds/fails,
records a history run, honours cancellation, maps all three severities, namespaces the dedupe key and
dedupes repeats, and gives each cloned environment its own key. `PluginApi.Current` is `4.1`.
**Depends on:** Steps 1–3
**Verify:** `dotnet test` Plugins.Tests (full) and App.Tests filtered to `Plugin|Activity`.

## Step 5: Docs
**Files:** spec §4, `wiki/Plugins.md`, roadmap.
