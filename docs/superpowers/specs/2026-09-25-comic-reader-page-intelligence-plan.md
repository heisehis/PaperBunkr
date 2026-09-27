# Comic reader — Page intelligence (slice B) — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md*

**Status 2026-09-25:** Steps 1–13 built and unit/service tested; **on-screen check by the user still outstanding** (after
Steps 3, 6/7 and 12), all uncommitted. Slice A is committed (`06aec0c`). The design's build order was kept:
Steps 1–3 = read-state fix + #6, Step 4 = #13, Steps 5–7 = #12, Steps 8–12 = #9, Step 13 = docs. Deviations found while
building are listed in the spec's "Implementation notes" (in-canvas picker instead of a Popup, one archive open per file for
hashing, Run-now instead of a separate Scan-now button, no completion alert, `Reader.PageAdSeeder`).

## Corrections found while surveying (fold into the spec's "Implementation notes" at Step 13)

- **Third read formula in the reader.** `ReaderScreenViewModel.TrackSessionProgress` (l.2331) emits Finished when
  `100.0 * pageIndex / PageCount >= 95`, which is another 0-based formula (the spec names only `ReadPercentage` and the
  `read` template token). It is aligned to CE's `(pageIndex+1)*100/PageCount` in Step 4, since it decides the Finished
  event and the tracker auto-sync.
- **`IssueReadStateResolver.MarkAsRead` keeps its one-page hack.** With CE's formula `LastPageRead = 0` is still
  "unread", so a one-page issue still needs `LastPageRead = 1`. No change needed there; a test pins it (Step 1).
- **Keys are not registry-only.** `Reader.JumpBack` works through three pieces: the registry entry, a `…Key` property
  on the VM (`GetKeys` in `Load`, l.1105), and a `…Gesture` styled property plus `AnyMatches` branch in
  `PageCanvas` (l.197, l.2351), bound in `ReaderScreen.axaml` (l.355). `Reader.ReportBadPage` needs all three. No
  existing binding uses `Key.X`.
- **`LoadIssue(issueId, readingListId)` has no start page.** "Open in reader" from Library Health needs an optional
  `startPage` (passed as `Load`'s existing `forcedStartPage`), added in Step 7.
- **Library Health lives inside `PreferencesScreenViewModel`**, not its own VM: `RefreshLibraryHealth(context)` builds
  `MissingFileItems` / `EmptyIssueItems` / `EmptySeriesItems` from `MissingFileRowViewModel`; the UI is in
  `Views/Preferences/LibrarySection.axaml`. The new "Reported pages" list follows that shape.
- **Needs Review has no own view file.** Its sections render in `Views/MigrationOverlay.axaml` (and the pending badge in
  `LibrarySection.axaml`), each row type with a `DataTemplate` (`DuplicateGroupRowTemplate` is the closest model).
- **`PaperbunkrDbContextModelSnapshot.cs` and `PaperbunkrDbContext.cs` are already modified and several recent
  migrations are untracked** (other sessions: `AddLibrarySnapshots` … `AddScrapePermanentlySkipped`). A
  `dotnet ef migrations add` here can sweep another session's unmigrated model changes into ours. Before keeping any new
  migration, read its `Up()`: it must contain only our tables/columns. If it contains foreign changes, stop and ask
  rather than trimming by hand (the snapshot would then disagree with the migration).
- The rest of the working tree is shared (`Issue.cs`, `Series.cs`, `MainViewModel.cs`, `ScheduledTaskCatalog.cs`,
  scraper files …). Edit narrowly, `git diff` each shared file before and after, never revert another session's hunks.
- Test runs: check `Get-Process testhost` first, use `--blame-hang-timeout 90s --blame-hang-dump-type none`. The full
  App/Data suites are known to report "Test host process crashed" after all tests pass; run Data's Migration and
  non-Migration filters separately.

## Step 1: Read-state fix (Data)
**Files:** `src/Paperbunkr.Data/Metadata/IssueMetadataExtensions.cs` (edit),
`src/Paperbunkr.Data.Tests/IssueMetadataExtensionsTests.cs` (edit), `src/Paperbunkr.Data.Tests/IssueReadStateResolverTests.cs` (edit)
**What:** `ReadPercentage` = 0 when `LastPageRead` is null/0 or `PageCount` unknown, else
`Clamp((LastPageRead+1)*100/PageCount, 1, 100)`. `IsUnread` = `LastPageRead is null or 0`. `IsInProgress` =
`!IsUnread && ReadPercentage < 95`. `HasBeenRead` unchanged. Update the doc comment (it currently says 0-100 "clamped"
with no CE `+1`). Leave the smart-list formula and the `read` template token alone.
**Depends on:** none
**Verify:** rewrite `ReadPercentage_ComputesClampedPercent` and `HasBeenRead_UsesCeVerified95PercentThreshold_Not90`;
add edge cases (LP null, 0, 1, last, PageCount 0/null with LP > 0 → in progress, 10-page issue at LP 9 → 100, 20-page at
LP 18 → 95). Add a `MarkAsRead` test for the one-page hack under the new formula. Then run the resolver/consumer tests:
`HomeFeedResolver`, `InsightsResolver`, `TrackerProgressCalculator`, `TrackerSyncResolver`, `StatsResolver`,
`LibrarySnapshotService`, and the App tests that name `ReadPercentage`/`IsUnread` (`ReadStateGlyphTests`, `StatusBadgeTests`,
`HomeScreenViewModelTests`, `DetailTabsViewModelTests`, `IssueListFieldCatalogTests`). Fix expectations only where the
change is the intended one; anything else is a finding to report.

## Step 2: Skip settings, migration, Preferences toggles
**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit), `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit, shared:
`HasDefaultValue`), `src/Paperbunkr.Data/Migrations/*_AddPageSkipSettings.cs` + Designer + snapshot (via
`dotnet ef migrations add AddPageSkipSettings --project src/Paperbunkr.Data`),
`src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit: properties, load at l.923, `On…Changed` →
`PersistBehaviorSetting`), `src/Paperbunkr.App/Views/Preferences/ReaderSection.axaml` (edit, new rows in the `reader.zoomNav`
group or a new "PAGE SKIPPING" group), `src/Paperbunkr.Data.Tests/AddPageSkipSettingsMigrationTests.cs` (new),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:** `SkipDeletedPages` (default true) and `SkipAdvertisementPages` (default false), both with `HasDefaultValue`
(true needs the explicit default in the model or existing rows read as false; see the EF `HasDefaultValue` gotcha in the
Metron memory). Migration `Down()` is a **no-op**. Two `SettingsRow` toggles, wording "Skip pages tagged Deleted" /
"Skip pages tagged Advertisement".
**Depends on:** none (independent of Step 1)
**Verify:** migration test (columns exist; existing AppSettings row reads Deleted=true, Ads=false); Preferences VM tests
mirroring `TogglingResetZoomOnPageChange_PersistsToAppSettings`. Run the whole `Paperbunkr.Data.Tests` project (snapshot
is shared), Migration and non-Migration filters separately. Inspect the generated `Up()` per the survey note.

## Step 3: #6 page skipping in the paged reader
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit: `Load` l.1072 reads the two settings;
`NextPage` l.2527, `PreviousPage` l.2500; new hint chip state), `src/Paperbunkr.App/Views/ReaderScreen.axaml` (edit,
narrow: new chip beside the jump-back chip), `src/Paperbunkr.App/Services/Reader/PageSkipStepper.cs` (new, pure),
`src/Paperbunkr.App.Tests/PageSkipStepperTests.cs` (new), `src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit)
**What:**
- `PageSkipStepper.FindNext(current, direction, pageCount, isSkippable)` → target index or null: walks in the direction
  and returns the first page that is not skippable; the current page never counts as skippable (CE
  `SeekNextPage`). Null means nothing further, so the caller falls through to `TriggerChapterTransition` (end card from
  slice A), at the start likewise.
- `NextPage`/`PreviousPage` compute their normal step (spread stepping unchanged), and if the landing page is skippable,
  hand over to the stepper starting from that landing page in the same direction. Pairing is recomputed by
  `RefreshCurrentPage` after `GoToPage`, so it is untouched. Paged mode only: continuous mode and direct jumps
  (`JumpToPage`, thumbnail, bookmark, jump-back) never call the stepper. `PageLabel` keeps real page numbers.
- `isSkippable(index)` = tag Deleted and `SkipDeletedPages`, or Advertisement and `SkipAdvertisementPages`, read from
  `_pageOverrides` (untagged = Story). Tags stay live because `SetPageOverride` already updates `_pageOverrides`.
- Hint chip `SkippedPagesHint` ("Skipped 2 pages", singular for 1) with the same timer-plus-`internal void On…Expired`
  test-seam pattern as the jump-back chip; cleared on the next page change.
**Depends on:** Step 2
**Verify:** stepper unit tests (both directions, run of several skippable pages, all-skippable ahead → null, current page
skippable still returns a neighbour, settings off → never skips). VM tests: `NextPage` skips a Deleted page by default,
does not skip Ads by default, skips Ads when on, end card appears when everything ahead is skippable, `PreviousPage` at
the first real page triggers the backward transition, jump/thumbnail ignores skipping, hint text and expiry via the test
seam, spread pairing lands correctly after a skip. **On-screen check by the user** (turning through a comic with a Deleted
page and an ad; toggling both settings).

## Step 4: #13 story-end finish
**Files:** `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit: `Load`, `SetPageOverride` l.1855,
`GoToPage` l.2285, `TrackSessionProgress` l.2331, `FlushPendingPositionSave` l.2440),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs` (edit)
**What:**
- `_storyEndIndex`: last index whose tag is not Advertisement or Deleted (untagged = Story); recomputed in `Load` and in
  `SetPageOverride`. If every page is tagged, fall back to the last page.
- In `GoToPage`, after saving `LastPageRead`, if the new index equals `_storyEndIndex` and it is **before** the last page,
  set `LastPageRead` to `PageCount-1` (same value `IssueReadStateResolver.MarkAsRead` writes, including the one-page case,
  which cannot apply here) and call `EmitFinishedIfNeeded()`. Nothing changes when the story end is the last page. The
  reader's own position (`_currentPageIndex`) is untouched, only the persisted state.
- Align `TrackSessionProgress` to `100.0 * (pageIndex + 1) / PageCount >= 95` (survey correction), same for the
  position-save flush path that calls it.
**Depends on:** Step 1 (formula), Step 3 not required
**Verify:** VM tests: story end before trailing ads → persisted `LastPageRead` = last page and one Finished row recorded
once per session; story end = last page → no extra write; tagging the trailing page as ad/untagging mid-session moves
the story end; formula alignment (10-page issue, reaching the last page emits Finished; before it, not); existing
Finished/tracker-sync tests still pass.

## Step 5: #12 `PageReport` data
**Files:** `src/Paperbunkr.Data/Entities/PageReport.cs` (new), `PageReportReason.cs` (new enum: Corrupt, Blank, LowRes, Other),
`src/Paperbunkr.Data/Entities/Issue.cs` (edit only if a navigation collection is wanted; prefer none, to keep the shared
file untouched), `src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit: `DbSet`, unique index on
(`IssueId`,`PageNumber`), cascade on Issue delete), migration `AddPageReports` (+ Designer + snapshot),
`src/Paperbunkr.Data/Metadata/PageReportService.cs` (new: `Upsert`, `Remove`, `Acknowledge`),
`src/Paperbunkr.Data.Tests/PageReportServiceTests.cs` + `AddPageReportsMigrationTests.cs` (new)
**What:** entity per the spec (`Id`, `IssueId`, `PageNumber`, `Reason`, `CreatedAt`, `Acknowledged`). `Upsert` updates the
reason and clears `Acknowledged` on a repeat report. No-op `Down()`. Confirm what `LibraryDeletionHelper.RemoveIssue`
does for related rows so removing an issue does not orphan reports.
**Depends on:** none
**Verify:** service tests (insert, re-report updates the reason, remove, unique index enforced), migration test, delete-issue
cleanup test, full Data suite. Inspect the migration `Up()`.

## Step 6: #12 reader popup, command, key, menus, chip
**Files:** `src/Paperbunkr.App/Models/KeyboardCommandRegistry.cs` (edit: `ReaderReportBadPage = "Reader.ReportBadPage"`, `X`,
`NavigationGroup`, `ConflictContext.Always`), `src/Paperbunkr.App/Views/PageCanvas.cs` (edit: `ReportBadPageGesture` +
`ReportBadPageCommand` styled properties and the `AnyMatches` branch), `src/Paperbunkr.App/Views/ReaderScreen.axaml` +
`.axaml.cs` (edit, narrow: gesture/command binding, reason popup, "Reported page N · Undo" chip),
`src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit: `ReportBadPageKey`, `IsReportPopupOpen`, commands
`ReportBadPage` (open popup), `ReportPageReason(reason)`, `UndoPageReport`, chip state + timer),
`src/Paperbunkr.App/ViewModels/ReaderPageContextMenuBuilder.cs` (edit: menu item in both builders; the main-page builder
reports the VM's current page, the thumbnail builder the thumbnail's index),
`src/Paperbunkr.App.Tests/ReaderScreenViewModelTests.cs`, `ReaderPageContextMenuBuilderTests.cs`,
`KeyboardShortcutsTests` (whichever file pins the registry list) (edit)
**What:** Popup with Corrupt / Blank / Low resolution / Other, keys 1–4, Esc cancels. Choosing writes through
`PageReportService.Upsert` and shows the undo chip; Undo removes the row (or restores the previous reason if it was a
re-report). **Routed-event rule (CLAUDE.md):** the popup close, and any collection mutation triggered by a button inside
the popup, go through `Dispatcher.UIThread.Post`. Paged and continuous both report the current page.
Because the registry drives the Keyboard Shortcuts Preferences section, X becomes remappable with no UI work there.
**Depends on:** Step 5
**Verify:** VM tests (open/choose/cancel, upsert on repeat, undo, key property populated from bindings, menu items present
and bound to the right page, popup close is deferred: assert state after `TestDispatcher.Drain()`), registry test updated.
Load `avalonia` and read `avalonia-pro-max/review-checklist` before calling the XAML done; new `.axaml` files (none
planned) would need their code-behind in the same step (AVLN2000). **On-screen check by the user** after Step 7.

## Step 7: #12 Library Health "Reported pages"
**Files:** `src/Paperbunkr.App/ViewModels/PageReportRowViewModel.cs` (new), `PreferencesScreenViewModel.cs` (edit:
`PageReportItems`, `HasPageReportItems`, load in `RefreshLibraryHealth`, three commands),
`src/Paperbunkr.App/Views/Preferences/LibrarySection.axaml` (edit: new section under Missing Files, same row style as
`MissingFileRowViewModel`), `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit: `LoadIssue(issueId,
readingListId = null, startPage = null)`), `src/Paperbunkr.App.Tests/LibraryHealthPageReportsTests.cs` (new)
**What:** rows show "Series #N · page P · reason". Actions: **Open in reader** (navigate to the reader at that page),
**Tag as Deleted** (writes an `IssuePage` Deleted row, resolves the report), **Dismiss** (sets `Acknowledged`). Reports are
never auto-resolved. Row removals that run from a click inside the row's own `ItemsControl` are deferred one tick
(`Dispatcher.UIThread.Post`, fresh context inside the lambda), copying the pattern in `RemoveMissingFile`.
**Depends on:** Steps 5, 6
**Verify:** VM tests (list contents, Tag as Deleted creates/updates the `IssuePage` row without clobbering rotation/spread
on an existing row, Dismiss hides it, reopening after a re-report un-hides it, Open passes the right page, deferred
removal via `TestDispatcher.Drain()`). **On-screen check by the user** for the whole #12 group.

## Step 8: #9 ad-detection data
**Files:** `src/Paperbunkr.Data/Entities/AdPageHash.cs`, `PageHash.cs`, `AdPageProposal.cs`, `AdPageProposalStatus.cs` (new),
`src/Paperbunkr.Data/PaperbunkrDbContext.cs` (edit: three `DbSet`s, unique indexes on `PageHash(IssueId,PageNumber)` and
`AdPageProposal(IssueId,PageNumber)`, `Hash` as `long`), migration `AddAdPageDetection` (+ Designer + snapshot),
`src/Paperbunkr.Data.Tests/AddAdPageDetectionMigrationTests.cs` (new)
**What:** entities exactly as in the spec §5. `AdPageProposal` is separate from `MetadataProposal`. No-op `Down()`. Cascade
deletes from Issue for `PageHash` and `AdPageProposal`; `AdPageHash.SourceIssueId` is nullable-and-no-cascade so deleting
a source issue keeps the ad in the library (decide and record in the entity's doc comment).
**Depends on:** none (independent of Steps 1–7)
**Verify:** migration test, model tests for unique indexes, full Data suite. Inspect the migration `Up()`.

## Step 9: `PageHasher` (dHash)
**Files:** `src/Paperbunkr.App/Services/AdDetection/PageHasher.cs` (new), `src/Paperbunkr.App.Tests/PageHasherTests.cs` (new)
**What:** `Compute(Bitmap)` → `long`: convert to a 9×8 grayscale via SkiaSharp (already referenced by `Paperbunkr.App`;
render to a small `SKBitmap` with high-quality resampling), compare horizontal neighbours for 64 bits. `Compute(string
filePath, int pageIndex)` uses `PageDecodeCore.DecodeSinglePage` (off-UI-thread safe; it is `internal`, so this class
sits in the same assembly). `Distance(a, b)` = `BitOperations.PopCount(a ^ b)`.
**Depends on:** none
**Verify:** synthetic-bitmap tests: identical → 0; the same image re-encoded as JPEG at quality 60 and slightly scaled → ≤ 6;
an unrelated image (different pattern) → > 6; flat image handled (no divide-by-zero, deterministic). No new NuGet package.

## Step 10: `AdPageDetectionService` and seeding
**Files:** `src/Paperbunkr.App/Services/AdDetection/AdPageDetectionService.cs` (new),
`src/Paperbunkr.App/Services/AdDetection/AdHashSeeder.cs` (new), `ReaderScreenViewModel.cs` (edit: `SetPageOverride` calls
the seeder when a page becomes Advertisement / stops being one), `src/Paperbunkr.App.Tests/AdPageDetectionServiceTests.cs`
+ `AdHashSeederTests.cs` (new; fixtures are CBZs generated in the test with the synthetic bitmaps from Step 9)
**What:**
- Service `ScanAsync(IProgress<…>, CancellationToken)`: per linked, present issue, hash pages 0–2 and the last 10 when the
  file stamp differs from `PageHash.ContentStamp` (use the same stamp the cover-verification code uses; reuse, do not
  invent); skip pages that already have an `IssuePage` tag; match against `AdPageHash` at distance ≤ 6; create a Pending
  `AdPageProposal`. A page with any existing proposal, Rejected included, is never proposed again. Never writes an
  `IssuePage`. Per-file exceptions swallowed and counted; cancellable.
- Seeder: tagging Advertisement hashes the page off the UI thread (`Task.Run`, fresh context) and adds an `AdPageHash`
  unless one within distance 2 already exists; removing the tag deletes the hash with that
  (`SourceIssueId`,`SourcePageNumber`). Window (3/10) and thresholds (6, 2) are named constants.
**Depends on:** Steps 8, 9
**Verify:** service tests against generated CBZ fixtures: window bounds, unchanged stamp skipped, tagged page skipped,
Rejected never re-proposed, matched vs unmatched, cancellation mid-scan, corrupt file swallowed. Seeder tests: seeds once,
near-duplicate not re-added, un-tagging removes the hash. Reader VM test that tagging triggers the seeder (via an injectable
seam, no real decode in the VM test).

## Step 11: Scheduled task + "Scan now"
**Files:** `src/Paperbunkr.App/Services/Scheduling/ScheduledTaskCatalog.cs` (edit, shared/dirty: narrow: new const
`DetectAdPages = "detect-ad-pages"` and descriptor, resource class `DiskCpu`, `DefaultEnabled: false`, interval 7 days,
`ActivityJobKind.Other` unless a better kind exists), `ScheduledRunStore.cs` (only if the task needs a mirror like
`VerifyCovers`; it does not), `PreferencesScreenViewModel.cs` + `Views/Preferences/LibrarySection.axaml` or
`AutomationSection.axaml` (edit: "Scan now" button running the same service as an Activity Center job), Activity Center
completion alert linking to Needs Review, `src/Paperbunkr.App.Tests/ScheduledTaskCatalogTests.cs` (edit) + VM test (new)
**What:** modelled on `VerifyCovers`. Per the standing feedback, jobs, alerts and progress all go through the Activity Center
(planned now, not after a reminder). The completion message reads "Found N possible ad pages" / "No new ad pages found".
**Depends on:** Step 10
**Verify:** catalog test (present, off by default, correct resource class, no priority collision: check `Priority` values
already in use, the list ends around 13+), Scan-now VM test with a fake service, alert emitted. Run the scheduler tests.

## Step 12: Needs Review "Advertisement pages" section
**Files:** `src/Paperbunkr.App/ViewModels/AdPageProposalRowViewModel.cs` + `AdPageGroupRowViewModel.cs` (new),
`NeedsReviewViewModel.cs` (edit: `AdPageProposalItems`, `HasAdPageItems`, `RefreshAdPageItems`, add to `Refresh`,
`NotifyCountsChanged`, `HasPendingItems`, Accept all / Reject all commands), `src/Paperbunkr.App/Views/MigrationOverlay.axaml`
(edit: section + row `DataTemplate`, modelled on `DuplicateGroupRowTemplate`), `LibrarySection.axaml` only if the
pending badge needs no change (it binds `HasPendingItems`, so probably not),
`src/Paperbunkr.App.Tests/NeedsReviewAdPagesTests.cs` (new)
**What:** proposals grouped by `MatchedAdHashId`: the matched ad's thumbnail (decode the source page off-thread, cache with
the existing thumbnail helpers), match count, **Accept all** / **Reject all**, and an expander with single pages, each with
Accept/Reject. Accept writes a normal `IssuePage` Advertisement row (create or update, keep rotation/spread) and marks the
proposal Accepted, so #6 skipping applies immediately if the user enabled it; it does not seed a new hash (the matched
hash already covers it). Reject marks Rejected (never re-proposed). Row removal / bulk actions that mutate the collection
from a click inside it are deferred one tick, as in `KeepLargestInAllGroups` neighbours (`Dispatcher.UIThread.Post`, fresh
context inside).
**Depends on:** Steps 8, 10 (Step 11 not required)
**Verify:** VM tests (grouping, counts, Accept all writes N `IssuePage` rows and no duplicate on an already-tagged page,
Reject all, single-row actions, `HasPendingItems` true/false, refresh after resolution, deferred removal). **On-screen check
by the user** for the whole #9 group, with real proposals from a seeded ad.

## Step 13: Docs and close-out
**Files:** `docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md` (add "Implementation notes" listing
every deviation found while building, incl. the survey corrections above), `docs/paperbunkr-todo.md` (slice B status,
commit refs, and what was verified, not just claimed), `docs/Paperbunkr-Roadmap.md` (slice B line), user wiki page for the
reader if one names skipping/reporting (`wiki/`), memory: new `project_paperbunkr_comic_reader_slice_b.md` + `MEMORY.md`
line, and update the slice A memory's "uncommitted" note (it is committed as `06aec0c`).
**Depends on:** all previous
**Verify:** `dotnet build` of the whole solution with the AVLN weave check (if any XAML build failed after `CoreCompile`, delete
the App `.dll`/`.pdb` before rebuilding, per CLAUDE.md), then the `avalonia-pro-max/review-checklist` pass over every XAML
file touched (`ReaderScreen.axaml`, `ReaderSection.axaml`, `LibrarySection.axaml`, `MigrationOverlay.axaml`): tokens not hex
colours, squircle radius `PbRadiusChip` for chips (no `CornerRadius 999`). Full test run split as noted above.

## Test strategy summary

- Pure logic (formulas, stepper, hasher, story end) is unit-tested with no UI; the reader VM is tested headlessly with
  synchronous state assertions, timer behaviour through `internal void On…Expired` seams, dispatcher work through
  `TestDispatcher.Drain()`. Never `await window.FadeOutAndCloseAsync()`.
- Data changes each get a migration test and a service test; every new migration's `Up()` is read before it is kept.
- Services that decode pages (hasher, detection) use generated CBZ fixtures, never real library files, never the network.
- On-screen verification is the user's, after Steps 3, 7 and 12; no FlaUI scripting without asking.
