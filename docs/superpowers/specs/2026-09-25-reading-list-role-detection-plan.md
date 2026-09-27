# Role detection — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md*

## Step 1: Detector (pure)
**Files:** `Paperbunkr.Data/ReadingLists/MemberRoleDetector.cs` (new: `RoleConfidence`, `RoleSuggestion`, `MemberRoleFacts`, `MemberRoleDetector`),
tests `Paperbunkr.Data.Tests/ReadingLists/MemberRoleDetectorTests.cs`.
**Verify:** every table row, precedence, whole-word, null cases.

## Step 2: Schema
**Files:** `Entities/EventMembership.cs`, `Entities/ReadingListItem.cs` (+`RoleSource`, `RoleReason`, `SuggestedRole`, `SuggestedReason`, `RoleSuggestionDismissed`),
new enum `RoleSource`, `PaperbunkrDbContext.cs` config, migration `AddRoleDetectionColumns` (+ Designer/snapshot via `dotnet ef`), migration test.
**Depends on:** none (parallel with 1).

## Step 3: Applier
**Files:** `ReadingLists/MemberRoleApplier.cs` (applies High into empty/Auto slots, stores Low as suggestion, never touches User), tests.
**Depends on:** 1, 2.

## Step 4: Source annotation + builder wiring
**Files:** `ReadingLists/Sources/ArcModels.cs` (`ArcIssue.Annotation`), `ComicBookReadingOrdersSource.cs` (keep annotations/headings), `ArcReadingListBuilder.cs` (create + refresh call the applier), event-suggestion accept paths
(`EventsScreenViewModel.StoryEventSuggestions.cs`, `IssuePropertiesScreenViewModel.cs`).
**Depends on:** 3. **Verify:** parsing fixtures, builder tests.

## Step 5: UI + Detect roles action
**Files:** event member row VM/view, reading-list item row VM/view, Events + Reading screens (Detect roles command), Activity Center job.
Avalonia skill + review checklist first; `.axaml` and code-behind in the same step.

## Step 6: Docs
`docs/paperbunkr-todo.md`, onboarding metadata section, wiki pages.
