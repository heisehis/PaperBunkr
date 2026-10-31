# Continuity Unplaced Events and CBRO Overlap Notes — Implementation Plan

*Implements: [2026-10-25-continuity-unplaced-overlap-design.md](2026-10-25-continuity-unplaced-overlap-design.md)*

## Step 1: Capture and refresh CBRO overlap evidence

**Files:** `src/Paperbunkr.Data/Metadata/EventOrderSources.cs`, `src/Paperbunkr.Data/ReadingLists/Sources/ComicBookReadingOrdersSource.cs`, `src/Paperbunkr.Data/Entities/EventOrderEvidence.cs`, `src/Paperbunkr.Data/PaperbunkrDbContext.cs`, `src/Paperbunkr.Data/Metadata/EventOrderSweep.cs`, `src/Paperbunkr.Data/Migrations/` (generated), `src/Paperbunkr.Data.Tests/StoryEventIdentity/EventOrderTests.cs`.

**What:** Parse the three approved same-time phrasings into structured CBRO notes; persist notes with the matched event's CBRO evidence and atomically replace them on refresh. Leave Metron evidence untouched. Add the additive EF migration.

**Depends on:** none.

**Verify:** Extend fixture tests for variants, multiple notes, and HTML decoding; test refresh replacing stale notes and retaining other provider rows. Run the focused `EventOrderTests` class.

## Step 2: Build terminal Unplaced blocks and resolve overlap edges

**Files:** `src/Paperbunkr.App/Services/EventMap/EventMapModels.cs`, `src/Paperbunkr.App/Services/EventMap/ContinuityMapLoader.cs`, `src/Paperbunkr.App.Tests/EventMap/ContinuityMapTests.cs`, `src/Paperbunkr.App.Tests/ContinuityScreen/ContinuitySourcesAppTests.cs`.

**What:** Carry CBRO notes and local target candidates into continuity-map data. Partition events with no usable date, source rank, or directional relation into a terminal Unplaced region, alphabetically ordered. Keep filters, issue ordering, and shared-issue behavior. Resolve targets only by unique normalized title within the same CBRO list, using source year when present; build deduplicated, non-directional overlap connectors without adding `EventRelation` data.

**Depends on:** Step 1.

**Verify:** Test Unplaced membership, ordering, filters and relation exceptions; target matching, ambiguity, reciprocal notes, and stable chronology. Run focused Continuity map tests.

## Step 3: Show Unplaced and CBRO notes in the map UI

**Files:** `src/Paperbunkr.App/Services/EventMap/ContinuityMapLoader.cs`, `src/Paperbunkr.App/ViewModels/EventMapViewModel.Continuity.cs`, `src/Paperbunkr.App/ViewModels/EventMapCardViewModel.cs`, `src/Paperbunkr.App/Views/EventMapView.axaml`, `src/Paperbunkr.App/Controls/EventMap/EventMapRuler.cs`, `src/Paperbunkr.App.Tests/EventMap/ContinuityMapViewModelTests.cs`, `src/Paperbunkr.App.Tests/EventMap/ContinuityMapTests.cs`.

**What:** Render the terminal region label; expose event CBRO notes in the map inspector, including unresolved notes. Draw overlap connectors with the existing non-directional bracket geometry and label their provenance in the tooltip. Use app theme resources and existing map control patterns; make no screen-specific shortcut handlers.

**Depends on:** Step 2.

**Verify:** Focused view-model and ruler tests; inspect the continuity map at normal and compact widths in the main Debug app and check both theme variants. Complete the Avalonia review checklist for this screen.

## Step 4: Verify and record the completed roadmap item

**Files:** `docs/paperbunkr-todo.md`.

**What:** Run the targeted Data/App tests, build `src/Paperbunkr.App/Paperbunkr.App.csproj` in the main Debug configuration, launch the app, and record the verified completion and migration reference in the roadmap.

**Depends on:** Steps 1–3.

**Verify:** Record actual command results and the committed implementation reference if one exists; do not claim a commit before it is made.
