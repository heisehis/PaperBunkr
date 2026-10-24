# Bulk actions for story-event suggestions

Date: 2026-10-24
Status: Implemented after written-spec approval in chat; implementation uncommitted.

## Scope and reference

Add bulk selection and actions to **New story events** on Suggestions & checks.
The user approved both recommendations: checkboxes, selected count, Select all,
Clear, Accept selected and Dismiss selected; stay on this page, show progress and
one completion summary, and retain failed items selected for retry. Existing
individual Accept and Dismiss actions remain available.

The same page already implements these selection controls for Shared universes.
ComicRack CE has generic Select All/Select None patterns in
`ComicRack/Controls/PagesView.cs` and `ComicRack/Dialogs/DeviceEditControl.cs`,
but no corresponding story-event suggestion queue was found. This addition
extends Paperbunkr's existing queue rather than claiming direct CE parity.

The chosen approach is a toolbar within the New story events section, mirroring
Shared universes. A floating selection bar would introduce a second interaction
pattern on this page; unconditional Accept all/Dismiss all would remove the
ability to choose a subset.

## Selection and layout

- Add an accessible checkbox to each story-event suggestion row.
- Show a selected count and Select all, Clear, Accept selected and Dismiss selected
  above this section's rows. Select all selects every pending story-event
  suggestion in the section, including those below the viewport.
- Accept selected and Dismiss selected are disabled when nothing is selected.
- Use shared button families, text classes, theme tokens and spacing from
  `.agents/skills/paperbunkr-design-language/SKILL.md`. Wrap toolbar controls when
  width is constrained so they fit the user's 1366×768 window and narrower layouts.
- Preserve keyboard access through standard controls. No new shortcuts are needed.
- Selection is temporary view-model state; it does not add database fields.

## Batch execution

Snapshot the selected rows when an action begins. Run one batch at a time and
disable conflicting story-event selection, row actions and refresh actions while
it is active. Keep the UI responsive and display completed/total progress plus
the current event name. Process candidates sequentially to respect provider
request pacing and avoid concurrent creation of the same event.

Accept uses the same external verification, event resolution, membership and
role detection as individual acceptance. Extract a shared operation instead of
calling the individual command, which navigates and notifies for each row.
Retain existing identity-completion and duplicate-detection behavior after
successful acceptance, with UI refreshes grouped where practical.

Dismiss uses the existing remembered dismissal key (arc name and publisher).
Bulk actions use the existing acceptance/dismissal semantics without additional
confirmation dialogs.

Each row is an independent result. A failure does not stop later selected rows.
Successfully processed rows are removed after their operation succeeds; defer
collection changes until the triggering routed event has completed. Failed rows
remain present and selected, with their failure shown for retry. Preserve any
selection on unprocessed rows. Refresh counts and the sidebar badge after changes.

## Completion and navigation

Stay on Suggestions & checks after a batch, even when it contains one row.
Publish one completion summary with succeeded/failed counts, rather than a toast
or navigation for every row. Individual acceptance retains its current behavior
of opening the accepted event. Always clear busy state after completion or an
unexpected failure so the user can retry.

## Files and validation

Expected implementation surface:

- `src/Paperbunkr.App/ViewModels/StoryEventCandidateRowViewModel.cs`: selection,
  selection notification, action availability and per-row batch error state.
- `src/Paperbunkr.App/ViewModels/SuggestionsChecksViewModel.cs`: aggregate selection,
  shared acceptance operation, batch execution, progress and summary.
- `src/Paperbunkr.App/Views/SuggestionsChecksView.axaml`: toolbar, checkbox and status.
- Focused tests under `src/Paperbunkr.App.Tests/ContinuityScreen/` and roadmap note.

Validate selected-only acceptance and dismissal, remembered dismissals, progress,
failure retention and continuation, no batch navigation, busy-state recovery,
deferred row removal and existing individual acceptance. Use isolated databases
and fake provider work, with no live requests. Run focused tests and the existing
XAML/shared-control lint checks. Review wrapping, checkbox labels, status text and
theme tokens using the Avalonia MVVM, layout and review-checklist subskills.
Record any manual rendering or keyboard checks that remain outstanding.

## Verification

The App and its compiled XAML built successfully. The focused selection of
StoryEventSuggestionBulkTests, SuggestionsChecksViewModelTests,
XamlConsistencyLintTests, ThemeHardcodedColorLintTests and SharedControlsStyleTests
passed all 45 tests. This includes selected-only acceptance/dismissal, remembered
dismissals, failure continuation and retry, progress and busy guards, sidebar
refresh, existing individual acceptance/navigation, and view construction/layout
in default, Daylight and Matrix themes at content widths of 640 and 1000 pixels.
Headless render samples were generated for visual inspection. Real-window
keyboard navigation and screen-reader checks remain manual follow-up work.
