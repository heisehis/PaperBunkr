# Guided Event and Continuity Creation — Implementation Plan

Implements: [2026-10-16-guided-event-continuity-creation-design.md](2026-10-16-guided-event-continuity-creation-design.md)

## Step 1: Persist structured Event types

**Files:** `src/Paperbunkr.Data/Entities/StoryEvent.cs`, new `StoryEventType.cs`, new `StoryEventTypeAssignment.cs`, `src/Paperbunkr.Data/PaperbunkrDbContext.cs`, EF Core migration and model snapshot, focused `Paperbunkr.Data.Tests` tests.

**What:** Add the controlled multi-select taxonomy with a unique Event/type assignment. Keep existing Events unclassified. Add query support for types and a migration test that confirms no inferred data is written.

**Depends on:** none.

**Verify:** focused Data tests, migration/schema tests, and `dotnet build src/Paperbunkr.Data/Paperbunkr.Data.csproj`.

## Step 2: Add provider setup and staged reconciliation services

**Files:** existing `src/Paperbunkr.Data/Metadata` Event identity and relationship services, `src/Paperbunkr.Data/ComicVine/MetronClient.cs` only if an existing query is missing required fields, new focused setup/review records or coordinator, and matching Data/App tests.

**What:** Reuse current arc sources, provider clients, Metron universe/catalog lookup, exact provider IDs, cross-provider identity bridges, Event relationship resolver, Continuity resolver, and role suggestion service. Return reviewable staged details and candidates without writing. Apply confirmed provider identities/details and selected memberships/relationships transactionally. Exact ID matches reuse local entities; names alone do not merge. Preserve the existing continuity catalog-browser implementation.

**Depends on:** Step 1 for typed Event preset data.

**Verify:** service tests for exact IDs, provider conflicts, verified matches, unavailable items, role suggestions, no-write previews, stale proposals, and transaction rollback.

## Step 3: Implement the guided editor stages and presets

**Files:** `src/Paperbunkr.App/ViewModels/NewEventOrContinuityViewModel.cs`, `src/Paperbunkr.App/Views/NewEventOrContinuityOverlay.axaml`, existing main-window/dialog wiring in `src/Paperbunkr.App/ViewModels/MainViewModel.cs` and `src/Paperbunkr.App/Views/MainWindow.axaml`, plus `src/Paperbunkr.App.Tests/NewEventOrContinuityViewModelTests.cs` and focused view tests.

**What:** Add Basics → Provider setup → Review, template defaults, editable Event types, optional Alternate Universe designation, provider search/preview, staged member selection, role suggestions, optional Event relations, skip/back/cancel behavior, and safe Apply. Keep edit mode direct and support reopening setup without retaining stale staged candidates.

**Depends on:** Steps 1 and 2.

**Verify:** view-model tests for every preset, skip/resume, preview with no writes, confirmation preserving existing details, and apply failures. UI tests for stage transitions, loading/empty/error states, keyboard focus, and cancel behavior.

## Step 4: Surface Event types and setup entry points

**Files:** `src/Paperbunkr.App/Models/StoryEventSummary.cs`, `src/Paperbunkr.App/ViewModels/ContinuitySidebarViewModel.cs`, `src/Paperbunkr.App/ViewModels/EventPageViewModel.cs`, `src/Paperbunkr.App/ViewModels/ContinuityPageViewModel.cs`, `src/Paperbunkr.App/Views/EventHero.axaml`, `src/Paperbunkr.App/Views/ContinuityHero.axaml`, relevant Event picker models/views including `src/Paperbunkr.App/ViewModels/NewReadingListViewModel.cs`, and continuity/event view tests.

**What:** Show Event type chips in the hero, sidebar, and relevant pickers; add the Events type/unclassified filter; add explicit resume/setup actions from Event and Continuity details. Keep **Find series on Metron** and its existing browser fully reachable and unchanged.

**Depends on:** Steps 1 and 3.

**Verify:** focused view-model and Avalonia tests for multiple/unset types, filtering without changing ordering, resume entry points, and catalog-browser reachability.

## Step 5: Full verification and build

**Files:** any focused fixes in the files above; `docs/paperbunkr-todo.md` if the completed work changes roadmap status.

**What:** Run targeted migration, matching, view-model, and UI tests; run the project fast test set when time permits; build the normal Debug app output. Check the UI against runtime theme resources and inspect the final diff for accidental changes to unrelated work.

**Depends on:** Steps 1–4.

**Verify:** report exact test/build commands and outcomes; no claim of success without completed checks.
