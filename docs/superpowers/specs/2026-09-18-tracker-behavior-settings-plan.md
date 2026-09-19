# Tracker behavior settings — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md*

## Step 1: Data model + migration
**Files:** `Data/Entities/TrackerAutoUpdateMode.cs` (new), `Data/Entities/AppSettings.cs`, `Data/Entities/Series.cs`, `Data/PaperbunkrDbContext.cs`, new migration `AddTrackerBehaviorSettings`, `Data.Tests/AddTrackerBehaviorSettingsMigrationTests.cs` (new)
**What:** 5 AppSettings columns + `Series.TrackerPromptShown`, HasDefaultValue in DbContext, `dotnet ef migrations add`, Down no-op per convention.
**Verify:** `has-pending-model-changes`; migration test (no up-down-up).

## Step 2: Shared adapter factory + provider map
**Files:** `Data/Tracking/TrackerAdapterFactory.cs`, `Data/Tracking/TrackerProviderMap.cs` (new), `App/ViewModels/DetailTabsViewModel.cs` (switch to factory)
**What:** one place for connected-check + adapter + detailed push; `ExternalMetadataProvider` <-> `TrackingService` map.
**Verify:** existing DetailTabs/tracker tests + new factory/map tests.

## Step 3: TrackerAutoSyncService
**Files:** `App/Services/ITrackerAutoSyncService.cs`, `TrackerAutoSyncService.cs`, `IToastHost.cs` (new); `MainViewModel.cs` (wire, ToastHost impl)
**What:** push flow, pull flow, pacing, coalescing, Activity Center job/toast/alert per spec §3.6/3.7/§4, Ask prompt.
**Verify:** `TrackerAutoSyncServiceTests` with fake adapters.

## Step 4: Preferences
**Files:** `PreferencesScreenViewModel.cs`, `Views/Preferences/ConnectionsSection.axaml`, `Models/PreferenceIndex.cs`, tests
**Verify:** VM load/persist tests, build.

## Step 5: Hooks
**Files:** `ReaderScreenViewModel.cs`, mark-read sites (Detail/MangaDetail/Library/Reading VMs), `DetailTabsViewModel` (pull on load, auto-open, pin), `TrackerMatchSample`
**Verify:** hook tests, wiring test.

## Step 6: Docs
Roadmap entry; wiki guide deferred to commit time (memory).
