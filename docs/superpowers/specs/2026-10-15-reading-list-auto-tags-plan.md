# Reading List automatic tags — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-15-reading-list-auto-tags-design.md*

## Step 1: Persist tag provenance
**Files:** `src/Paperbunkr.Data/Entities/ReadingListTag.cs`, new `ReadingListTagOrigin.cs`, `src/Paperbunkr.Data/PaperbunkrDbContext.cs`, migration and model snapshot.
**What:** Add a backwards-compatible manual/generated marker and stable generated rule key. Existing rows remain user-created; add indexes needed to reconcile generated rows without duplicate identities.
**Depends on:** none.
**Verify:** migration test upgrades a legacy tag and preserves its value, category, and weight.

## Step 2: Generate and reconcile tags
**Files:** new `src/Paperbunkr.Data/ReadingLists/ReadingListTagGenerator.cs` and `ReadingListTagReconciler.cs`; focused `src/Paperbunkr.Data.Tests/ReadingLists/` tests; relevant list mutation/build call sites.
**What:** Generate Source, list Type, Story Event, Continuity, and member-issue Genre/Tags values; normalize/deduplicate by value and category; transactionally diff only generated rows. Connect list metadata, membership, and issue-tag changes, and add an idempotent bounded local backfill.
**Depends on:** Step 1.
**Verify:** generator/reconciler tests for every source, category, weight, manual overlap, removal, retry, and backfill behavior.

## Step 3: Present generated tags safely
**Files:** `src/Paperbunkr.App/ViewModels/TagPillViewModel.cs`, `ReadingListPageViewModel.cs`, `src/Paperbunkr.App/Views/ReadingListPageView.axaml`, focused Reading Lists view-model/view tests.
**What:** Mark generated chips and explain their origin; prevent generated rows from being reweighted or removed through manual-tag commands; keep filtering compatible with all tags.
**Depends on:** Step 1.
**Verify:** focused `Paperbunkr.App.Tests` Reading Lists tests and `dotnet test src/Paperbunkr.App.Tests --filter "Speed!=Slow"`.
