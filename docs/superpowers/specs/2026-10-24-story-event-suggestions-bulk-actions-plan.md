# Bulk story-event suggestions — implementation plan

Implements: `2026-10-24-story-event-suggestions-bulk-actions-design.md` (approved in chat).

1. Add row selection, action availability and error state in
   `StoryEventCandidateRowViewModel.cs`; extract reusable event acceptance in
   `SuggestionsChecksViewModel.cs`. Follow the Avalonia MVVM subskill.
2. Add aggregate selection and sequential batch execution in
   `SuggestionsChecksViewModel.Bulk.cs`. Snapshot rows, retain failures, defer
   collection changes, share verification and membership semantics, and keep
   navigation exclusive to individual acceptance. Guard refreshes and conflicting
   actions. Verify with isolated database tests in the existing Continuity fixture.
3. Add wrapping toolbar, accessible checkboxes, progress and summary in
   `SuggestionsChecksView.axaml`, following the Avalonia layout/data-binding
   subskills and Paperbunkr design language. Run focused behavior tests and shared
   XAML/theme lint tests, review the Avalonia checklist, and record verification
   and any manual checks outstanding in the design and roadmap.

Completed: steps 1–3. Also wired the batch's sidebar event refresh through
`ContinuityScreenViewModel.cs`, so new events appear without navigating away.
The 45-test focused behavior and UI lint selection passed; real-window keyboard
and assistive-technology checks remain outstanding.
