# Guided View Detection Performance — Implementation Plan

*Implements: [2026-10-26-guided-view-performance-design.md](2026-10-26-guided-view-performance-design.md)*

## Step 1: Add single-flight, serialized background detection

**Files:** `src/PaperBunkr.App/ViewModels/ReaderScreenViewModel.Panels.cs`; `src/PaperBunkr.App/Services/Reader/Panels/PagePanelDetectionQueue.cs` (new); `src/PaperBunkr.App/Services/Reader/ReaderPerfStats.cs`.

**What:** Coalesce requests by issue/page/direction, serialize inference, skip queued work once its page is stale, preserve the existing bounded result cache, and dispatch visible-state updates only for the current request. Show cold analysis p50/p99/sample count in the existing reader performance overlay without adding a persistent log.

**Verify:** Inspect each request origin and dispatcher boundary; build the main App Debug target after all steps.

## Step 2: Make canvas smart double-click asynchronous

**Files:** `src/PaperBunkr.App/Views/PageCanvas.Panels.cs`; `src/PaperBunkr.App/ViewModels/ReaderScreenViewModel.Panels.cs`; existing bindings in `src/PaperBunkr.App/Views/ReaderScreen.axaml` if command/property contracts change.

**What:** Remove the synchronous ensure call from the cold path. Store a pending click with its page/view identity, request detection, then apply the existing smart panel zoom or ordinary pointer zoom when the result arrives and the view is still eligible. Preserve cached behavior and existing guided-view toggling.

**Verify:** Review the event path for both mouse double-click and touch double-tap, and confirm no hotkey is introduced outside `IInputService`.

## Step 3: Move tuning actions to the shared async path

**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.Panels.cs` and any dedicated panel request coordinator introduced in Step 1.

**What:** Show pending feedback and display the existing overlay after a current-page result. Capture report page/identity independently, then perform analysis and report writes off-thread so the requested report completes across navigation.

**Verify:** Inspect ownership/disposal paths for the report snapshot; confirm failure leaves no partial report and feedback returns through the dispatcher.

## Step 4: Review and build

**Files:** implementation files above; `docs/paperbunkr-todo.md` (update item status and evidence).

**What:** Run the Avalonia pre-delivery review checklist against affected interactions, record any visual/native checks that could not be performed, and update the roadmap status with what was verified. Preserve unrelated working-tree changes.

**Verify:** `dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj -c Debug --no-restore` from the main checkout. Do not claim a test-suite or native on-screen pass unless actually run.
