# Keyboard reach for Detail, Home, Smart Lists and Insights — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-detail-home-smart-design.md*

## Step 1: PosterTile keyboard
**Files:** Views/PosterTile.axaml (edit), Views/PosterTile.axaml.cs (edit)
**What:** Focusable root, adorner off, Enter/Space/Delete, Left/Right/Home/End along the shelf, Up/Down via `FocusManager.FindNextElement`.
**Verify:** `PosterTileKeyboardTests` (open, dismiss, shelf walk, up/down between shelves).

## Step 2: Home reclaim
**Files:** Views/HomeScreen.axaml.cs (edit)
**What:** `FocusReclaimer` over `Sections` and each shelf collection; default target the first tile.
**Depends on:** Step 1
**Verify:** `HomeAndSmartFocusTests` (lands, survives rebuild, no steal).

## Step 3: Smart Lists
**Files:** Views/SmartScreen.axaml (edit), Views/SmartScreen.axaml.cs (edit)
**What:** Arrow handler on series/novel cards, reclaimer over the result collections, Grouped Review focus in, restore and Escape.
**Verify:** `HomeAndSmartFocusTests` (lands, survives reload, Escape closes and focus stays inside).

## Step 4: Comic detail
**Files:** Views/DetailTabs.axaml (edit), Views/DetailTabs.axaml.cs (edit)
**What:** Enter opens, Space focuses, Ctrl/Shift+Space toggles; reclaimer on issue lists, tab and view mode; adorner off on the poster tile only.
**Verify:** `DetailTabsFocusTests`; mutation-checked by disabling the collection handler in a scratch copy (the reset test fails).

## Step 5: Manga and Book detail
**Files:** Views/MangaDetailScreen.axaml(.cs), Views/BookDetailScreen.axaml(.cs), Views/FocusReclaimer.cs (edit: `TryStepVertically`)
**What:** Reclaimers plus Up/Down stepping on chapter and bookmark rows.
**Verify:** Compiles and the existing suite passes; no headless test (needs a seeded manga/book VM). On-screen check.

## Step 6: Insights
**Files:** Views/InsightsScreen.axaml.cs (edit)
**What:** Reclaimer with the active tab header as the fallback; `ReclaimIfFocusLost` on tab flags.
**Verify:** `InsightsScreenFocusTests`.

## Step 7: Harness fix
**Files:** src/Paperbunkr.App.Tests/FocusTestHarness.cs (edit)
**What:** `Key.Enter` is an alias of `Key.Return`, so mapping it to a `PhysicalKey` name threw; map it to `Enter`.

## Step 8: Sweep
Run `avalonia-pro-max/review-checklist`, then the full suite. On-screen check: sidebar, reader adorner suppression, Home tiles, Manga/Book row stepping.
