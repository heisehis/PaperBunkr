# Continuity map M2 Layout — implementation plan

*Implements [the approved design](2026-11-11-continuity-map-layout-design.md), on top of the M1 working tree. Preserve M1's pending on-screen review and legacy cleanup.*

## Step 1: Pure layout and navigation
**Files:** `src/Paperbunkr.App/Services/EventMap/EventMapLayout.cs`, `EventMapModels.cs`, `ContinuityMapLoader.cs`, `src/Paperbunkr.App.Tests/EventMap/EventMapLayoutTests.cs` and neighboring layout tests.
**What:** Add relay wave packing and one-per-column option, packed Event/Unplaced blocks, position ranges and same-column edge geometry. Fold eligible short lanes after filters, preserving per-series colors and spine edges; add hide-read filtering. Preserve row-order navigation and month-packed blocks.
**Verify:** Pure tests for packing, edge direction, folding thresholds/collisions/colors, filtering and navigation.

## Step 2: Relation spine and lane metadata
**Files:** `src/Paperbunkr.Data/Metadata/EventSpineResolver.cs`, `Metadata/SeriesConnector/SeriesConnectorSnapshot.cs`, `EventTieInInference.cs`, `src/Paperbunkr.App/Services/EventMap/SpineResolver.cs`, `EventMapLoader.cs`, `ContinuityMapLoader.cs`, `EventMapModels.cs`, and related Data/App tests.
**What:** Load relation evidence and apply the user-only spine signal with the specified precedence and tie-breaks in both map and connector; carry relation labels to map tracks.
**Verify:** Resolver and connector tests cover user and inferred evidence, precedence and ties; loader tests cover lane labels.

## Step 3: Settings and interaction
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs`, EF migration and snapshot, `src/Paperbunkr.App/ViewModels/EventMapViewModel*.cs`, `ContinuityScreenViewModel.cs`, `src/Paperbunkr.App/Views/EventMapView.axaml` and code-behind, `Controls/EventMap/EventMapCanvas*.cs` and relevant tests.
**What:** Persist Packed default true; add toolbar choices, fold controls and session state, Hide read and its deferred selected-card removal, empty state, labels and rendered edges. Follow `avalonia-xaml`, `avalonia-mvvm`, `avalonia-custom-controls`, `avalonia-input-interaction`, and Paperbunkr design language; shortcuts continue through the input service.
**Verify:** Migration forward/default and Down inspection; headless tests for persistence, folding, Hide read selection and relation text; theme renders and real-window interaction.

## Step 4: Integration and documentation
**Files:** M2 design and pitch, `docs/paperbunkr-todo.md`, `wiki/Story-Events-and-Relations.md`.
**What:** Run relevant Data and App tests sequentially, app build and UI lint; load `avalonia-pro-max/review-checklist`; update verified status and manual limitations. Do not apply the migration to the user's database during tests.
**Verify:** Targeted suites and real-window checks; document on-screen checks still needed.
