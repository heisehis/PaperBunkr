# Continuity event details — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-15-continuity-event-details-design.md*

## Step 1: Persist event cover and curated tags
**Files:** `src/Paperbunkr.Data/Entities/StoryEvent.cs`, new `StoryEventTag.cs` and cover provenance type, `src/Paperbunkr.Data/PaperbunkrDbContext.cs`, migration and model snapshot.
**What:** Add nullable cover URL/provenance and categorized curated event tags, leaving dates nullable and existing descriptions untouched.
**Depends on:** none.
**Verify:** migration tests upgrade existing events without fabricating metadata; entity persistence tests cover tags and provenance.

## Step 2: Resolve provider details and event tag projection
**Files:** new event metadata/tag projection service under `src/Paperbunkr.Data/Metadata/`, existing ComicVine/Metron arc overview adapters, `src/Paperbunkr.Data.Tests/StoryEventIdentity/` tests.
**What:** Fetch only for known, compatible provider IDs when details are missing; apply Metron-before-ComicVine cover precedence, preserve manual covers/non-empty descriptions, and project de-duplicated Genre/Tags from event member issues without persisting them as curated tags.
**Depends on:** Step 1.
**Verify:** fake-provider tests cover precedence, conflict, offline/failure, empty-field fill, and projected tag refresh.

## Step 3: Load expanded event details into Continuity
**Files:** `src/Paperbunkr.App/Services/ContinuityScreen/ContinuityOverview.cs`, `ContinuityOverviewLoader.cs`, `src/Paperbunkr.App/ViewModels/ContinuityPageViewModel.cs`, focused Continuity tests.
**What:** Extend event rows with dates, description, cover identity, curated/projected tags, and persisted related-event labels/targets; keep current chronology and progress calculations.
**Depends on:** Steps 1–2.
**Verify:** loader/view-model tests validate row order, event details, related navigation data, and progress invariance.

## Step 4: Edit event metadata
**Files:** `src/Paperbunkr.App/ViewModels/NewEventOrContinuityViewModel.cs`, `src/Paperbunkr.App/Views/NewEventOrContinuityOverlay.axaml`, existing cover picker integration, related tests.
**What:** Extend the existing Edit Event flow with dates, curated tags, and manual cover select/clear; retain the separate Continuity editor behavior and existing name/description fields.
**Depends on:** Step 1.
**Verify:** edit tests cover nullable date persistence, tag categories/weights, and manual cover precedence.

## Step 5: Render the Continuity event details
**Files:** `src/Paperbunkr.App/Views/ContinuityOverviewView.axaml`, code-behind only if needed, matching Avalonia XAML/style/view tests.
**What:** Keep compact existing rows and chronology when collapsed; add an explicit expansion affordance with event artwork, description, categorized tags, and related-event links using current Continuity styling.
**Depends on:** Step 3.
**Verify:** XAML compilation, focused UI tests, manual visual check; run `avalonia-pro-max/review-checklist` before completion.
