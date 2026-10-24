# Reading List page scrolling — Implementation Plan
*Implements: [2026-10-19-reading-list-page-scroll-design.md](2026-10-19-reading-list-page-scroll-design.md)*

## Step 1: Width-aware two-row tag wall
**Files:** `src/Paperbunkr.App/Controls/CollapsibleTagWall.cs` (new), `src/Paperbunkr.App/ViewModels/ReadingListPageViewModel.cs` (edit), `src/Paperbunkr.App/Views/ReadingListPageView.axaml` (edit), `src/Paperbunkr.App.Tests/CollapsibleTagWallTests.cs` (new), `src/Paperbunkr.App.Tests/ReadingLists/ReadingListsScreenViewTests.cs` (edit).
**What:** Add a measured wrap panel that lays out complete tag chips in at most two rows while collapsed, reports hidden-item count, and excludes hidden chips from focus/accessibility. Add transient expanded state and a toggle to the existing view model. Keep the add-tag action outside the capped rows. Reset expansion when another list is loaded.
**Depends on:** none.
**Verify:** Pure layout tests cover row assignment, exact-fit/overflow, narrow widths, and expansion; view tests cover collapsed/expanded states, hidden focus targets, add-tag availability, and reset on list change.

## Step 2: Unified page scrolling
**Files:** `src/Paperbunkr.App/Views/ReadingListPageView.axaml` (edit), `src/Paperbunkr.App/Views/ReadingListPageView.axaml.cs` (edit), `src/Paperbunkr.App.Tests/ReadingLists/ReadingListsScreenViewTests.cs` (edit).
**What:** Place the main page content in its vertical page scroller and remove the Path and Covers issue-local scroll viewers. Preserve the existing virtualizing panels, focus reclaim, and `BringIntoView` behavior using the outer page's effective viewport. Leave the cover rail's separate rail navigation intact.
**Depends on:** none; coordinate XAML changes with Step 1.
**Verify:** Extend view tests to prove the page scroller owns the entire content flow, there are no issue-local scroll viewers, and both path and covers remain virtualized.

## Step 3: Hero parallax and integration review
**Files:** `src/Paperbunkr.App/Views/ReadingListPageView.axaml` (edit), `src/Paperbunkr.App.Tests/ReadingLists/ReadingListsScreenViewTests.cs` (edit), `src/Paperbunkr.App.Tests/HomeEffectControlsTests.cs` (edit only if a missing assertion is needed).
**What:** Put the existing `ParallaxBackdrop` behind the hero gradient, bound to the existing hero image. Confirm it observes the page scroller and does not intercept input or alter hero layout. Reuse the existing `MotionPolicy` behavior and reduced-motion coverage.
**Depends on:** Step 2, because the hero must observe the new page scroller.
**Verify:** Test the backdrop's ancestor scroller and the already-covered bounded/reduced-motion offset behavior; build the App project and load the compiled view through focused headless tests.

## Final verification
- Run `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~ReadingListsScreenViewTests"` and the focused tag-panel tests, then `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~HomeEffectControlsTests"` if Step 3 adds or changes parallax assertions. Run these sequentially because they share output folders.
- Run the Avalonia review checklist against the affected view: theme resources only, resize behavior, visible keyboard focus, no hidden focusable tags, reduced-motion parallax, and virtualized lists. Check a many-tag list at short window height in the running app if the desktop harness is available.
- Preserve all pre-existing dirty workspace changes; stage or edit only files listed above for this feature.
