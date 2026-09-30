# Keyboard focus reclaim, Phases 2-6 — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md*

## Step 1: Extend `FocusReclaimer`
**Files:** `src/Paperbunkr.App/Views/FocusReclaimer.cs` (edit)
**What:** Add `ReclaimIfFocusWithinOrNowhere()`, `ReclaimIfFocusLost()`, the usable-focus post-check, and static `FocusFirstButton(scope, prefer)`.
**Depends on:** none
**Verify:** `FocusReclaimerTests` (new) - hidden-focused element is reclaimed; focus in a sibling region is not stolen; `ReclaimIfFocusLost` no-ops while focus is live.

## Step 2: Continuity
**Files:** `Views/ContinuityScreen.axaml.cs` (edit)
**What:** Reclaimer over the screen; fallback = active `segToggle`; triggers attach, `IsVisible`, `Page`/`DetailView`, `Timeline.Sections`.
**Depends on:** Step 1
**Verify:** `ContinuityScreenFocusTests` (new).

## Step 3: Wanted
**Files:** `Views/WantedScreen.axaml.cs` (edit)
**What:** Reclaimer; last-row memory over the visible `vlist`; fallback active tab; triggers attach, `IsVisible`, `ActiveTab`, the three row collections.
**Depends on:** Step 1
**Verify:** `WantedScreenFocusTests` (new).

## Step 4: Books
**Files:** `Views/BooksScreen.axaml.cs`, `Views/BooksScreen.axaml` (`FocusAdorner` on `Button.card`) (edit)
**What:** Reclaimer; card-list discovery by item type; remembered (list, index); triggers attach, `IsVisible`, grouping, `Books`, `Groups`.
**Depends on:** Step 1
**Verify:** `BooksScreenFocusTests` (new), including the adorner assertion.

## Step 5: Preferences
**Files:** `Views/PreferencesScreen.axaml.cs` (edit)
**What:** Reclaimer; fallback active nav item / first search result; triggers attach, `IsVisible`, `ActiveSection`, `IsSearching`.
**Depends on:** Step 1
**Verify:** `PreferencesScreenFocusTests` (new, minimal - the screen needs many theme tokens headless).

## Step 6: MainWindow contextual sidebar
**Files:** `Views/MainWindow.axaml` (`x:Name="ContextualSidebar"`), `Views/MainWindow.axaml.cs` (edit)
**What:** Window-level `GotFocus` records last-focus-in-sidebar and row index; each sidebar list's `CollectionChanged`/`ItemsSource` change calls `ReclaimIfFocusLost()` when the flag is set.
**Depends on:** Step 1
**Verify:** on-screen (delete a smart list / collection with the keyboard, focus lands on the neighbouring row); build.

## Step 7: Verification
Build; run the new test classes, then the full suite (Release if the app is running). Run `avalonia-pro-max/review-checklist` (read off disk) before calling UI work done.
Known unrelated full-suite flakes: `MatrixRainOverlayRenderTests.Overlay_VisibleFromMount_RendersGreenGlyphs`, `ReaderImagePipelineTests.GetDetailPage_ReservesAgainstBudget_EvictsDisplayPages_KeepsActivePage`.
