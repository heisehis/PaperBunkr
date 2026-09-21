# Comic reader — Flow & defaults (slice A) — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-design.md*

**Status 2026-09-21:** Steps 1-8 done. Step 1 ended up in `Paperbunkr.App/Services/Reader` (test in `Paperbunkr.App.Tests`), Step 4 became a Details Info sub-tab addition in `DetailTabsViewModel`/`DetailTabs.axaml` (shared by comic and manga), and the end card dropped its Flag button. See the spec's Implementation notes.

## Corrections found while surveying (fold into the spec's "Corrections" list at Step 8)

- `Series.PageLayoutMode` (nullable) already exists and the reader already writes it, so #4 needs only **two** new
  `Series` columns (`PageFitModeOverride`, `AutoRotateOverride`), not three.
- `Continuity` is series-level (`ContinuityMembership` joins Continuity↔Series), so it defines no issue order. The
  only issue-level order beyond reading lists and series is `StoryEvent` via `EventMembership.Position`. "Event /
  continuity order" in the spec means **StoryEvent order only**.
- The reader has no top bar. Chrome is floating clusters (`chromeCluster`, top-left navigate cluster in
  `ReaderScreen.axaml`). The strip goes with that cluster, and the chip sits above the bottom chrome.
- `LoadIssue(issueId, readingListId)` only carries a reading-list anchor. An "opened-from Event" anchor does not
  exist and is **not added** in this slice; the strip falls back to Event membership when there is no list anchor.
- Today the card only appears when `AutoNavigateComics` is on and a neighbour exists; otherwise the reader just
  clamps (plus the review prompt). The new card always appears at the end of an issue, so this is a deliberate
  behaviour change for auto-navigate-off users (Step 5).
- The working tree is shared with uncommitted work from other sessions (`ReaderScreen.axaml`,
  `PaperbunkrDbContextModelSnapshot.cs`, several ViewModels are already modified). Edit narrowly, run
  `git status`/`git diff` on each shared file before and after, and do not revert others' hunks.

## Step 1: `ReadingOrderResolver`
**Files:** `src/Paperbunkr.Data/ReadingLists/ReadingOrderResolver.cs` (new),
`src/Paperbunkr.Data.Tests/ReadingOrderResolverTests.cs` (new)
**What:** Static/pure class over a `PaperbunkrDbContext`:
- `ResolveNeighbour(context, issueId, seriesId, readingListId, forward)` → `ReadingOrderStep?`
  (`From`, `To`, `Position`, `Total`, `SourceLabel`). Ports the existing logic verbatim: reading list by `SortOrder`
  skipping `FileIsMissing` and null-series rows and stopping at the list boundary, otherwise
  `series.Issues.OrderByNumber()`.
- `ResolveContext(context, issueId, readingListId)` → `ReadingContext?` (`Kind` list/event, `Label`, `Position`,
  `Total`, `PrevIssueId`, `NextIssueId`). Rule: opened-from list wins; else Event membership (lowest StoryEvent
  id if several); else the most recently opened reading list containing the issue; else null. Uses
  `EventMembership.Position`, skipping missing files for prev/next.
**Depends on:** none
**Verify:** new Data tests: list order with missing files skipped, list boundary stops (no series fallback), series
order, event order, precedence, no-context null, position/total. Run `dotnet test src/Paperbunkr.Data.Tests`.

## Step 2: Reader VM consumes the resolver (behaviour-preserving)
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit)
**What:** Replace the bodies of `TryResolveAdjacentIssue` (l.2571) with a call to
`ReadingOrderResolver.ResolveNeighbour`. Keep the method signature and its `TryGetAdjacentIssuePreview` /
`NavigateToAdjacentIssue` callers unchanged so nothing else moves yet.
**Depends on:** Step 1
**Verify:** existing `ReaderScreenViewModelTests` (adjacent-issue, reading-list anchor, chapter-transition cases)
pass untouched. Use `--blame-hang-timeout 90s --blame-hang-dump-type none`, and check `Get-Process testhost` first.

## Step 3: #4 series-level fit / auto-rotate defaults (data + reader)
**Files:** `src/Paperbunkr.Data/Entities/Series.cs` (edit), `src/Paperbunkr.Data/Migrations/*_AddSeriesReaderDefaults.cs`
+ Designer + `PaperbunkrDbContextModelSnapshot.cs` (new/regenerated via
`dotnet ef migrations add AddSeriesReaderDefaults --project src/Paperbunkr.Data`),
`src/Paperbunkr.Data/Metadata/ReaderDefaultsResolver.cs` (new),
`src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit: `Load` l.1114–1115, add
`ApplyFitModeToSeries` / `ApplyAutoRotateToSeries` commands),
`src/Paperbunkr.Data.Tests/IssueReaderOverridesTests.cs` + `SeriesReaderDefaultsTests.cs` (new/edit),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit)
**What:** Two nullable columns on `Series`. `ReaderDefaultsResolver.EffectiveFitMode/AutoRotate(issue, series,
appSettings)` = `issue ?? series ?? global`. `Load` uses it. `SetFitMode` and `ToggleAutoRotate` keep writing only the
issue. New "Apply to series" commands write the series column. Migration `Down()` is a no-op and no earlier
migration is edited.
**Depends on:** none (independent of Steps 1–2)
**Verify:** resolver unit tests (each level wins, null falls through); a migration test that the columns exist and
default to null; VM tests that Load picks the series value, that an issue override beats it, and that Apply writes
only the series column. Run the whole `Paperbunkr.Data.Tests` project, since the snapshot is shared.

## Step 4: #4 detail-screen control
**Files:** `src/Paperbunkr.App/Controls/SeriesReaderDefaultsControl.axaml` + `.axaml.cs` (new, code-behind in the
same step per the AVLN2000 gotcha), `src/Paperbunkr.App/ViewModels/SeriesReaderDefaultsViewModel.cs` (new),
`DetailScreenViewModel.cs`, `MangaDetailScreenViewModel.cs`, `Views/DetailScreen.axaml`,
`Views/MangaDetailScreen.axaml` (edit), tests in `src/Paperbunkr.App.Tests` (new)
**What:** One shared control showing the series' fit / auto-rotate default with a Clear action. Bind it in both
detail screens. Load `avalonia` and read the matching subskills first (components, review-checklist).
**Depends on:** Step 3
**Verify:** VM tests (shows null as "Not set", Clear nulls the column). Full `dotnet build` (weave check per
CLAUDE.md: also delete the App `.dll`/`.pdb` if a XAML failure occurred), then the review checklist. The user checks
it on screen.

## Step 5: #5 end-of-issue card
**Files:** `ReaderScreenViewModel.cs`, `Views/ReaderScreen.axaml` (edit, shared/dirty: narrow edits),
`Models/ChapterTransitionState.cs` (edit: add `EndCard`), `ReaderScreenViewModelTests.cs`
**What:** `TriggerChapterTransition(forward:true)` at the last page now shows the end card whether or not a neighbour
exists or `AutoNavigateComics` is on. Content: "Finished · #n", next issue (cover, label, source + position from the
resolver) with Continue, Mark read, Rate, Flag for review, Back to library. With auto-navigate on it runs a visible
countdown that any key cancels (reuse `_chapterTransitionHoldTimer`); off, it waits. Last issue: "That's the last
issue". `MaybePromptReviewOnFinish` folds into Rate/Flag (keep the `PromptReviewOnFinish` gate and
`EmitFinishedIfNeeded`). Rating uses the existing rating path, Flag the existing needs-review path (find them; do not
invent new writers). The backward (previous issue) transition keeps today's card. Button clicks that close the card or
mutate state defer with `Dispatcher.UIThread.Post` (CLAUDE.md routed-event rule).
**Depends on:** Steps 1–2
**Verify:** VM tests: card at last page with auto on/off, countdown cancel, last-issue text, Mark read writes
`LastPageRead = PageCount-1`, review gate preserved. Existing chapter-transition tests updated where the
auto-navigate-off expectation changes. On-screen check by the user.

## Step 6: #27 context strip
**Files:** `ReaderScreenViewModel.cs`, `Views/ReaderScreen.axaml` (edit), `ReaderScreenViewModelTests.cs`
**What:** On `Load`, call `ResolveContext`; expose `ContextStripLabel`, `HasContextStrip`, prev/next commands that
`Load` the neighbour while preserving the same list anchor. Render as a chip in the top-left navigate cluster (hides
with chrome) and flash it for ~4s on open via a `DispatcherTimer`. No strip when the resolver returns null.
**Depends on:** Step 1
**Verify:** VM tests: label/position for list, event and none, prev/next stay in the strip's context, flash timer state
via a test seam like `OnChapterTransitionHoldTick`. On-screen check.

## Step 7: #10 jump-back chip
**Files:** `Models/KeyboardCommandRegistry.cs` (edit: new `ReaderJumpBack` command + default gesture, checking conflicts
with existing bindings), `ReaderScreenViewModel.cs`, `Views/ReaderScreen.axaml`, `ReaderScreenViewModelTests.cs`,
`KeyboardCommandRegistry` tests
**What:** Record one previous position when a jump of more than 5 pages comes from the thumbnail sidebar, go-to-page,
bookmark or search hit (not from prev/next, arrow keys, or continuous scroll). Chip shows "Back to page N" and the
shortcut, expires after ~6s or on the next page turn (test seam timer). The command returns to the stored page and
clears it. Registered, so it is remappable and shown via `GetShortcutHint`.
**Depends on:** none
**Verify:** VM tests: threshold, expiry, next-turn clears, single stored position, return restores. Registry test for
the new command. On-screen check.

## Step 8: Docs, review and close-out
**Files:** the design spec (fold in the corrections above), `docs/Paperbunkr-Roadmap.md`,
`docs/paperbunkr-todo.md` (status, what was verified), memory note
**What:** Run `avalonia-pro-max/review-checklist` on all new XAML. Full `dotnet build` plus the full test suite. Update
the roadmap slice A status and the todo doc. Record what the user still needs to check on screen.
**Depends on:** Steps 1–7
**Verify:** solution builds with 0 errors, the suite passes (report the actual counts), and the docs match the code.

## Test strategy notes

- Headless tests assert state synchronously; never `await window.FadeOutAndCloseAsync()`.
- Use timer test seams (`internal void On…Tick`) rather than real clocks.
- Orphaned `testhost` processes lock the test `bin` (MSB3027): check `Get-Process testhost` before each run.
- No FlaUI or computer-use scripting. On-screen verification is the user's.
