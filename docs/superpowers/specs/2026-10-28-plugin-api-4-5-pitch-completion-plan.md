# Plugin API 4.5 — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md*

Conventions: new `IPluginEnvironment` members get **default interface implementations** (a null/no-op object) so the five
existing environment implementations and every test fake keep compiling; only the real `PaperbunkrPluginEnvironment` (and the
native wrapper, which forwards) supply working ones. Every App build that the user will launch goes into the real `bin`
(memory: scratch OutDir never updates the exe). Never two `dotnet` builds at once. App tests: targeted classes only.

## Step 0: Version
**Files:** `Paperbunkr.Plugins.Abstractions/PluginApi.cs`, `Paperbunkr.Plugins.Tests/PluginActivityTests.cs` (version assertion)
**What:** `PluginApi.Current` → 4.5. **Verify:** Plugins.Tests version test.

## Slice A — developer experience
### A1 Dev mode setting + watcher
**Files:** `Paperbunkr.Data/Entities/AppSettings.cs` (+ migration `AddPluginDeveloperMode`), `Paperbunkr.App/Plugins/PluginFolderWatcher.cs` (new),
`Paperbunkr.App/Plugins/PluginHostService.cs` (dev-mode property, watcher lifetime, `PluginsReloaded` event, reload-failure toast),
`Paperbunkr.App/ViewModels/PluginScreenViewModel.cs` (toggle, posted refresh on reload), `Paperbunkr.App/Views/PluginScreen.axaml`.
**Verify:** `PluginFolderWatcherTests` (debounce collapses bursts; ignores other extensions; dispose stops), host test (reload re-discovers a new plugin folder).
### A2 Live log pane
**Files:** `Paperbunkr.App/Plugins/PluginLogFiles.cs` (`ReadTail`, `Clear`), `Paperbunkr.App/ViewModels/PluginLogPaneViewModel.cs` (new),
`PluginPackageDetailViewModel.cs`, `PluginScreen.axaml`. **Verify:** tail/clear tests; VM test with a fake clock-free `Refresh()`.
### A3 Scaffolding
**Files:** `Paperbunkr.App/Plugins/PluginScaffolder.cs` (new, pure: writes the template files), `Paperbunkr.App/ViewModels/NewPluginDialogViewModel.cs`,
`Paperbunkr.App/Views/NewPluginDialogView.axaml(.cs)` (DialogCard), `PluginScreenViewModel.cs`. **Verify:** scaffolder tests - the C# and Python
templates discover and run through the real `PluginEngine`; native template files exist and the `.csproj` names the dlls; existing key refused.
### A4 Testing harness
**Files:** `src/Paperbunkr.Plugins.Testing/` (new project, in the .sln), move `FakePluginEnvironment`, `FakeNativePluginEnvironment`, `FakePluginLogger`
from Plugins.Tests (namespace kept), add `PluginScriptRunner`; Plugins.Tests references it. **Verify:** full Plugins.Tests still green; a runner test.

## Slice B — hooks
**Files:** `PluginHooks.cs` (+2 hooks, descriptions, `DomainEventHooks`), `PluginGlobals.cs` + `PluginGlobalsTypeMap.cs` (2 globals types),
`Paperbunkr.Data/Events/LibraryEvents.cs` (2 events), `PaperbunkrDbContext.cs` (series-status detection in both SaveChanges paths),
`Paperbunkr.App/Services/Scheduling/SchedulerService.cs` (raise after each run), `PluginHostService.cs` (subscribe/dispatch).
**Verify:** Data test (status change → one event after save; no event for failed/unchanged save); scheduler test; host dispatch test.

## Slice D — data and rules
### D1 Smart fields
**Files:** `PluginManifest.cs` (`<Fields>`), `Paperbunkr.Plugins/PluginFieldSchema.cs` (parse/validate/format), `PluginEngine.cs` (`FieldSchemas`, block on error),
`Paperbunkr.Plugins.Abstractions/IPluginFields.cs`, `IPluginEnvironment.cs` (`Fields`), `Paperbunkr.App/Plugins/PluginFieldsAdapter.cs`,
`Paperbunkr.Data/SmartLists/PluginFieldTypeRegistry.cs` + `SmartListQueryBuilder.cs` (typed compare for registered custom-value names),
`SmartListConditionViewModel.cs` / smart-list editor field picker ("Plugin fields" group). **Verify:** schema tests, adapter tests, query-builder typed-compare tests, editor VM test.
### D2 Batch writes
**Files:** `IMetadataWriter.cs` (`BeginBatch`), `IMetadataWriteBatch.cs` (new), `PaperbunkrMetadataWriter.cs` (batch: record, diff, apply in one transaction),
`Paperbunkr.App/ViewModels/PluginWriteBatchDialogViewModel.cs` + view (DialogCard Large). **Verify:** diff/apply/rollback/empty tests; dialog VM test.

## Slice C — providers
**Files:** `Paperbunkr.Plugins.Abstractions/IPluginProviders.cs` (+ DTO records), `IPluginEnvironment.cs` (`Providers`), `Paperbunkr.App/Plugins/PluginProviderRegistry.cs`
(timeouts, per-plugin replace, cleared on rediscovery), `Paperbunkr.Data/ReadingLists/Sources/ReadingListSourceRegistry.cs` (external source hook) +
`PluginReadingListSource`, cover picker (`CoverPickerViewModel.cs`), metadata search (`DetailTabsViewModel.cs` provider list → proposals).
**Verify:** registry tests (timeout, exception, replace), reading-list source adapter test, cover/metadata VM tests with a registered fake provider.

## Slice E — UI surfaces
**Files:** `CommandManifestEntry`/`Command` (`menus`, `toolbar`), `PluginHostService.cs` (`GetMenuCommands(menu)`, `GetToolbarCommands`, issue-detail + Insights views),
`Paperbunkr.Plugins.Abstractions.Ui/INativeIssueDetailUi.cs`, `INativeInsightsWidget.cs`, the reading-list item menu builder, `LibraryToolbar.axaml` (+VM),
the Detail Details tab, Insights Today. **Verify:** menu filtering tests, builder tests, headless mounts where a view is added, lint run.

## Slice F — network
**Files:** `PluginManifest.cs` (`<Network>`), `PluginEngine.cs` (`NetworkHosts`), `IPluginHttp.cs` + `PluginNetworkException`, `IPluginEnvironment.cs` (`Http`),
`Paperbunkr.App/Plugins/PluginHttp.cs`, install confirm (`PluginScreenViewModel.InstallPackage` + detail line). **Verify:** host matching (exact, wildcard), blocked call throws + logs, install-confirm text tests.

## Finish
Wiki (`wiki/Plugins.md` 4.5 sections), roadmap items 1–14 statuses, `docs/paperbunkr-todo.md` entry, spec "as implemented" notes, memory.
Build the App into its real `bin` (forced recompile for new XAML), lint tests, targeted App classes, full Plugins.Tests and the touched Data classes.
