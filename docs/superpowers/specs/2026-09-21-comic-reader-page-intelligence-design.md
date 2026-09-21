# Comic reader — Page intelligence (slice B) design

Date: 2026-09-21. Status: design approved in conversation, awaiting written-spec review.
Source: "Comic reader pitch" in `docs/Paperbunkr-Roadmap.md`, slice B (items #6, #9, #12, #13). Slice A
(`2026-09-21-comic-reader-flow-and-defaults-design.md`) is built; this is the next slice. All four items are in this
one spec, built in the order given under "Build order".

## Facts this design rests on (verified 2026-09-21)

- **CE paging filter.** `ComicPageType` is a flags enum; `All` excludes `Deleted`. `ComicBookNavigator.SeekNextPage`
  walks in a direction and counts a page only if it matches the filter **or is the current page**. The filter is
  persisted (`Settings.PageFilter`, default `All`), so CE skips only Deleted pages by default and shows ads.
- **Paperbunkr page tags.** `PageType` has four values (Story, Cover, Advertisement, Deleted), not flags. `IssuePage`
  rows are sparse and per-issue (no row means Story). `_pageOverrides` in `ReaderScreenViewModel` holds them but
  paging ignores the type. Tags are not written to ComicInfo.xml.
- **CE read state.** `ReadPercentage` is 0 when `LastPageRead <= 0`, otherwise `((LastPageRead+1)*100/PageCount)`
  clamped to 1..100. `HasBeenRead` is `>= 95`. `MarkAsRead` sets `LastPageRead = PageCount-1`.
- **Paperbunkr read state.** `IssueMetadataExtensions.ReadPercentage` has no `+1`, so an issue under 20 pages can
  never reach 95% at its last page (mark-read on a 10-page issue gives 90%). Smart lists already use CE's formula.
  The `read` template token (`FieldResolvers`) is a third, separate formula.
- **No perceptual-hash code exists.** SkiaSharp is referenced only by `Paperbunkr.App`. `PageDecodeCore.DecodeSinglePage`
  is the safe off-UI-thread one-shot decode. Library Health stores flags on `Issue` only. Needs Review is a vertical
  stack of sections with no bulk action for proposals. Scheduled tasks support a default-off, Activity Center-reporting
  task without a migration.

## Scope

In: #6 auto-skip tagged pages, #13 finish at story end plus the read-percentage fix, #12 report bad page, #9
auto-tag advertisement pages by page hash (proposals only).

Out: credit pages (need new `PageType` values), writing page tags to ComicInfo.xml, skipping in continuous mode,
sharing hashes or reports across remote libraries, auto-applying any proposal.

## 1. Read-state fix (Paperbunkr.Data)

- `ReadPercentage`: 0 when `LastPageRead` is null or 0 or `PageCount` is unknown; otherwise
  `(LastPageRead+1)*100/PageCount` clamped to 1..100 (CE).
- `IsUnread` becomes `LastPageRead is null or 0`. `IsInProgress` becomes `!IsUnread && ReadPercentage < 95`.
  `HasBeenRead` (`>= 95`) is unchanged.
- One behavioural difference to note: an issue with an unknown or zero `PageCount` and `LastPageRead > 0` is now
  in progress, where before it counted as unread.
- The smart-list formula is already CE-exact and stays. The `read` template token is left alone (different
  semantics).
- Update the two pinned tests in `IssueMetadataExtensionsTests` (`ReadPercentage_ComputesClampedPercent`,
  `HasBeenRead_UsesCeVerified95PercentThreshold_Not90`) and re-run the resolver tests that use these members
  (`HomeFeedResolver`, `InsightsResolver`, `TrackerProgressCalculator`).

## 2. #6 page skipping (paged mode only)

- `AppSettings.SkipDeletedPages` (default **on**, CE parity) and `SkipAdvertisementPages` (default **off**).
  Migration with a **no-op `Down()`**. Two toggles in Preferences → Reader.
- `NextPage`/`PreviousPage` step past pages whose tag is in the skip set, as CE's `SeekNextPage` does. The current
  page always counts, so the reader can never get stuck. If every page ahead is skippable, that is the end of the
  issue (so the end card from slice A shows); likewise at the start.
- Spread pairing is computed after landing on the target page. Skipping does not change the pairing rules.
- Unchanged: thumbnails, the dot strip, continuous mode, direct jumps (thumbnail, bookmark, jump-back) and
  `PageLabel`, which keeps real page numbers.
- A transient hint "Skipped 2 pages" appears when a page turn skipped pages, using the same in-reader chip pattern as
  the jump-back chip.

## 3. #13 story-end finish

- The **story end** is the last page not tagged Advertisement or Deleted (untagged counts as Story). Recomputed on
  `Load` and whenever a page tag changes.
- Reaching it sets `LastPageRead` to the final page, exactly as `IssueReadStateResolver.MarkAsRead` does, and emits
  the Finished reading event once per session. Nothing changes when the story end already is the last page.
- No change to list formulas: `HasBeenRead()` runs per row in large lists and cannot join page tags. The reader
  writes the finished state instead.

## 4. #12 report bad page

- **Entity** `PageReport` in Paperbunkr.Data: `Id`, `IssueId`, `PageNumber`, `Reason` (Corrupt, Blank, LowRes, Other),
  `CreatedAt`, `Acknowledged`. Unique index on (`IssueId`, `PageNumber`), so reporting a page twice updates its reason.
  Migration with a **no-op `Down()`**.
- **Reader:** command `Reader.ReportBadPage`, default key `X`, in the existing Navigation group (no Preferences UI
  change needed), remappable. It is also a menu item in the thumbnail and main-page context menus
  (`ReaderPageContextMenuBuilder`; the main-page builder needs the current page from the VM). The key opens a small
  Popup with the four reasons (1–4 or click, Esc cancels). Afterwards a chip reads "Reported page N · Undo".
- **Library Health** (Preferences): a "Reported pages" list with per-row Open in reader, Tag as Deleted, Dismiss.
  Reports are never auto-resolved.
- **Routed-event rule:** the popup close and the row removals are deferred one dispatcher tick
  (`Dispatcher.UIThread.Post`), per CLAUDE.md.

## 5. #9 advertisement-page detection

**Data (Paperbunkr.Data, one migration with a no-op `Down()`):**
- `AdPageHash`: the confirmed-ad library. `Id`, `Hash` (stored as `long`), `SourceIssueId`, `SourcePageNumber`,
  `CreatedAt`.
- `PageHash`: `IssueId`, `PageNumber`, `Hash`, `ContentStamp` (file stamp), unique on (`IssueId`, `PageNumber`).
- `AdPageProposal`: `Id`, `IssueId`, `PageNumber`, `MatchedAdHashId`, `Distance`, `Status` (Pending, Accepted,
  Rejected), `CreatedAt`, `ResolvedAt`, unique on (`IssueId`, `PageNumber`). A separate entity from
  `MetadataProposal` because that one is field-based with string values.

**App (Paperbunkr.App, because SkiaSharp lives only there):**
- `PageHasher`: 64-bit difference hash (dHash) from a 9×8 grayscale downscale, decoding via
  `PageDecodeCore.DecodeSinglePage` (safe off the UI thread). No new dependency.
- `AdPageDetectionService`: for each issue, hash the first 3 and last 10 pages when the file stamp changed, skip pages
  that already have a tag, match against `AdPageHash` at Hamming distance <= 6, and create Pending proposals. A page
  with any proposal, including Rejected, is never proposed again. Never auto-tags. Cancellable, per-file exceptions
  swallowed, progress through `IProgress`.
- **Seeding:** tagging a page Advertisement in the reader hashes it off the UI thread and adds it to `AdPageHash`
  (skipped if a hash within distance 2 already exists). Removing the tag removes the hash that page sourced, so a
  wrong tag cannot keep producing proposals. The window (3/10) and thresholds (6, 2) are constants, not settings.
- **Scheduled task** "Detect advertisement pages" in `ScheduledTaskCatalog`: resource class `DiskCpu`,
  `DefaultEnabled: false`, modelled on `VerifyCovers`. Preferences also gets a "Scan now" button. It runs as an
  Activity Center job with progress and a completion alert that links to the review section.
- **Review UI:** a new "Advertisement pages" section in the Needs Review stack, grouped by matched hash, showing the
  matched ad's thumbnail, the match count, **Accept all** and **Reject all**, and an expander listing single pages with
  Accept/Reject. It adds an `AdPageProposals` row VM, a `Has…Items` flag, a `Refresh…` method, and an entry in
  `HasPendingItems`/`NotifyCountsChanged`. Accepting writes a normal `IssuePage` Advertisement row (so #6 skipping
  applies immediately) and resolves the proposal. Row-removal buttons defer one dispatcher tick.

## Build order

1. Read-state fix and #6 (settings migration, Preferences toggles, paging).
2. #13 story-end finish.
3. #12 `PageReport`, reader popover and chip, Library Health list.
4. #9 entities and migration, `PageHasher`, detection service, scheduled task, Needs Review section.

Each step is built, tested and verified before the next. On-screen check by the user at the end of each step that
adds UI.

## Testing

- Unit: read formulas at the edges (LP 0, 1, last, unknown page count); skip stepping (both directions, all-skippable
  ahead/behind, spreads, setting off); story-end detection; `PageReport` upsert/undo; dHash on synthetic bitmaps
  (identical is 0, a recompressed copy is under 6, an unrelated image is over 6).
- Service: `AdPageDetectionService` against generated CBZ fixtures (window bounds, unchanged-stamp skip, tagged pages
  skipped, rejected page not re-proposed, un-tagging removes the seeded hash, cancellation).
- Migration tests for the settings migration and the two new-entity migrations.
- Headless tests assert state synchronously; never `await window.FadeOutAndCloseAsync()`. Timer behaviour goes through
  `internal void On…Tick` test seams. Check `Get-Process testhost` before each run.
- No FlaUI scripting without asking.

## Open risks

- Hash matching is heuristic: expect some false proposals. Proposals-only plus remembered rejections is the mitigation.
- A large library makes the first scan long. It is opt-in and runs as a background job, and later runs only touch
  changed files.
