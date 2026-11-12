# Continuity map M3 — implementation plan

*Implements [the approved Matrix, roll-call and progress design](2026-11-11-continuity-matrix-rollcall-design.md) on the M1/M2 working tree.*

## Step 1: Matrix and progress model
**Files:** `src/Paperbunkr.App/Services/EventMap/EventMatrixBuilder.cs` (new), `EventMapLayout.cs`, `EventMapModels.cs`, `src/Paperbunkr.App.Tests/EventMap/EventMatrixBuilderTests.cs` (new).
**What:** Build series rows independent of folded tracks; derive spine segments, relay chunks of eight, continuity blocks, totals and read counts. Keep references to original layout cell indices; support hidden series and optional future ghost counts. Derive lane and band progress from live cards.
**Verify:** Pure tests for all column modes, folded lanes, read counts, hidden series, totals and empty segments.

## Step 2: Drawn Matrix and accessibility
**Files:** `src/Paperbunkr.App/Controls/EventMap/EventMatrixCanvas.cs` and automation peer (new), `EventMapCoverSource.cs`, `src/Paperbunkr.App.Tests/EventMap/EventMatrixCanvasTests.cs` (new).
**What:** Pinned headers and series rail, two-axis scrolling, compact and cover rows, read rings, expansion strip, keyboard and pointer navigation, virtual visible-cell peers. Reuse token palette, text and cover cache; route focused-item keys locally, not app shortcuts. Follow `avalonia-custom-controls`, `avalonia-input-interaction`, `avalonia-testing`, and `avalonia-pro-max/accessibility`.
**Verify:** Headless keyboard, pointer, expansion, selection, scrolling and automation tests; theme renders.

## Step 3: Mode, roll-call, and shared selection
**Files:** `src/Paperbunkr.App/ViewModels/EventMapViewModel.Matrix.cs` (new), `EventMapViewModel.cs`, `EventMapViewModel.Continuity.cs`, `ContinuityScreenViewModel.cs`, `src/Paperbunkr.App/Views/EventMapView.axaml` and code-behind, `Controls/EventMap/EventMapRollCallPanel.axaml` and code-behind (new).
**What:** Add Map/Matrix sub-switch without changing top `TabStrip.Step`; preserve selection, scope mode, hidden-series lifetime and inspector. Add virtualized searchable roll-call, show-all and reveal/highlight requests. Hide series before both layouts; keep hidden series in roll-call. Use shared control styles, `Reveal`, `avalonia-xaml`, `avalonia-mvvm`, and motion guidance.
**Verify:** View-model and real-window tests for mode switching, top tabs, hide/show, reset, search, selection transfer and Esc precedence.

## Step 4: Live progress and integration
**Files:** `Controls/EventMap/EventMapCanvas.Render.cs`, `.Input.cs`, `EventMapViewModel*.cs`, map/Continuity tests; `wiki/Story-Events-and-Relations.md`, M3 design and pitch, `docs/paperbunkr-todo.md`.
**What:** Render read/in-progress lane bars, corner totals and band fills; refresh all progress on read-state changes without reload. Run `avalonia-pro-max/review-checklist` and document manual limitations and M1's pending sign-off.
**Verify:** Progress update tests, theme/scale renders, targeted map and Continuity suites plus relevant lint and a build. Do not run concurrent builds/tests or modify the user's database.
