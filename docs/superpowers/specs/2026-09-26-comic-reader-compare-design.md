# Comic reader — Two-edition compare (slice E2) design

Date: 2026-09-26. Status: **built 2026-09-26** (uncommitted; on-screen check by the user pending, see "Implementation notes" at the end). Design settled in one grilling round ("recommended" on all questions).
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, slice E, second half: **#11 two-edition compare mode** ("same page from two files side by side or as a flicker toggle, to pick the better scan and safely delete the duplicate; ties into Find Similar Series").
Built after E1 (`2026-09-26-comic-reader-inreader-reference-design.md`). The rest of the slice's working-tree caveats apply.

## Facts this design rests on (verified 2026-09-26)

- **No CE counterpart.** CE's duplicate handling is a passive filter (`ComicBookDuplicateMatcher`, ported as `SmartListQueryBuilder.DuplicateIssueIds`/`BuildDuplicateGroups`); there is no page comparison in CE. Compare is Paperbunkr-original, an enhancement the duplicate-files
  design (`2026-09-05-duplicate-files-review-design.md`) listed as a non-goal ("no visual/content diff between candidate files") and left for later.
- **Duplicate groups exist and have a cleanup action.** Groups come from `SmartListQueryBuilder.BuildDuplicateGroups`, are listed with cover thumbnail, size and date, and resolved by keeping one and deleting the rest through `LibraryDeletionHelper.RemoveIssue(context, issue, deleteFile)` (Recycle Bin, cross-reference cleanup). That review UI was moved out of the migration overlay into Library Health by another session
  (uncommitted, `NeedsReviewViewModel.cs`); the compare button is added where the groups are now shown.
- **Page alignment can use dHash.** Slice B's `PageHasher` (`Services/AdDetection/PageHasher.cs`: `Compute(SKBitmap)`, `TryCompute(byte[])`/`TryCompute(Bitmap)`, `Distance(a, b)` = popcount of the xor) gives a 64-bit hash per page; editions of one book differ in page count (extra covers, ads, credits pages), so a fixed page-number pairing would mismatch.
- **Rendering and pipelines are reusable.** `ReaderImagePipeline.TryOpen(path)` opens any file with its own caches; `PageCanvas` renders one page with zoom, pan and fit modes; `ZoomPanMath` and slice D's smooth zoom already exist. Two pipelines and two canvases need no new rendering code.
- **Navigation pattern.** Screens are view models registered in `MainViewModel` with a `CurrentScreen` key and a `DataTemplate` in `MainWindow.axaml`; reading screens set `IsInReader` so the rail and status bar hide; navigation history entries are `NavigationEntry(key, kind, id, title)`. The Library right-click menu is built by `LibraryContextMenuBuilder` and supports selection-aware entries.
- **Remote issues have no local file** (`FilePath` null, pages fetched from a host), so compare is local files only.

## Scope

In: a Compare screen with side-by-side and flicker modes, dHash page alignment with a manual offset, linked zoom and pan, a facts strip per edition, and "Keep A"/"Keep B" deletion; entry from the Library menu (exactly two selected issues) and from a duplicate group.
Out: pixel-diff overlays or heatmaps, comparing more than two files, comparing remote issues, editing anything, and any automatic deletion.

## Design

### Entry points

- **Library:** right-click with exactly two issues selected shows "Compare files…" (hidden otherwise; disabled with a tooltip if either is missing its file or is remote). Also on the Detail screen's issue list, same rule.
- **Duplicate group:** a "Compare" button on a group of two or more; with more than two, a small chooser picks the two.
- The screen is a normal drill navigation (`CurrentScreen = "compare"`, history entry "Compare: A vs B"), full-bleed like the reader (`IsInReader`-style hiding of rail and status bar), Escape or the back button returns.

### The Compare screen

`CompareScreenViewModel` holds two `EditionSide` objects (issue id, path, pipeline, page count, facts) and the shared state (current step, offset, mode, zoom, pan). Two `PageCanvas` instances, paged and single-page, fed by each side's pipeline; the canvases' zoom and pan are two-way bound to one shared value each, so zooming or panning
either side moves both, with slice D's smooth zoom.

*Modes.* **Side by side** (default): A on the left, B on the right, each labelled. **Flicker**: one canvas shows A or B at the same place; `Space` (or holding `F`) flips, and a header chip says which one is showing; pixel-identical framing so a difference pops. **Wipe** is not built. Toggle with a segmented control and `M`.

*Pages and alignment.* Navigation is by A's page number (`Left`/`Right`, `PageUp`/`PageDown`, slider, thumbnails not needed). For A's page `i`, B's page `j` = the best dHash match in B within a window of ±8 pages around `i + offset` (distance at most 12 of 64; when none qualifies, `j = i + offset` unmatched and the label says so). Hashes are computed lazily on a
background thread, a page at a time as it is shown plus a window ahead, cached in memory per side (never stored), reusing `PageHasher.TryCompute` on the pipeline's display bitmap. A "match confidence" chip shows Matched (distance) / Unmatched. **Offset** buttons (−1, +1, "Reset") shift B's default pairing for the whole session when the automatic match keeps failing (a book that is all
splash pages). The chosen offset is remembered for this pair of issues for the visit only.

*Facts strip.* Under each side: pixel size of the current page (and its long-side ratio to the other side's), file size and format (`Issue.Format`, container), page count, average bytes per page, and a "Higher resolution" / "Larger file" / "More pages" badge on the winner of each row, colour-blind safe (text plus icon). Facts are read from the file and the issue row, never guessed.

*Decide.* "Keep A" / "Keep B" (and their keys `1` and `2`) ask for confirmation naming both files ("Keep A (12 MB, 1600 px) and move B to the Recycle Bin?") through the app's confirm dialog, then call `LibraryDeletionHelper.RemoveIssue(context, other, deleteFile: true)`, mark the group resolved the way the review's existing resolve action does (a shared method extracted from `NeedsReviewViewModel`, not reimplemented), show a toast, and return to where the compare started. "Not a duplicate" (dismiss the group, sets `DuplicateAcknowledged`) is the third
button. Nothing is deleted without that confirmation, and closing the screen changes nothing.

### Memory and lifetime

Two pipelines are opened when the screen opens (one per side, with the default memory budget split in half) and disposed when it closes or the pair changes. The pipelines' windows follow the shown page only.

### Settings and keys

No new settings. Keys are local to the screen (`Left`/`Right`, `Space`, `F`, `M`, `1`, `2`, `Escape`), documented in the screen's help line; not added to the remappable registry in this pass (the screen is a modal-ish tool, like the Books reader's own keys).

### Notifications

Toasts only (kept, deleted, dismissed, "hashes are still computing"); no Activity Center job.

## Build order

1. `EditionSide` and the hash aligner (pure: window search, threshold, offset) with tests. 2. `CompareScreenViewModel` and screen: two canvases, linked zoom/pan, side-by-side, page navigation. 3. Flicker mode, facts strip, offset and confidence chips. 4. Entry points (Library menu, Detail, duplicate group), navigation and history. 5. Keep A / Keep B / dismiss with the confirm dialog and the extracted resolve method. 6. Docs, wiki, memory, review checklist.

## Testing

- Pure: the aligner picks the true partner when one edition has an extra cover and an extra ad page (synthetic pages built with `PageHasher`-stable patterns), returns unmatched when nothing is within the threshold, and honours the offset and window; the facts badges.
- View model: opening builds both sides from two real `.cbz` fixtures of different resolutions and page counts; the current pair moves together; flicker flips the shown side; the offset shifts the pairing; Keep A deletes B's row and file (temp files), Keep B the reverse, dismiss sets the flag, cancelling the confirm deletes nothing; the entry-point visibility rules (exactly two, local, files present).
- Headless: linked zoom and pan between the two canvases, the mode switch, key handling.
- Data/regression: the extracted resolve method behaves exactly as the review's existing action (its current tests keep passing).

## Not verified by design

How the two-canvas layout feels on screen, alignment quality on real editions (the confidence chip and offset buttons are the escape hatch), and the flicker's usefulness for spotting differences; those are the user's on-screen checks.

## Implementation notes (2026-09-26)

Built in the order given under "Build order". Where the code differs from the design above:

- **More than two copies:** instead of a chooser, the screen compares the copy marked to keep against the others one at a time, with a "Next copy" button that cycles (`CompareScreenViewModel.NextCandidate`).
- **Shared delete:** `DuplicateGroupResolver` (`RemoveIssues`, `Acknowledge`) was extracted from `NeedsReviewViewModel`, whose resolve and dismiss now call it, and Compare uses it too (Recycle Bin through `LibraryDeletionHelper`). The extraction is the only change to the duplicate review's behaviour: none.
- **Pairing:** `PageAligner` (window of 8 pages, difference at most 12 of 64 bits, ties to the page nearest the expected one), `PageHashCache` (memory only), hashes taken with `PageHasher.TryCompute` on a page decoded by `PageDecodeCore.DecodeSinglePage` off the UI thread; B is shown at its default pairing at once and moves when the pairing arrives (a request id drops stale results). The offset is remembered per pair for the life of the view model (the visit). `PageHashProvider` is the test seam.
- **Facts strip:** native page size from `ReaderImagePipeline.PeekPageSize` (the header, not the downsampled bitmap), file size (binary megabytes, like Explorer), format from the extension, page count, bytes per page; badges need a 5% margin and are text, not colour alone.
- **Navigation:** screen key `compare`; `NavigationEntry` gained an optional `SecondaryEntityId` so Back and Forward replay the pair; a compare is never restored as the last screen (it persists as the library). `IsInReader` includes it, so the rail and status bar hide. Leaving by any route releases both files (`OnCurrentScreenChanged`), and Escape closes it.
- **Entry points:** "Compare files…" on the Library issue menu with exactly two selected (`LibraryScreenViewModel.CompareSelectedIssues`, refusing remote, missing or non-file comics with a toast) and a Compare button on each duplicate group. The Detail screen's issue list is not wired in this pass.
- **Keys** are local to the screen (`CompareScreen.axaml.cs`, caught on the way down): Left/Right, PageUp/PageDown, Home, Space or F to flip, M, 0, 1 and 2; not in the remappable registry.
- **Memory:** each side opens its pipeline with the default budget (the design's "half each" was not built).
- **Not verified:** how the two-canvas layout feels, alignment quality on real editions (the match chip and offset buttons are the escape hatch), and the flicker's usefulness for spotting differences.
