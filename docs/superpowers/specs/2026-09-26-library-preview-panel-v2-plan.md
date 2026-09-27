# Library preview panel v2 — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-26-library-preview-panel-v2-design.md*

Surveyed 2026-09-26. The working tree is shared with other sessions and already has uncommitted edits in
`LibraryScreenViewModel.cs`, `LibraryScreen.axaml`, `AppSettings.cs`, `PaperbunkrDbContext.cs` and the model
snapshot — every edit below is additive (targeted `Edit`, never a rewrite of those files). Nothing is
committed by this plan. The app is not launched: the user checks the visuals on screen.

Survey findings that shaped the steps:

- `ContextMenuHost.ShowMenu(anchor, entries)` already exists "for a visible … button", and
  `LibraryContextMenuBuilder` already builds the per-issue / per-series entries including the
  "Add to Collection ▸" children. The panel's `⋯` and "Add to Collection" buttons reuse that, not new menu code.
- `MarkIssueReadCommand` / `MarkIssueUnreadCommand` route through `Selection.UnionForAction(id)`, so on a panel
  button they would also act on an unrelated multi-selection. New panel-specific commands must call the private
  `MarkIssuesRead` / `MarkIssuesUnread` with exactly the previewed id(s).
- No single-series "mark all read" exists; the series version is `MarkIssuesRead(ids of IssueList.Rows where SeriesId == …)`.
- The DB default for `LibraryPreviewPanelWidth` is `HasDefaultValue(320.0)`; changing a default on an existing column
  makes SQLite rebuild `AppSettings` and the change is cosmetic. **Deviation from the spec: the default stays 320**;
  only the column `MinWidth` moves 260 → 280. (Spec §8 said 340; existing users kept their value either way.)
- The new setting is a plain string column (`AddColumn`, no table rebuild).

## Step 1: Persisted section state (data)
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit),
`src/Paperbunkr.Data/Migrations/*_AddLibraryPreviewCollapsedSections*.cs` + Designer + snapshot (generated)
**What:** `string LibraryPreviewCollapsedSections = "story,file,details"` (entity initializer must equal the
`HasDefaultValue` — the EF default gotcha). First run `dotnet ef migrations has-pending-model-changes`; if other
sessions left the model dirty, stop and report instead of bundling their changes. Generate the migration, then check
the Designer/snapshot nullability (stale-snapshot gotcha) and that the Up is a single `AddColumn`.
**Depends on:** none
**Verify:** `dotnet build`; `Paperbunkr.Data.Tests` migration tests (up/down/up antipattern respected: no
`Down` re-application in a test).

## Step 2: PosterRail supports cover keys and read markers
**Files:** `src/Paperbunkr.App/Models/PosterRailItem.cs` (edit: `CoverKey`, `IsRead`),
`src/Paperbunkr.App/Views/PosterRail.axaml` (edit)
**What:** When `CoverKey` is set the cover renders through `AsyncCoverImage.SourceId`, else the existing `CoverImage`
bitmap (Detail rails unchanged). `Border.railCover` gets `Width="76"`. `IsRead` adds a ✓ overlay + reduced opacity.
**Depends on:** none
**Verify:** build; on screen (user): preview rail covers, and Detail Related/Continuity/Collection rails unchanged.

## Step 3: PanelSection control
**Files:** `src/Paperbunkr.App/Controls/PanelSection.axaml` + `.axaml.cs` (new, code-behind in the same step per the
new-View build gotcha)
**What:** Header (uppercase overline + hairline + chevron), `Key`, `IsOpen` (two-way), `Collapsible`, `Hint`
(shown when collapsed), content. Tokens only (`PbTextFaintBrush`, `PbBorderBrush`).
**Depends on:** none
**Verify:** build with the forced-CoreCompile recipe from CLAUDE.md; launch not required.

## Step 4: View-model surface
**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit), `LibraryContextMenuBuilder.cs` (edit)
**What:**
- Rail items: set `CoverKey`, `IsRead` in `OnPreviewSeriesChanged`.
- Drill-in: `PreviewDrillIssue`, `ClearPreviewDrillCommand`, `DrillIntoIssueCommand(IssueListRow)`; new
  `ShowIssuePreview` / `ShowSeriesPreview` per spec §5; `ActivePreviewIssue`; `PreviewBackLabel`.
  Raise `ShowIdlePreview` from the drill setter.
- Actions: `MarkPreviewIssueReadCommand` / `…UnreadCommand`, `MarkPreviewSeriesReadCommand` / `…UnreadCommand`
  (exact ids, no selection union); series primary-button label (`PreviewSeriesPrimaryLabel`,
  `PreviewSeriesPrimaryIssueId`: continue issue, else first issue).
- Multi-select strip: `ShowPreviewSelectionStrip`, `PreviewSelectionStripText` (issue or series selection count > 1;
  re-raised from the selection-changed hooks).
- Section state: `IsPreviewSectionOpen(key)`, `SetPreviewSectionOpen(key, open)` backed by
  `LibraryPreviewCollapsedSections`, loaded in the ctor block near line 473, saved in `SaveLibrarySettings`.
- `LibraryContextMenuBuilder.BuildPreviewOverflow(object target)` (issue: Add to Collection ▸, Open Detail, Reveal;
  series: Open series page, Add to Collection ▸) and `BuildPreviewCollectionMenu(object target)`.
**Depends on:** Steps 1, 2
**Verify:** new unit tests in `LibraryScreenViewModelTests.cs` (Step 7).

## Step 5: Rewrite the panel view
**Files:** `src/Paperbunkr.App/Views/LibraryPreviewPanel.axaml` (edit — full body), `LibraryPreviewPanel.axaml.cs` (edit)
**What:** Root grid: strip row / scroll row / pinned-bar row per state. Series = layout C (backdrop hero, natural-ratio
cover, chips, progress, About, Issues rail, Details); issue = layout B (back link, header, chips, progress, Summary,
Credits open; Story, File collapsed). Backdrop = the cover `Image` with `Effect="blur(28)"` behind a gradient scrim;
hidden when the cover key is empty. ~120 ms cross-fade via a `DoubleTransition` on the hero opacity, reset without
animation when a second focus change lands inside the window. Code-behind handles rail click (single → drill-in,
double-tap → `IssueList.OpenIssueCommand`), the back link, the `⋯`/Add-to-Collection buttons via
`ContextMenuHost.ShowMenu`, and `PanelSection.IsOpen` ⇄ VM. Keep the existing `IsTabStop=False` rail style. Any
close-from-inside-a-row path posts via `Dispatcher.UIThread.Post` (CLAUDE.md gotcha).
**Depends on:** Steps 2, 3, 4
**Verify:** build; on-screen check (user).

## Step 6: Screen wiring
**Files:** `src/Paperbunkr.App/Views/LibraryScreen.axaml` (edit: column `MinWidth` 260 → 280),
`src/Paperbunkr.App/Views/LibraryScreen.axaml.cs` (edit: `previewColumnMinWidth` 280; `OnCardGotFocus` clears the drill-in)
**Depends on:** Step 4
**Verify:** build; unit test that a focus-style assignment clears the drill (Step 7).

## Step 7: Tests
**Files:** `src/Paperbunkr.App.Tests/LibraryScreenViewModelTests.cs` (edit: new region), `src/Paperbunkr.App.Tests/`
PosterRail item test if a natural home exists
**What (view-model level only — the headless app loads no theme, so item controls can't render there):**
`ShowSeriesPreview`/`ShowIssuePreview` truth table with and without drill-in; drill cleared by setting
`PreviewSeries`/`PreviewIssue`; back clears; rail items carry `CoverKey`/`IsRead`; `MarkPreviewIssueRead` marks only
that id even with an unrelated multi-selection; `MarkPreviewSeriesRead` marks every issue of the series and no
others; selection-strip text/visibility; collapsed-sections round-trip through `AppSettings`.
**Depends on:** Steps 4, 6
**Verify:** `dotnet test src/Paperbunkr.App.Tests --filter LibraryScreenViewModelTests`, then the migration tests.

## Step 8: Review and docs
**What:** Read `avalonia-pro-max/review-checklist/SKILL.md` and apply it to the new XAML (no hex literals, tokens,
focus/tab order, accessibility names on icon-only buttons, `pbText*`). Update `docs/paperbunkr-todo.md` with what was
verified (build + tests) and what was not (on-screen). Save a project memory note. Do not commit.
**Depends on:** all
