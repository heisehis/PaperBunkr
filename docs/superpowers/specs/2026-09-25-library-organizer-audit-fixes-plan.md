# Library organizer audit fixes — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-25-library-organizer-audit-fixes-design.md*

Order matters: Phase 1 first (files can land in the wrong place today). Each step: failing test first, then the fix.
Test project: `src/Paperbunkr.Data.Tests/Organizing` + `/Naming`, `src/Paperbunkr.App.Tests/OrganizerUiTests.cs`.

## Step 1: Path building (defect a, i, j)
**Files:** `Naming/TemplateEvaluator.cs` (strip `/` `\` from resolved *values* in the Normal group), `Naming/Sanitizer.cs`
(`SanitizePath` drops empty segments; dot-strip on folder segments only), `Organizing/LibraryOrganizerService.cs` (guard blank BaseFolder; empty file part → per-item problem).
**Verify:** `SanitizerTests` (empty/rooted segments, "Fate/Zero"), `LibraryOrganizerServiceTests` (missing publisher stays under BaseFolder, empty file template).

## Step 2: Plan model + per-item problems (f, b, c)
**Files:** `Organizing/OrganizerModels.cs` (`PlannedMove` gains `IsAlreadyInPlace`, `Problem`; `OrganizeResult.AlreadyInPlace`),
`LibraryOrganizerService.cs` (plan catches template errors per item; same-path → already in place; case-only → rename; in-batch destinations claimed;
execution-time collision check incl. simulated paths; renamed path avoids claimed).
**Depends on:** Step 1. **Verify:** service tests for each.

## Step 3: Recycle Bin, atomicity, write-back gating, undo (d, e, g, h)
**Files:** `LibraryOrganizerService.cs` (ctor takes `Action<string>? sendToRecycleBin`; Replace recycles and repoints/removes the old row; move-back on DB failure;
`BeginBatch` only for Move; undo = latest batch only, skips no-ops, removes folders it created), `Organizing/OrganizerEntities.cs` (`GetLastBatch` semantics),
`App/Scraper/OrganizeCoordinator.cs` (pass `RecycleBinHelper.SendToRecycleBin`; write-back only for Move; catch per-run exceptions; report already-in-place/failed reasons;
Activity job Succeed/Fail with reason; undo logged + write-back re-queued).
**Verify:** service tests + `OrganizerUiTests`.

## Step 4: Template parity (Phase 2)
**Files:** `Naming/FieldResolvers.cs` (manga polarity, case-insensitive `first()` names, decimal/negative/auto-width pad, date token, series-union mode),
`Naming/TemplateEvaluator.cs`, `Organizing/OrganizerEntities.cs` (`UseFolder`, `UseFileName`, `EmptyFolder`, `EmptyDataJson`, `SkipOnEmptyFields`, `ExcludeFoldersJson`),
migration `AddOrganizerParityOptions`, `ProfileManagerViewModel.cs` (+view) fields and save-time validation, windows name/length checks (reuse `ImportNaming` helpers).
**Verify:** `TemplateEngineTests` (changed `manga` test at :179-181), `OrganizerStoreTests`, migration test.

## Step 5: Exclude rules + preview/report (Phase 3)
**Files:** `ProfileManagerViewModel` (nested rule group + excluded folders), `OrganizeCoordinator` (`PlanAsync`→preview dialog→execute), new
`OrganizePreviewDialogView/ViewModel(.axaml/.cs together)`, run report on the Activity job + save-as-text.
Load the `avalonia` skill + review checklist before the UI parts.

## Step 6: Docs and comments
Fix the wrong plugin citations listed in the spec; update `docs/paperbunkr-todo.md`, `wiki/Scraping-and-Organizing.md`.
