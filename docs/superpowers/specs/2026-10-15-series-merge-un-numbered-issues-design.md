# Series merge — preserve unnumbered issues

Date: 2026-10-15. Status: design, pending user review. Not approved for `writing-plans`.

## Problem

The Library's “Merge series…” action can remove an entire source series without adding its
unnumbered comics to the target. `SeriesMergeHelper.MergeInto` currently identifies duplicates
using `(EffectiveNumber, EffectiveVolume)`. When both values are absent, every issue has the same
`(null, null)` key. If the target already has an unnumbered issue, an unnumbered source issue is
removed as a duplicate; otherwise the first unnumbered source issue moves, but subsequent
unnumbered source issues collide with it and are removed. The files remain on disk; because their
issue rows were deleted, a later Scan Now can import them again under the series name embedded in
the files. This explains both the apparent loss during merge and the source series returning after
a scan.

The merge dialog independently calculates its warning from the same nullable tuple, so it can
warn that unnumbered issues are duplicates even though their identities are unknown.

## Evidence and CE parity

- `SeriesMergeHelper.MergeInto` builds and checks the nullable tuple key. It is shared by Library
  bulk merge, Find Similar Series, and Needs Review's series-conflict merge.
- `MergeSeriesScreenViewModel` builds the same tuple set for its duplicate count and warning.
- The Library scanner builds `existingPaths` from issue rows and filters those paths before import.
  Once a merge deletes an issue row, its still-existing file is eligible for a later scan.
- The checked-out ComicRack CE source (`_reference/ComicRackCE`) has no series-merge command, so it
  provides no merge duplicate policy to copy. This change preserves Paperbunkr's explicit existing
  numbered-issue rule and fixes only its treatment of missing identity.
- The user chose to let “Resync Series from File” move issues according to embedded ComicInfo.
  Merge therefore adds no permanent assignment lock or resync exception; a later explicit resync
  keeps its current behavior.

## Goals

- Never delete an issue as a duplicate when its effective issue number is blank or missing.
- Keep moving such issues into the target series, with their files and issue metadata intact.
- Make the merge dialog's duplicate count and warning agree exactly with the merge operation,
  including merges with multiple source series.
- Keep the existing duplicate rule for issues with a usable number: matching effective number and
  effective volume within the target-and-already-merged issues is considered a duplicate.
- Apply the same correction in every flow that uses `SeriesMergeHelper`.

## Design

### Duplicate identity

Define one shared merge-key rule. An issue has a usable merge key only when
`EffectiveNumber()` is not null, empty, or whitespace. Its key remains the pair
`(EffectiveNumber(), EffectiveVolume())`. A missing volume remains a valid part of a numbered
issue's key, matching current behavior. A blank-number issue has no merge key and cannot be
classified as a duplicate by this rule.

When merging, numbered issues whose key is already present are removed as before. Numbered issues
whose key is new move to the target and add that key to the set. Issues without a usable number
always move to the target and do not add an identity key. This also preserves multiple unnumbered
issues from the same merge or from different source series rather than collapsing them together.

The duplicate-count calculation uses the same shared key extraction and insertion logic, iterating
sources in the same order the merge applies them. This avoids drift between the preview and the
actual operation.

### Persistence and resync

No schema change is needed. Moved issues retain their existing rows and files. No file metadata is
rewritten. Explicit “Resync Series from File” remains allowed to reassign a moved issue from its
current target to the series named in its embedded ComicInfo, per the user's decision.

### Errors and edge cases

- Null, empty, and whitespace-only effective numbers all mean “identity unavailable,” not a shared
  duplicate identity.
- A numbered issue with a null volume still uses `(number, null)` as its key, preserving the
  current behavior for that case.
- Duplicate removals still invalidate the removed issue's cover cache entry.
- Existing foreign-key and save failures continue through their current error handling. This fix
  does not change merge transaction or undo behavior.
- Numbered duplicates with different volumes remain separate, as they do now.

## Testing

- `SeriesMergeHelper`/Library bulk-action tests: when both target and source contain issues with no
  number and no volume, both source rows move and no issue row is deleted.
- Blank-number variants: null, empty, and whitespace issue numbers do not collide, including when
  a volume is present.
- Numbered duplicate regression: equal effective number and volume still removes only the source
  duplicate; distinct volumes still move as separate issues.
- Multiple sources: duplicate keys are counted and removed against the target and earlier source
  issues, while unnumbered issues from every source are preserved.
- Merge-dialog tests: `DuplicateCount` and `DuplicateWarning` exclude issues without usable numbers
  and match the helper's removal count.
- Scanner integration regression: after merging unnumbered issues, a subsequent Scan Now does not
  re-import their still-present files because their issue rows remain attached to the target.
- Existing Needs Review and Find Similar Series merge coverage remains green under the shared rule.

## Out of scope

- Guessing duplicate identity from issue titles, file names, file contents, or page counts.
- Changing the duplicate rule for numbered issues, transfer of series-level metadata, or merge
  undoability.
- Preventing an explicit “Resync Series from File” from moving issues back to their embedded
  series.
- Automatically merging newly scanned series by name.
