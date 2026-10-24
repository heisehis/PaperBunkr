# Reading List source marks — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-15-reading-list-source-logos-design.md*

## Step 1: Resolve bundled marks by source
**Files:** `src/Paperbunkr.Data/ReadingLists/Sources/ReadingListSourceRegistry.cs`, `src/Paperbunkr.App/Assets/Marks/SOURCES.md`, existing service-mark assets.
**What:** Add an explicit optional mark key for each registered source; map ComicVine and Metron/MetronLists to their existing assets and keep the four unavailable marks null.
**Depends on:** none.
**Verify:** registry tests cover known, missing, and unknown source keys.

## Step 2: Add the detail-header mark
**Files:** `src/Paperbunkr.App/ViewModels/ReadingListPageViewModel.cs`, `src/Paperbunkr.App/Views/ReadingListPageView.axaml`, existing `BrandMark`/`MarkResolver` integration, focused Reading Lists view tests.
**What:** Display an optional decorative source mark beside the existing source label in the detail header, retaining visible text and leaving cards/navigation unchanged.
**Depends on:** Step 1.
**Verify:** headless view test resolves ComicVine and Metron marks and shows text-only for missing/unknown marks; run focused App tests.
