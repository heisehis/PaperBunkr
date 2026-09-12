# Plugin Management Screen Redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-12-plugin-management-screen-redesign-design.md*

Read in full before starting each step below; this plan doesn't repeat its reasoning, only what
to build. One deliberate refinement over the design doc, found while planning (noted again at
Step 2): §4.2's "the first-discovered package's `NativeLoadResults` entry is unaffected" isn't
achievable as literally worded — both packages share the identical manifest key, so they share
the *same* dictionary slot; there is no separate slot for "the first one, untouched." The
implementable version (and the one this plan builds): when a duplicate key is found, that shared
slot ends up holding a single "Duplicate plugin key" error, not either package's individual real
result. This is still exactly what the design's own goal requires (surface the conflict instead of
silently masking it) — arguably more useful than letting one specific copy look falsely healthy
while hiding that a duplicate exists at all. The design doc's §4.2 wording gets corrected to match
in Step 2, not just the code.

## Step 1: Package identity fix + new `Paperbunkr.Engine.Tests` project
**Files:** `src/Paperbunkr.Engine/PackageManager.cs` (edit), `src/Paperbunkr.Engine.Tests/Paperbunkr.Engine.Tests.csproj` (new), `src/Paperbunkr.Engine.Tests/PackageIdentityTests.cs` (new)

**What:**
- In `PackageManager.Package`, replace the two separate `DetectNativeTier` reads with one
  `ReadManifestAttributes(string packagePath)` returning `(bool isNative, string key, string name)`
  from a single `XDocument.Load` of `plugin.xml` (tier/key/name attributes together) — avoids three
  separate file reads per package for what the design doc showed as three separate methods.
  Add `public string Key { get; private set; }` to `Package`.
- In `InitValues()`: call `ReadManifestAttributes(PackagePath)` once. Set `IsNativeTier` from it
  (replacing the old `DetectNativeTier` call). Set `Key` to the manifest key if non-blank, else a
  fallback: `Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(PackagePath).ToUpperInvariant())))[..16]`
  (needs `using System.Security.Cryptography;` and `using System.Text;` added to the file). Set
  `Name` to the manifest name if non-blank, else the existing `iniFile.GetValue("Name", FileToName(originalName))`
  chain — capture `Name`'s constructor-supplied value into a local *before* overwriting it, since
  the existing fallback reads `Name` as its own default input.
- This project is `<Nullable>disable</Nullable>` — don't add `?` annotations; match the file's
  existing plain-type style.

**Depends on:** none

**Verify:**
- New `Paperbunkr.Engine.Tests` (xUnit, mirrors `Paperbunkr.Plugins.Tests.csproj`'s shape:
  `Microsoft.NET.Test.Sdk`/`xunit`/`xunit.runner.visualstudio`/`coverlet.collector`, `IsTestProject`,
  one `ProjectReference` to `..\Paperbunkr.Engine\Paperbunkr.Engine.csproj`). Add it to
  `Paperbunkr.sln` via `dotnet sln add src/Paperbunkr.Engine.Tests/Paperbunkr.Engine.Tests.csproj`.
- `PackageIdentityTests.cs`: a package folder with `plugin.xml` (`key="x" name="Real Name"`) reads
  `Key == "x"`, `Name == "Real Name"` even with a differently-named `package.ini` present; a folder
  with `plugin.xml` missing `name` falls back to `package.ini`'s `Name`; a folder with no
  `plugin.xml` at all falls back to the path-hash for `Key` (assert it's non-empty and stable
  across two `Package.CreateFromPath` calls on the *same* path) and to `FileToName` for `Name`; two
  folders named identically (`dist`) under two different parents produce two *different* `Key`
  values (proves the hash, not the bare folder name, is what's actually used).
- `dotnet test src/Paperbunkr.Engine.Tests/Paperbunkr.Engine.Tests.csproj`.

## Step 2: `PluginEngine.NativeLoadResults` + duplicate-key detection
**Files:** `src/Paperbunkr.Plugins/PluginEngine.cs` (edit), `src/Paperbunkr.Plugins.Tests/SampleNativePlugins/ThrowingNative/ThrowingNative.csproj` (new), `src/Paperbunkr.Plugins.Tests/SampleNativePlugins/ThrowingNative/ThrowingNativePlugin.cs` (new), `src/Paperbunkr.Plugins.Tests/Paperbunkr.Plugins.Tests.csproj` (edit), `src/Paperbunkr.Plugins.Tests/NativePluginTests.cs` (edit), `docs/superpowers/specs/2026-09-12-plugin-management-screen-redesign-design.md` (edit)

**What:**
- Add `public sealed record NativePluginLoadResult(INativePluginModule? Module, string? LoadError);`
  and `private readonly Dictionary<string, NativePluginLoadResult> _nativeLoadResults = new();` +
  `public IReadOnlyDictionary<string, NativePluginLoadResult> NativeLoadResults => _nativeLoadResults;`
  on `PluginEngine`. Clear `_nativeLoadResults` in `Discover()` alongside the existing `_commands.Clear()`.
- Rewrite `DiscoverNative`: after computing `pluginKey`, if `_nativeLoadResults.ContainsKey(pluginKey)`
  already, write `_nativeLoadResults[pluginKey] = new(null, $"Duplicate plugin key '{pluginKey}' - more than one installed plugin folder declares this key.")`
  and return (contributes no commands for this second occurrence). Otherwise, on success, write
  `_nativeLoadResults[pluginKey] = new(module, null)` (capturing the module the existing
  `(_, commands) = PluginLoadContext.LoadPlugin(...)` call currently discards — change to
  `(module, commands) = ...`) before adding commands; on the existing `catch (Exception ex)`, write
  `_nativeLoadResults[pluginKey] = new(null, ex.Message)` instead of swallowing silently.
- New `ThrowingNative` fixture project, mirroring `SampleNativePlugins/MinimalNative/` exactly
  (same `net10.0`/`ImplicitUsings`/`Nullable` PropertyGroup, one `ProjectReference` to
  `Paperbunkr.Plugins.Abstractions.csproj`): `ThrowingNativePlugin : INativePluginModule` whose
  constructor throws `new InvalidOperationException("Simulated native plugin load failure.")`
  (empty `Initialize`/`RegisterCommands` bodies — never reached).
- `Paperbunkr.Plugins.Tests.csproj`: add `<ProjectReference Include="SampleNativePlugins\ThrowingNative\ThrowingNative.csproj" />`
  and `<Compile Remove="SampleNativePlugins\ThrowingNative\**\*.cs" />` (same nested-SDK-project
  globbing guard `MinimalNative` already needed).
- Correct the design doc's §4.2 paragraph starting "What the indexer-overwrite *would* do
  silently..." to describe the real, single-shared-slot behavior (see this plan's header) instead
  of "the first-discovered package is unaffected."

**Depends on:** none (independent of Step 1 — both touch different projects)

**Verify:**
- `NativePluginTests.cs`: parameterize the existing `WriteNativeManifest` helper to accept an
  explicit `key` (default `"minimal-native"`, preserving every existing test's call sites
  unchanged). Add: (a) a successful load records `NativeLoadResults["minimal-native"].LoadError == null`
  and `.Module is not null`; (b) loading `ThrowingNativePlugin` (new manifest, key
  `"throwing-native"`, assembly path via `typeof(ThrowingNativePlugin).Assembly.Location`) records
  a non-null `LoadError` containing "Simulated native plugin load failure" and contributes zero
  commands with that key; (c) two manifests in differently-named subfolders (e.g. `aaa-first`/
  `zzz-second`, to keep enumeration order deterministic) both declaring `key="minimal-native"`
  leave `NativeLoadResults["minimal-native"].LoadError` containing "Duplicate plugin key".
- `dotnet test src/Paperbunkr.Plugins.Tests/Paperbunkr.Plugins.Tests.csproj`.

## Step 3: `PluginHostService` — settings + master enable/disable
**Files:** `src/Paperbunkr.App/Plugins/PluginHostService.cs` (edit)

**What:** Add two methods:
- `public async Task OpenPluginSettingsAsync(string pluginKey)`: look up
  `Engine.NativeLoadResults.GetValueOrDefault(pluginKey)?.Module as INativePluginSettingsUi`; if
  null, return. Otherwise call `CreateSettingsView(_environment as INativePluginUiEnvironment)` —
  guard the cast (the one real `_environment` is always `PaperbunkrNativePluginEnvironment`, which
  already implements `INativePluginUiEnvironment`, but keep the null-check rather than a `!`
  assertion). If the returned `Control` is non-null, `await` the existing
  `INativePluginUiEnvironment.ShowModalAsync<object?>(resolve => control)` (the `resolve` callback
  is never invoked from inside — dismissal is scrim/X-only, per design §4.4) inside a
  `try { } catch (OperationCanceledException) { }` (the documented cancellation path when the user
  dismisses via scrim/X).
- `public void SetPackageEnabled(string pluginKey, bool enabled)`: `foreach (var cmd in
  Engine.AllCommands.Where(c => c.PluginKey == pluginKey)) SetCommandEnabled(cmd, enabled);` —
  reuses the existing per-command persistence, no new storage.

**Depends on:** Step 2 (`Engine.NativeLoadResults`)

**Verify:** Covered indirectly by Step 9's `PluginScreenViewModelTests` (no dedicated
`PluginHostServiceTests` addition needed — this is thin plumbing over already-tested primitives);
build `Paperbunkr.App` to confirm it compiles against the new `Engine.NativeLoadResults` shape.

## Step 4: `NativePluginModalHostViewModel` — opportunistic disposal
**Files:** `src/Paperbunkr.App/ViewModels/NativePluginModalHostViewModel.cs` (edit), `src/Paperbunkr.App.Tests/NativePluginModalHostViewModelTests.cs` (new, if no such test file exists — check first)

**What:** In `Advance()`, before `HostedContent = null;`, add:
```csharp
(_current?.Content.DataContext as IDisposable)?.Dispose();
(_current?.Content as IDisposable)?.Dispose();
```
(`_current` is still the outgoing `PendingModal` at that point — read `.Content` off it rather than
the already-nulled `HostedContent` field order; reorder the existing `Advance()` body so this runs
before `_current = null;` too.)

**Depends on:** none

**Verify:** Check whether `Paperbunkr.App.Tests` already has a test file for this ViewModel first
(`Glob src/Paperbunkr.App.Tests/*ModalHost*`) — extend it if so, else add a small new one: a
`ShowAsync<object?>` call whose `contentFactory` returns a `Control` with a `DataContext` that's a
fake `IDisposable` (a tiny test double recording `Disposed = true`); trigger `Dismiss()` (or
complete via the exposed `resolve`); assert the fake's `Disposed` is true. Also assert dismissing a
modal whose content/DataContext implement neither `IDisposable` doesn't throw. `dotnet test
src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter NativePluginModalHost`.

## Step 5: Sidebar row cleanup + delete the hook-grouping ViewModel
**Files:** `src/Paperbunkr.App/ViewModels/PluginPackageRowViewModel.cs` (edit), `src/Paperbunkr.App/ViewModels/PluginCommandRowViewModel.cs` (edit), `src/Paperbunkr.App/ViewModels/PluginGroupViewModel.cs` (delete)

**What:**
- `PluginPackageRowViewModel`: drop the constructor's `onRemoved`/`DeleteConfirm` (moves to the new
  detail VM, Step 6) and the `Description`/`HasDescription` properties (not shown in the sidebar
  per design §4.4 — they stay on the detail header instead, read directly off `Package` there).
  Keep `Name`/`IsNativeTier`/`IsPending`. Add a `SelectCommand` (`[RelayCommand] private void
  Select() => _onSelect(this);`, constructor takes `Action<PluginPackageRowViewModel> onSelect`)
  and a `HealthState` (`bool?` — `true` "healthy", `false` "broken", `null` never used here; simpler
  than the tri-state master-toggle case since a package only has two dot states, healthy or broken
  — expose as a plain `bool IsBroken` computed from whatever `PluginScreenViewModel.Refresh()`
  passes in via constructor, sourced from §4.3's health rule using `Engine.NativeLoadResults` +
  command `IsBroken` state).
- `PluginCommandRowViewModel`: delete the `Package` property entirely (design §4.1 — the column has
  no reason to exist once commands are shown under their real package in the detail pane).
- Delete `PluginGroupViewModel.cs` (grepped: only consumer is `PluginScreenViewModel`, updated in
  Step 7).

**Depends on:** none (View/other ViewModels not updated to match yet — that's Steps 6-8; this repo
will not build again until Step 7 finishes, which is expected mid-refactor)

**Verify:** Deferred to Step 9's full rebuild — this step alone leaves the solution non-compiling
(callers still reference the old shape), which is fine given how tightly coupled Steps 5-7 are;
don't run `dotnet build` until Step 7 is done.

## Step 6: New `PluginPackageDetailViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/PluginPackageDetailViewModel.cs` (new)

**What:** Constructed per selected package by `PluginScreenViewModel` (Step 7) with the package's
`PackageManager.Package`, its scoped `IReadOnlyList<Command>` (already filtered by `PluginKey`),
the owning `PluginHostService`, and callbacks for remove/refresh-parent. Exposes:
- Header fields read straight off `Package`: `Name`, `Version`, `Author`, `IsNativeTier`,
  `IsPending`, and `LoadError` (from `_host.Engine.NativeLoadResults.GetValueOrDefault(Package.Key)?.LoadError`,
  null for script-tier or a healthy native package).
- `bool HasConfigure` — true when `_host.Engine.NativeLoadResults.GetValueOrDefault(Package.Key)?.Module`
  is `INativePluginSettingsUi`. `[RelayCommand] private async Task Configure() => await
  _host.OpenPluginSettingsAsync(Package.Key);`
- `bool ShowReload` — true when `!Package.IsNativeTier`. `[RelayCommand] private void Reload() {
  _host.RediscoverPlugins(); _onRefreshRequested(); }` (the parent re-selects/rebuilds this detail
  VM from fresh engine state — simplest way to reflect a cleared `IsBroken` after a script fix,
  matching design §4.4's Reload button).
- `[RelayCommand] private void CopyError() => _filePicker.SetClipboardTextAsync(LoadError ?? "")`
  — reuses the existing `IFilePickerService.SetClipboardTextAsync` (already used elsewhere in this
  codebase per `PluginScreenViewModelTests`'s fakes) rather than reaching for
  `TopLevel.Clipboard` directly, keeping this testable the same way the rest of the screen is.
  `bool CanCopyError => LoadError is not null;`
- `EnabledState` (`bool?`, computed once at construction from the scoped commands: `true` if all
  `Enabled`, `false` if none, `null` otherwise — recomputed by a `Recompute()` method the parent
  calls after any mutation instead of via its own polling).
- `[RelayCommand] private void MasterEnabledClick() { bool next = EnabledState != true;
  _host.SetPackageEnabled(Package.Key, next); _onRefreshRequested(); }` — per design §4.4, this is
  the only call site that ever changes `EnabledState`; the view's `IsChecked` binds one-way to it.
- `TwoStepConfirm DeleteConfirm` (moved from `PluginPackageRowViewModel`, same `TwoStepConfirm`
  pattern, `onRemoved` callback provided by the parent).
- `ObservableCollection<PluginCommandRowViewModel> Commands` — built once at construction from the
  scoped command list (reuses the existing row VM unchanged, per design §4.5).

**Depends on:** Step 3 (`OpenPluginSettingsAsync`/`SetPackageEnabled`), Step 5 (`PluginCommandRowViewModel`'s
final shape)

**Verify:** Deferred to Step 9 (same reasoning as Step 5 — this only compiles once Step 7 wires it
in).

## Step 7: `PluginScreenViewModel` restructure
**Files:** `src/Paperbunkr.App/ViewModels/PluginScreenViewModel.cs` (edit)

**What:**
- Replace `ObservableCollection<PluginGroupViewModel> Groups` with `ObservableCollection<PluginPackageRowViewModel>
  Packages` (sidebar) — note this reuses the existing `Packages` property name/type that already
  exists today for the old flat Packages-panel, but its *construction* changes: instead of being a
  separate list from the commands (today's shape), each row now also carries its computed health
  and a `SelectCommand`. Add `[ObservableProperty] private PluginPackageDetailViewModel?
  _selectedPackageDetail;` and `[ObservableProperty] private string _filterText = "";`.
- Add a `FilteredPackages` computed view (`IEnumerable<PluginPackageRowViewModel>` — either a
  manual filter re-applied in `OnFilterTextChanged` into a second `ObservableCollection`, or a
  `IReadOnlyList` recomputed and reassigned; match whichever pattern this codebase already uses
  elsewhere for a filtered list bound to XAML — check `LibraryScreenViewModel`'s own filter
  property shape first and mirror it) — case-insensitive substring match on `Name`.
- `Refresh()`: rebuild `Packages` from `_packageService.GetPackages()` same as today, but each
  `PluginPackageRowViewModel` now also takes a `SelectCommand` callback (`row => SelectPackage(row)`)
  and its computed `IsBroken` (per §4.3: for a native package, true when
  `_host.Engine.NativeLoadResults.GetValueOrDefault(package.Key)?.LoadError is not null`; for
  either tier, also true when every one of that package's own commands — filtered by
  `PluginKey == package.Key` from `_host.Engine.AllCommands` — has `IsBroken`, while at least one
  *other* package in this same `Refresh()` pass produced healthy commands, per §4.3's "not just a
  by-design zero-command config package" carve-out). Delete the old hook-grouping loop entirely.
  If `SelectedPackageDetail` pointed at a package that's gone (removed), clear it.
- `SelectPackage(PluginPackageRowViewModel row)`: constructs a new `PluginPackageDetailViewModel`
  from `row.Package`, the commands filtered to `c.PluginKey == row.Package.Key`, `_host`, and
  callbacks (`onRemoved: () => RemovePackage(row.Package)`, `onRefreshRequested: Refresh`) and
  assigns it to `SelectedPackageDetail`.
- `InstallPackage()`/`RemovePackage()` keep their existing bodies (install flow untouched per
  design §3's non-goals) but call the new `Refresh()` shape at the end instead of the old one.

**Depends on:** Step 5, Step 6

**Verify:** Full rebuild now becomes possible — `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`.
Fix any remaining compile errors before Step 8 (the View still references the old `Groups`
property and will fail to build until Step 8 rewrites it, but the ViewModel project itself should
compile standalone at this point since Views compile as part of the same project — build errors
here are expected until Step 8 lands; don't chase them individually, just confirm this step's own
C# logic is internally consistent by inspection before moving on).

## Step 8: `PluginScreen.axaml` master-detail rewrite
**Files:** `src/Paperbunkr.App/Views/PluginScreen.axaml` (edit)

**What:** Replace the whole `Grid RowDefinitions="Auto,*"` body with a two-column
`Grid ColumnDefinitions="220,*"` (per design §4.4, plain `Grid` matching this app's existing
hand-rolled panels, not `SplitView`/`NavigationView`):
- **Column 0 (sidebar):** a `TextBox` bound `Text="{Binding FilterText}"` with a search-style
  watermark, above a `ScrollViewer`/`ItemsControl` over `FilteredPackages`
  (`SelectCommand="{Binding SelectCommand}"` per row via a `Button`/clickable row template showing
  a health-dot `Ellipse`/`Border`, `Name`, tier/pending badges — reuse the existing badge XAML
  verbatim from the current Packages-panel `DataTemplate`), then an "Install Package…" `Button`
  bound to `InstallPackageCommand` pinned to the bottom of the column (`Grid.Row` inside a nested
  `Grid RowDefinitions="Auto,*,Auto"`: filter box / list / install button).
- **Column 1 (detail):** `IsVisible="{Binding SelectedPackageDetail, Converter={x:Static
  ObjectConverters.IsNotNull}}"` wrapping the whole detail pane, with a sibling "nothing selected"
  placeholder (reuse the current empty-state icon/copy from the `!HasPlugins` block, adapted to
  "select a plugin" wording) shown when null. Inside: header
  (`Name`/`Version`/`Author`/badges/`LoadError` banner with "Copy error" button next to it,
  conditionally shown), a Reload button (`IsVisible="{Binding ShowReload}"`), the master `CheckBox`
  exactly as specified in design §4.4 (`IsThreeState="True"`, `IsChecked="{Binding EnabledState,
  Mode=OneWay}"`, `Command="{Binding MasterEnabledClickCommand}"`), the Configure button
  (`IsVisible="{Binding HasConfigure}"`, `Command="{Binding ConfigureCommand}"`), the Remove button
  (`Content="{Binding DeleteConfirm.Label}"`, `Command="{Binding DeleteConfirm.TriggerCommand}"` —
  same `TwoStepConfirm` binding pattern the current Packages panel already uses), then an
  `ItemsControl` over `Commands` reusing the existing per-command `DataTemplate` (Name/enabled
  toggle/Run button/compile-error/Configure gear) verbatim, minus the now-deleted `Package` column.
- Update `x:DataType`/bindings as needed; `PluginScreenViewModel` (the screen's own `DataContext`)
  now exposes `FilteredPackages`/`SelectedPackageDetail` instead of `Groups`.

**Depends on:** Step 7

**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` — per this project's own
Avalonia-View build gotcha (CLAUDE.md), if this fails after `CoreCompile` already produced output,
delete `obj/Debug/net10.0/Paperbunkr.App.dll`/`.pdb` before rebuilding rather than trusting a bare
retry. `AVLN5001` obsolete-`Watermark` warnings are fine (pre-existing elsewhere in this codebase
per Step 6's own build log this session) — don't chase them.

## Step 9: Rewrite `PluginScreenViewModelTests.cs` for the new shape
**Files:** `src/Paperbunkr.App.Tests/PluginScreenViewModelTests.cs` (edit — substantial rewrite)

**What:** The existing tests assert behavior this redesign deliberately removes (grouping by hook,
a `Package` column read from `package.ini`) — rewrite rather than patch. Keep the existing test
infrastructure (constructor test-seam, `MakeEnvironment`/`MakeTempDir`/`BuildFlatZip`/stub
classes) unchanged; replace the test bodies:
- `Selecting_a_package_populates_its_detail_with_only_its_own_commands` (replaces
  `Refresh_GroupsAcrossPlugins_ByHookLabel...`): two script-tier plugin folders like the old test's
  setup, `vm.Packages` has 2 rows, selecting plugin A's row makes `vm.SelectedPackageDetail.Commands`
  contain only A's commands (by `PluginKey`), not B's.
- `Refresh_WithNoHost_LeavesPackagesEmpty` (renamed from `..._LeavesHasPluginsFalse` — `HasPlugins`
  no longer exists in the new shape; assert `Packages` is empty instead).
- Keep `InstallPackage_ExtractsTheZipAndListsIt_ThenRediscoveryShowsItsCommand`,
  `InstallPackage_WithANativeTierZip_ShowsTrustNoticeAndStaysPendingUntilRestart`,
  `InstallPackage_WithAnUnreadableZip_LeavesPackagesEmpty_AndDoesNotThrow`,
  `RemovePackage_ViaTwoStepConfirm_RequiresASecondTrigger_ThenDeletesTheFolder` — adjust only the
  final assertions that referenced the old `Groups`/row shape (e.g. the install test's
  `startupGroup` assertion becomes "select the newly installed package, assert its detail's
  `Commands` contains the new command").
- New tests for this redesign's actual new behavior: `EnabledState` computes `true`/`false`/`null`
  correctly for all-enabled/all-disabled/mixed commands under one package; invoking
  `MasterEnabledClickCommand` (never by setting `IsChecked` directly) from `false` and from `null`
  both bulk-enable, from `true` bulk-disables (external review round 2's specific case); a native
  package whose `NativeLoadResults` module implements a test-double `INativePluginSettingsUi`
  shows `HasConfigure == true`, one that doesn't shows `false`; a package with a recorded
  `LoadError` shows it in the detail VM and `CanCopyError` is true; the filter `TextBox`'s bound
  `FilterText` narrows `FilteredPackages` by a case-insensitive name substring.

**Depends on:** Steps 5-8 all landed and building

**Verify:** `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter
PluginScreenViewModelTests`.

## Step 10: Full verification pass
**Files:** none (verification only)

**What:**
- `dotnet build Paperbunkr.sln` — 0 errors, confirms `Paperbunkr.Engine.Tests` and the
  `ThrowingNative` fixture are correctly wired into the solution alongside everything else.
- `dotnet test src/Paperbunkr.Engine.Tests/Paperbunkr.Engine.Tests.csproj`
- `dotnet test src/Paperbunkr.Plugins.Tests/Paperbunkr.Plugins.Tests.csproj`
- `dotnet test src/Paperbunkr.App.Tests/Paperbunkr.App.Tests.csproj --filter "PluginScreenViewModelTests|NativePluginModalHost"`
- Manual on-screen check (per design §5 — this is real UI-rearrangement work, tests alone don't
  cover it): launch Paperbunkr, open Preferences → Plugins. Both real installed packages
  (`ClusterLibraryManager`, native; `Duplicate Finder`, script-tier) should appear in the sidebar.
  Select ClusterLibraryManager: confirm its "Full read/write access" badge, its Configure button
  opens the real `SettingsRootView` in the existing modal overlay (close via both the corner X and
  a scrim click — both should work per Step 4), toggle master Enabled off then on and confirm its
  commands' individual toggles follow both times. Select Duplicate Finder: confirm it renders with
  no Configure button (script-tier, no `INativePluginSettingsUi`) and its Reload button is visible.
  Type into the sidebar filter box and confirm both rows filter correctly.

**Depends on:** Steps 1-9
