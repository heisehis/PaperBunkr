# Detail Screen — Issue-Focused Display

*Date: 2026-08-07. Follow-on to docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md,
deliberately deferred out of that spec since it's a display concern, not an editing one. Closes
two loose ends flagged during that work: the "Edit" button at `DetailScreen.axaml:113` has no
`Command` bound at all, and `DetailPillsViewModel.Genres` reads `Series.Genre` (a separate field)
instead of any aggregation of per-issue `Issue.Genre` - a real, confirmed bug independent of this
feature, fixed here since this spec already has to sort out per-issue vs. series-level field
display. Motivated by the user's own experience with another comic-library app (Omnibus): selecting
an issue changes the Detail screen to show that issue's own cover and metadata, not the series
aggregate.

## 1. Selection → display mode

`DetailTabsViewModel` gains an `Action? onSelectionChanged` constructor parameter (same
callback-based convention as `goToProperties`/`goToBulkProperties`, not a new mechanism), invoked
at the end of `ToggleIssueSelection` after `SelectedIssueIds` is updated. `DetailScreenViewModel`
subscribes and recomputes display mode from `Tabs.SelectedIssueIds.Count`:

- **0 or 2+ selected → "series" mode** - today's existing aggregate behavior, unchanged.
- **exactly 1 selected → "issue" mode** - that one issue's own data.

Series title, summary, status pill, and issue-count pill stay series-level in both modes (per
explicit decision) - you're still browsing that series, just focusing one issue's cover/credits.

## 2. Cover

In issue mode, `DetailScreenViewModel.CoverImage` switches to that issue's own art via the existing
`CoverImageCache.Get(issueId)` (same cache every other cover-art consumer already uses); reverts to
the series-level cover in series mode. `CoverBrush` (the gradient shown behind/before the real
bitmap loads) stays derived from the series name in both modes - there's no per-issue color
concept, and there shouldn't be one just for this.

## 3. Meta/Pills, sourced through `BulkFieldDescriptor.Get`

Both `DetailMetaViewModel` and `DetailPillsViewModel` gain a `LoadIssue(Issue issue)` alongside
their existing `LoadSeries(Series series)`. Both aggregate-mode and single-issue-mode read fields
through `BulkFieldRegistry.Find(label).Get(...)` (a new small public lookup added to the registry)
instead of hardcoded `i => i.Writer`-style lambdas repeated in each ViewModel - one field-access
path now shared with the bulk editor, per the user's own suggestion that the field registry double
as this display's "gathering tool."

- **Series mode**: unchanged aggregation shape - `CsvFieldAggregator.Join`/`.Distinct` over
  `series.Issues.Select(BulkFieldRegistry.Find("Writer").Get)` etc., just re-sourced through the
  registry instead of inline lambdas.
- **Issue mode**: no aggregation needed - `CsvFieldAggregator.Distinct(new[] {
  BulkFieldRegistry.Find("Genre").Get(issue) })` (still routed through the same helper, so a
  single-valued field's own internal comma-list, e.g. a multi-genre issue, still tokenizes
  correctly) or a direct `descriptor.Get(issue)` read for the plain credit fields.

**Two small additions while this code is already being touched**:
- **Cover Artist becomes a 5th credit row** in `DetailMetaViewModel`/`DetailMeta.axaml` (Writer/
  Artist/Cover Artist/Colorist/Letterer) - matches the Omnibus reference screenshot; today's 4-field
  set was simply missing it. Extends the existing 2-column `Grid` with one more row, no layout
  rework.
- **The Genre bug fix**: `DetailPillsViewModel.Genres` switches from reading `series.Genre`
  (a single, separate field the bulk editor never touches) to aggregating real per-issue
  `Issue.Genre` via `BulkFieldRegistry.Find("Genre").Get`, in both series and issue mode. This is
  the fix for the "bulk-editing Genre doesn't reflect on the Detail screen" bug found during
  manual verification of the bulk editor - `Series.Genre` stops being read here at all.

## 4. Edit button

`DetailScreenViewModel` gains `EditCommand`, dispatching off `Tabs.SelectedIssueIds` exactly like
the right-click menu already does (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md
§2) - 1 selected → the existing single-book editor, 2+ → the existing bulk editor. No union with a
"clicked tile" here (there's no tile being clicked, it's a toolbar button) - it dispatches purely
off the current selection.

- `CanEdit` (bool, `IsEnabled` binding) is `false` when nothing is selected - there's no
  series-level properties editor to fall back to, and building one is out of scope here.
- `EditButtonLabel` reads "Edit" (disabled state) / "Edit Issue" / "Edit {N} Issues" depending on
  selection count - small, free UX improvement given the button already has to know the count to
  decide which editor to open.

## Testing

- `DetailScreenViewModelTests`: selecting exactly 1 issue switches `CoverImage`/`Meta`/`Pills` to
  that issue's own values; selecting 0 or 2+ shows the series aggregate; `EditCommand` routes to
  the right editor for 1 vs. 2+ selected and is disabled at 0; the already-fixed Genre bug is
  directly asserted here (bulk-edit an issue's Genre via `BulkIssuePropertiesScreenViewModel`, then
  confirm the Pills row reflects it in series mode without needing `Series.Genre` touched at all).
- `DetailMetaViewModelTests`/`DetailPillsViewModelTests` (new files - neither ViewModel has any
  test coverage today): `LoadIssue` populates every field including the new Cover Artist row;
  `LoadSeries`'s aggregation behavior is unchanged for every field except `Genres`, which gets a
  dedicated regression test for the fix.
- `BulkFieldRegistryTests`: `Find` returns the correct descriptor for a real label, and a sane
  failure (not a silent null) for an unknown one.
- Manual verification: same no-GUI-automation approach as every prior spec - build + run real
  tests, then ask the user to click a single issue tile and confirm the cover/credits/pills switch
  to that issue, click a second tile (2 selected) and confirm it reverts to the series aggregate,
  and confirm the Edit button opens the right editor and is disabled with nothing selected.
