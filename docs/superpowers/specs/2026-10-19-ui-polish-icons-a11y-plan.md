# UI polish, sub-project 3: Icons and accessible names — Implementation Plan

*Implements: docs/superpowers/specs/2026-10-19-ui-polish-icons-a11y-design.md*

## Step 1: Glyph buttons, by script
**Files:** the `.axaml` files with glyph buttons (BooksScreen, LibraryToolbar, ReaderSection, CollectionPropertiesOverlay, DetailTabs,
Continuity*, EventHero, LibraryPreviewPanel, PluginScreen, …).

**What:**
- A `Button` whose `Content` is a single ✕ ▲ ▼ ✎ ⋯ becomes `iconButton sm`, holding a `SymbolIcon` and an action name (unless it
  already has a name).
- A `Button` whose `Content` starts with a glyph followed by text becomes an icon-plus-label `StackPanel`.

**Verify:** XML parse of every file, then the build.

## Step 2: Standalone text glyphs, by hand
**Files:** LibraryToolbar (▾ ×4), LibraryPreviewPanel (★), LibraryScreen (●), PosterRail (✓), BookReaderScreen (🔖 ✓), EventHero (↻),
ReadingListPageView (⠿ handle), MigrationOverlay and LibrarySection (↔).

**Verify:** build; render the Library toolbar.

## Step 3: Names
- Copy the tooltip into `AutomationProperties.Name` wherever an icon-only button has a tooltip (by script).
- Write names by hand for the rest, listed in the change report.

**Verify:** the lint rule.

## Step 4: Rows without a logo
**Files:** MainWindow (Continuity row), ConnectionsSection (tile `BrandMark` size).

**Verify:** build.

## Step 5: Lint and tests
**Files:** `XamlConsistencyLintTests`, with `IconButtonsHaveNames` and `NoTextGlyphIcons`.

**Verify:** a targeted run (lint, SharedControls, LibraryLens, Reading Lists, the colour lint) and the toolbar render.
