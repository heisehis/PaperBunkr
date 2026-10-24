# Motion system v2 — Navigation — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-20-motion-system-v2-navigation-design.md*

Shared, dirty tree: targeted edits only. One build or test run at a time, and shut the build servers down after each run.

## Step 1: Intent + route content on MainViewModel
**Files:**
- `Models/RouteIntent.cs` (new).
- `ViewModels/MainViewModel.cs`: `ActiveRouteContent`, `RouteIntent`, `_pendingDrillKind` (set in `RunDrill`, consumed in
  `OnCurrentScreenChanging`), raised alongside the existing content notifications.
- `ViewModels/ITransientScreen.cs` (new), implemented on `ReaderScreenViewModel`, `BookReaderScreenViewModel` and
  `PdfPageReaderScreenViewModel`.

**Verify:** new `RouteIntentTests` (`MainViewModel`-level, using the existing test construction).

## Step 2: RouteHost
**Files:**
- `Controls/RouteHost.cs` (new): cached views, layers, intent transitions, supersession, `ScheduleAfter` seam.
- `Paperbunkr.App.Tests/Controls/RouteHostTests.cs` (new).

**Verify:** `RouteHostTests`.

## Step 3: Swap the hosts in MainWindow
**Files:**
- `Views/MainWindow.axaml`: one `controls:RouteHost` with all 17 DataTemplates.
- `Styles/AppTokens.axaml`: drop `PbScreenSlide` and `PbDrillTransition`.
- Delete `Theming/Motion/MotionAwarePageTransition.cs`.

**Verify:** build; the real-window keyboard, `NavRail`, `Wanted` in-window, `MangaSeriesEditor` and `Compare` suites.

## Step 4: Cover flight hardening
**Files:**
- `Services/SharedElementTransitionService.cs`: opacity holds, layout-driven wait, cancel on host bounds change.
- `Services/NavigationTransitionCoordinator.cs`: an owned CTS that cancels the previous flight.
- `Views/MainWindow.axaml.cs`: cancel on `WindowState` change.
- Tests: extend `NavigationTransitionCoordinatorTests`; add `SharedElementTransitionServiceTests` if no existing class covers the service.

## Step 5: Breadcrumb entrance + Library guard
**Files:**
- `Views/Breadcrumb.axaml(.cs)`.
- `Views/LibraryScreen.axaml.cs`: unsubscribe from the previous view model.

**Verify:** build; Library view tests.

## Step 6: Docs
**Files:** the spec's Status line, `docs/paperbunkr-todo.md`, the design-language skill's Motion section (RouteHost line), and memory.
