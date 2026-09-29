# Library Health: Dismissed rows stay findable

Date: 2026-09-28. Status: approved in session (grilling round 1, all recommendations taken).

## Problem

Library Health's Files tab lets you **Dismiss** a Missing Files row or an Empty Rows row. Dismiss sets a
review-queue flag (`Issue.MissingAcknowledged`, `Issue.EmptyRowAcknowledged`,
`Series.EmptyRowAcknowledged`) and the row is filtered out of every Library Health list and count, with
no UI anywhere in Library Health to see or undo it. Verify keeps detecting the file (real case: a
`Ben_10_(2026-)_#5.cbz` row counted missing on 62 passes) but the user sees "No missing files" and
concludes the scan is broken. The only surface still showing it is the system "Missing Files" smart
list, which ignores acknowledgement by design (docs/superpowers/specs/2026-08-06-migration-ux-polish-
design.md §2).

A second, latent defect: `MissingAcknowledged` is never cleared. Relinking a file, the watcher seeing it
come back, or Verify finding it all leave the flag set, so if that file goes missing again later it is
silently hidden from the start. (`EmptyRowAcknowledged` on an issue is already cleared by the Empty Rows
relink; nothing else clears either empty-row flag.)

CE has no dismiss concept - these flags are Paperbunkr additions, so there's no CE behavior to match.

## Decisions

| # | Question | Decision |
|---|----------|----------|
| 1 | Scope | Missing Files **and** Empty Rows (issues + series). Duplicate groups are out: their dismiss records a resolution ("not duplicates"), not a hidden reminder. |
| 2 | Placement | A collapsed **"Dismissed · N"** sub-group nested at the bottom of each section's list. |
| 3 | Actions on a dismissed row | **Restore**, **Relink**, **Remove** (empty series: Restore, Remove - it has no file). |
| 4 | Section with only dismissed rows | Header reads e.g. "No missing files · 1 dismissed" and is expandable. |
| 5 | Dismissal lifetime | Cleared automatically once the condition goes away (file back on disk / readable / series has issues). A dismissal covers one missing episode. |
| 6 | Bulk | **Restore All** in the dismissed sub-group header. |
| 7 | Summary tiles / tab badge | Unchanged. "Currently missing" already counts dismissed rows; "Confirmed missing" and the tab badge deliberately don't. |

## Design

### Data
No schema change, no migration. Reuses the three existing flags.

### ViewModel (`PreferencesScreenViewModel`)
- Three new collections filled in `RefreshLibraryHealth` by the existing queries with the acknowledgement
  filter inverted, same ordering (series name):
  - `DismissedMissingFileItems` - `FileIsMissing && MissingAcknowledged`
  - `DismissedEmptyIssueItems` - `IsContentEmpty && EmptyRowAcknowledged`
  - `DismissedEmptySeriesItems` - `EmptyRowAcknowledged && !Issues.Any()`
- `DismissedEmptyRowCount`, `Has...` flags, and header helpers: the section toggle is hit-testable when
  it has active **or** dismissed rows; the header shows `· N dismissed` when N > 0.
- Two open/closed states for the sub-groups (`MissingDismissedOpen`, `EmptyRowsDismissedOpen`, as
  `LibraryHealthSectionState`s not added to `LibraryHealthSections.All`, so they are not search/deep-link
  targets). Closed by default.
- Commands: `RestoreMissingFile(id)`, `RestoreEmptyIssue(id)`, `RestoreEmptySeries(id)`,
  `RestoreAllDismissedMissingFiles`, `RestoreAllDismissedEmptyRows`. Each clears the flag, then refreshes
  via `Dispatcher.UIThread.Post` - the click is still routing through the row being removed (CLAUDE.md
  "don't remove/detach a control from inside a routed event it's still raising").
- Dismissed rows reuse the existing Relink/Remove handlers.

### Row view models
- `MissingFileRowViewModel` and `EmptySeriesRowViewModel` gain an optional `onRestore`; when supplied the
  row is in dismissed mode: `IsDismissed = true`, a `RestoreCommand`, and the template shows Restore in
  place of Dismiss plus a neutral "Dismissed" chip in place of the severity chip. The row is dimmed.

### View (`Views/Preferences/LibrarySection.axaml`)
- Inside each section's open body, after the active list(s): a sub-group header (chevron, "Dismissed",
  count, Restore All) and its list, visible when the sub-group has rows.
- Section header: `· N dismissed` text; the "No missing files" / "Nothing empty" empty-state text stays,
  with the dismissed count appended.
- Only existing theme resources (`Pb*` brushes, `PbRadiusChip`), no hardcoded colors.

### Auto-clear (decision 5)
The flag is cleared wherever the app already establishes that the condition has gone:
- `LibraryHealthService.Verify`: file exists → `MissingAcknowledged = false`; probe opens →
  `EmptyRowAcknowledged = false`. Full (unscoped) passes also clear `Series.EmptyRowAcknowledged` for
  series that have issues.
- `LiveFolderWatchService` and `LibraryPathRepairService`, where they set `FileIsMissing = false`.
- Relink paths in `PreferencesScreenViewModel` (single relink, Relink All from Folder).

## Testing
- VM: dismissed rows land in the dismissed collection and not the active one (both sections, all three
  kinds); Restore moves a row back; Restore All clears the group; header count values.
- `LibraryHealthService`: each flag is cleared when its condition clears, and preserved while it holds.
- Relink clears `MissingAcknowledged`.
- `avalonia-pro-max/review-checklist` pass before calling the UI done.

## Out of scope
Duplicate-group dismissals, reported-page dismissals, Review-tab sections.
