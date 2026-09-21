# Wanted screen redesign — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-21-wanted-screen-redesign-design.md*

## Step 1: Data layer for on-demand weeks
**Files:** `Paperbunkr.Data/Acquisition/PullListService.cs` (edit: range-scoped `Store`, `FetchRangeAsync`), `Paperbunkr.Data/Acquisition/PullListSourceFactory.cs` (new), `Paperbunkr.Data.Tests/Acquisition/PullListServiceTests.cs` (edit)
**What:** `Store` gains an optional scope so a one-week fetch only replaces rows inside that week; `FetchRangeAsync` fetches, stores, and looks up unknown series (capped). The factory picks Metron else ComicVine by saved credentials.
**Depends on:** none
**Verify:** new and existing `PullListServiceTests`.

## Step 2: Queue view-model (slice 1)
**Files:** `ViewModels/WantedScreenViewModel.cs` (edit), `ViewModels/WantedScreenViewModel.Queue.cs` (new), `ViewModels/QueueItems.cs` (new), `Services/AcquisitionActivityBridge.cs` (edit), `ViewModels/MainViewModel.cs` (edit: toast, confirm, progress callback)
**What:** tabs enum to Queue/Series/Releases; queue build + filter + sort + expansion + in-place reconcile; downloads strip with speed/ETA; group bulk actions; toast-based messages; banner.
**Depends on:** none
**Verify:** rewritten `WantedScreenViewModelTests`, `WantedDownloadsTests`, `WantedNavigationTests`.

## Step 3: Shell and Queue views (slice 1)
**Files:** `Views/WantedScreen.axaml(.cs)` (rewrite as shell), `Views/Wanted/QueueView.axaml(.cs)` (new)
**What:** tab bar, banner, Queue: downloads strip, chips, sort, virtualized list with three templates.
**Depends on:** Step 2
**Verify:** view construction + virtualization tests; `avalonia-pro-max/review-checklist`.

## Step 4: Series (slice 2)
**Files:** `ViewModels/WantedScreenViewModel.Series.cs` (new), `Views/Wanted/SeriesView.axaml(.cs)` (new)
**What:** list restyle, Track-a-series flyout, Untrack with confirm.
**Depends on:** Steps 2, 3
**Verify:** VM tests for untrack; view construction.

## Step 5: Releases (slice 3)
**Files:** `ViewModels/WantedScreenViewModel.Releases.cs` (rewrite), `Views/Wanted/ReleasesView.axaml(.cs)` (new), `MainViewModel.cs` (edit: fetch callback)
**What:** week paging, day groups, calendar popup with dots, on-demand fetch, tile actions and overflow menu.
**Depends on:** Steps 1–3
**Verify:** rewritten `WantedReleasesTests`; 150-tile layout test.

## Step 6: Finish
**Files:** `docs/paperbunkr-todo.md`, wiki page if the screen is described
**What:** full build, full test run, checklist review, roadmap doc update.
