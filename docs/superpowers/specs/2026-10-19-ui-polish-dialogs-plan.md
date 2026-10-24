# UI polish, sub-project 4a: Dialogs — Implementation Plan

*Implements: docs/superpowers/specs/2026-10-19-ui-polish-dialogs-design.md*

## Step 1: `DialogCard` control

**Files:** `Controls/DialogCard.cs` (new), `Styles/Overlays.axaml` (ControlTheme), `Paperbunkr.App.Tests/Controls/DialogCardTests.cs`
(new).

**Verify:** `DialogCardTests`.

## Step 2: Migrate dialogs, one file at a time

**Files:** the Q2 list.

**What:** for each file:

1. Read its header, body and footer.
2. Replace the root card with a `DialogCard` (Title, Subtitle, Size, Footer, FooterStart).
3. Keep the body exactly as it is.
4. Remove the old header, footer and max height.
5. Check that its existing test class still passes.

**Order:**

1. Small dialogs: confirm, quick rate, workspace name, update available, merge.
2. Medium dialogs: book / bulk book / book series properties, collection properties, new reading list, new event-or-continuity, goal,
   paste, list options, detail list options, edit layouts.
3. Large and XLarge dialogs: reading-list properties, manga series editor, template gallery, legal viewer, continuity compare, migration,
   the scraper dialogs.
4. Inline dialogs: Edit Continuity, MangaDex connect.

## Step 3: Defect fixes

**What:** the design § Defect fixes.

## Step 4: Lint rule

**What:** `DialogsUseDialogCard`.

## Step 5: Build, tests, render sheet

**What:** build to a scratch OutDir if the app is running, then run the targeted test classes.
