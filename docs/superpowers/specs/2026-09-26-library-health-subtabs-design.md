# Library Health: sub-tabs, virtualized lists and a faster refresh

**Date:** 2026-09-26
**Status:** Approved in grilling, two rounds (all recommendations accepted); awaiting review of this write-up
**Builds on:** `2026-09-25-needs-review-into-library-health-design.md` (which moved the five review queues here)

## Problem

Library Health is now one long card with ten always-open subsections, and it lags. On a library the size of the
user's (about 3,650 issues, 1,814 auto-applied proposals, 34 duplicate groups) seeded probes measured:

**Refresh, on the UI thread, per visit to Preferences and after most bulk actions: 0.5-0.9 s.**

| Step | Time |
|---|---|
| Content Type | 430-1,060 ms (a smart-list scan across every issue, for a field that is only `Series.ContentType`) |
| Duplicate Files | 330-420 ms in the refresh; split: entity load with `Include(Series)` 460-1,270 ms, grouping 30-65 ms |
| Duplicate Files, slim projection load | load 54-190 ms, grouping 11-14 ms (about 70 ms total, same algorithm) |
| Metadata Proposals | 30-160 ms after the Applied grouping below (it used to load ~2,000 rows with three `Include`s) |
| Series Conflicts, Ad Pages | under 50 ms |

**Row building** (headless, Debug build, so relative numbers; real duplicate-group row shape, real theme):

| Rows | Plain `ItemsControl` (today) | `VirtualizingStackPanel` in a 480 px region |
|---|---|---|
| 50 | 1,145 ms (50 built) | 69 ms (4 built) |
| 200 | 2,732 ms | 89 ms |
| 1,000 | 14,896 ms | 241 ms |
| 3,000 | 22,256 ms | 256 ms |

About 13 ms per row in that build. A plain list inside the page's `ScrollViewer` is measured with infinite height, builds
every row, and never virtualizes (the Wanted screen's own comment says the same).

**`ItemsRepeater` is not an option:** the Avalonia docs state it is no longer supported as of Avalonia 12, and it does not
compile against 12.1.1. The virtualizing option left is `VirtualizingStackPanel`, which needs its own bounded-height
scroll region (the app already does this on the Wanted screen).

Already done before this spec (2026-09-26): Applied Metadata Proposals are bucketed by field + source from one `GROUP BY`
(`AppliedProposalGroups`), rows load on expand, and `MetadataProposal.ReviewedAt` makes Accept durable.

## Decisions

1. **Structure: three sub-tabs inside the existing card** (chosen from three mockups). The card stays in Preferences ->
   Library; only the open tab is populated.
   - **Overview** - the four stat tiles, Verify Now, Remove All Confirmed Missing, the "confirm missing after N checks"
     setting, and a "Needs attention" list: one line per queue with its count and a "Review ->" jump.
   - **Review** - Duplicate Files, Series Conflicts, Content Type, Metadata Proposals, Advertisement Pages, Reported pages,
     Find Similar Series.
   - **Files** - Missing Files, Empty Rows, Recently Removed.
2. **Sections are collapsed one-line rows** (icon, title, count, main bulk action, chevron). An empty queue is a "clear"
   line that cannot be opened. Bulk buttons always act on the whole queue, never just visible rows.
3. **Every list is virtualized** (replaces the earlier "first 50 rows + Show more" idea). A `VirtualList` control wraps
   `ScrollViewer` (`MaxHeight` about 420 px, so it sizes to its content when shorter) around an `ItemsControl` whose
   `ItemsPanel` is a `VirtualizingStackPanel`. Only the rows in view are built, a collapsed section builds nothing (a hidden
   virtualizing panel is never measured), and there is no "Show more". Scroll chaining stays on, so at the ends of an inner
   list the wheel continues into the page.
4. **Duplicate scan: keep the algorithm, slim the load.** `SmartListQueryBuilder.BuildDuplicateGroups` (CE-style union-find
   over effective values) is untouched. The Duplicate Files queue loads a projection of only the fields the grouping and the
   row need (`Id`, `SeriesId`, `Format`, `Count`, `Number`, `Volume`, `Year`, `LanguageISO`, `Month`, `Day`, `FilePath`,
   `FileSize`, `FileIsMissing`, `AddedTime`, `DuplicateAcknowledged`) with no tracking, and reads series names from one
   `Id -> Name` dictionary instead of `Include(Series)`. The current code does not `Include` proposals, so effective values
   there are already the raw values; the projection keeps that behaviour exactly.
5. **Content Type: direct query.** `SmartListCatalog` evaluates the field as `i.Series?.ContentType`, so it is the set of
   series where `ContentType == Unknown` and the series has at least one issue.
6. **Background refresh.** The whole refresh runs on a background thread when Preferences opens. The last result stays on
   screen while a new one is computed, and tab/section counts show "..." until the first result arrives. After an action,
   only the affected queue is refreshed.
7. **Default tab, remembered tab, deep links.** Precedence: a deep link, then the remembered tab, then (first-ever open) Review
   if anything is pending, else Overview. The last-used tab is saved (`AppSettings.LibraryHealthTab`). Deep links: the
   missing-files and startup path-repair alerts land on Files; the duplicate-files alert lands on Review with Duplicate
   Files open; the legacy `MigrationReview` link and the migration Results button land on Review.
8. **Search.** One Preferences search-index entry per queue (Duplicate files, Series conflicts, Content type, Metadata
   proposals, Advertisement pages, Reported pages, Similar series, Missing files, Empty rows, Recently removed), each with
   anchor `library.health/<tab>/<section>`. Opening a hit switches to the tab, opens the section and scrolls to it. The
   existing `library.health` entry and anchor keep working.
9. **Pending Metadata Proposals are grouped like Applied ones.** The Applied group view model is generalized into
   `ProposalGroupViewModel` (status: Pending or Applied): buckets by field + source (+ provider) with a count and
   per-bucket Accept All / Reject All, rows built on expand (latest 500, virtualized). The section-level Accept All /
   Reject All for Pending stay.
10. **Untouched:** the "Scanning" settings card, the folder cards, the Migrate card, the duplicate-grouping algorithm.

## Components

- `Controls/VirtualList` (new `UserControl`): `ItemsSource`, `ItemTemplate`, `MaxListHeight` (default 420) styled
  properties forwarded to an inner `ScrollViewer` + `ItemsControl` with a `VirtualizingStackPanel`. Every list in the card
  uses it. Keyboard focus and screen readers see only realized rows, so each row keeps its `AutomationProperties.Name`.
- `ViewModels/ProposalGroupViewModel` (replaces `AppliedProposalGroupViewModel`): as today plus `Status`, `RowLimit` = 500.
- `NeedsReviewViewModel`: `RefreshAsync()` snapshots each queue on a background thread on its own context into plain lists,
  then swaps the collections on the UI thread; a sequence number drops a result superseded by a newer refresh. `Refresh()`
  stays as the synchronous form for tests and simple callers. `Refresh(NeedsReviewQueue)` refreshes one queue. `IsRefreshing`
  drives the "..." counts. Content Type and Duplicate Files use the direct query / slim load.
- `PreferencesScreenViewModel.LibraryHealthTabs.cs` (new partial): `LibraryHealthTab` enum (Overview, Review, Files),
  `ActiveLibraryHealthTab` (persisted), `IsLibraryHealth{Overview,Review,Files}Tab`, `ShowLibraryHealthTabCommand`,
  `OpenLibraryHealth(tab, section?)`. Existing `GoLibraryHealth` becomes `OpenLibraryHealth(default)`. `OpenSearchResult`
  understands `library.health/<tab>/<section>` anchors.
- `Paperbunkr.Data`: `AppSettings.LibraryHealthTab` (string-stored enum, nullable = unset) + migration
  `AddLibraryHealthTab` with a **no-op `Down()`** (the AppSettings orphan-column rule).
- `ActivityLink` payloads: `LibraryHealth` (default), `LibraryHealth/Files`, `LibraryHealth/Review/Duplicates`.
  `MainViewModel.ResolveActivityLink` maps them to `OpenLibraryHealth`; `DuplicateAlertHelper` and the path-repair/missing
  alerts use the specific payloads.
- `Models/PreferenceIndex.cs`: the ten new entries.
- `Views/Preferences/LibrarySection.axaml`: the Library Health card becomes tab strip (reuse the file's `Button.segTab`
  style) + three panels + one shared section-row pattern (header row with toggle, count chip and bulk buttons; body is a
  `VirtualList`). Overview's "Needs attention" rows switch tab and open the section. Tab strip shows count chips (Review:
  pending total; Files: missing count or a check).

## Errors and edge cases

- A refresh that fails in the background keeps the previous result and logs; counts fall back to the last values.
- A bulk action's list rebuild stays deferred one dispatcher tick (CLAUDE.md "routed event" rule).
- Row-level Accept/Reject keep their in-place "Accepted"/"Rejected" feedback without a refresh, as today.
- A search hit or deep link to a section inside a hidden tab must switch the tab first and scroll after layout (post to the
  dispatcher), otherwise the anchor does not exist yet.
- Nested scrolling: wheel over an inner list scrolls it until its end, then the page (scroll chaining). Touch/trackpad
  behaves the same. Worth checking on screen.
- The projection load must produce the same duplicate groups as today's full load; a test pins that.
- One schema change: a nullable string on `AppSettings`.

## Testing

- `VirtualListTests` (headless, with the Fluent theme applied as the probe did): N = 1,000 rows realizes fewer than about
  10 rows; a short list sizes to its content; a hidden list realizes nothing.
- `NeedsReviewViewModelTests`: Content Type direct query returns the same series as the old smart-list path on a fixture
  (Unknown-type series with issues, Unknown-type series without issues, non-Unknown); the slim duplicate load returns the
  same groups, members and default-keep order as the full load; `RefreshAsync` result is applied, a superseded result is
  dropped, `IsRefreshing` toggles; per-queue refresh leaves the other queues untouched; Pending proposals are bucketed and
  per-bucket Accept All / Reject All behave like the Applied ones (including Series-field accepts that move an issue).
- `PreferencesScreenViewModelTests` / `MainViewModelTests`: precedence of deep link, remembered tab and first-open default;
  each alert payload lands on the right tab with the right section open; each new search entry resolves to its tab and
  section; the remembered tab round-trips through settings; `library.health` anchor still requested.
- `AddLibraryHealthTabMigrationTests` (Data): column exists, nullable, defaults to unset; all Data migration tests still pass.
- Performance (throwaway probes again, not committed). Targets: Content Type under 30 ms; duplicate scan under 100 ms;
  full refresh under 200 ms even on the UI thread and never blocking because it runs in the background; **UI-thread time
  to open Library Health under 50 ms**; at most about 10 realized rows per open list.
- Build with the Avalonia weave check, then on-screen: tab switching, section expand/collapse, inner-list scrolling and
  chaining, every deep link, a search hit into a hidden tab, the "..." to count transition, both themes; run
  `avalonia-pro-max/review-checklist` first.

## Out of scope

- `ItemsRepeater` (removed from Avalonia 12).
- Changing the duplicate-grouping algorithm (measured at 11-65 ms; the cost was the entity load).
- Making the duplicate scan honour accepted-proposal effective values (today's behaviour is the raw values; changing it is a
  separate, behaviour-visible decision).

## Implementation notes (2026-09-26)

Built as specified, with these deviations:

- **Search/deep-link anchors reuse the sections' own `Tag`s** (`library.healthDuplicates`, `library.healthSeriesConflicts`,
  `library.healthContentType`, `library.healthProposals`, `library.healthAdPages`, `library.healthReportedPages`,
  `library.healthSimilarSeries`, `library.healthMissing`, `library.healthEmptyRows`, `library.healthRecentlyRemoved`) instead of a
  `library.health/<tab>/<section>` scheme: `PreferenceIndexTests` already requires every anchor to be a `Border.Tag`, and each
  section is now a tagged `Border`, so the tab and section come from `LibraryHealthSections.Find(anchor)`. `library.health` still
  targets the card.
- **`VirtualList` is a code-only control** (the project's `Controls/` are all code-only), `MaxListHeight` default 420.
- **`ProposalGroupViewModel`** replaces `AppliedProposalGroupViewModel`; a Pending group's Accept All is two-step
  (`AcceptAllConfirm`), an Applied group's is a plain click (it only marks reviewed). Group rows load the latest 500.
- **`NeedsReviewViewModel.Queue`** is a nested enum (`Refresh(Queue)`), not a top-level `NeedsReviewQueue`.
- **Added beyond the spec:** count labels that read "…" until the first refresh finishes (`PendingCountLabel`,
  `DuplicateCountLabel`, ...), `HasAnyProposalItems` / `ProposalSummaryLabel`, `EmptyRowCount`, and a fix so a synchronous
  `Refresh()` that supersedes an in-flight `RefreshAsync()` clears `IsRefreshing`.
- The Files tab's header chip shows the missing-file count; the Review chip shows the pending total.
- Groups of proposals use a plain `ItemsControl` (a handful of buckets); each bucket's rows use a `VirtualList`. The Overview's
  "Needs attention" rows cover Duplicates, Series Conflicts, Content Type, Metadata Proposals (pending), Ad Pages and Reported pages.

**Measured after the build** (same seeded 3,650-issue / 1,814-applied-proposal library, headless, Debug):

| | Before | After |
|---|---|---|
| Content Type | 430-1,060 ms | about 1.5 ms warm (365 ms cold, one-time startup) |
| Duplicate scan | 330-420 ms | 31-135 ms |
| Full synchronous refresh | 500-880 ms | 53-70 ms |
| UI-thread time for `RefreshAsync` | the whole refresh | 0.1-3.6 ms |
| Rows built for a 1,000-row list | 1,000 (about 15 s) | about 7-25 (`VirtualListTests`) |

Not verified: nothing was viewed on screen (tab switching, section expand/collapse, inner-list scrolling and scroll chaining, every
deep link, a search hit into a hidden tab, both themes). The whole `Paperbunkr.App.Tests` suite was not run; the affected classes
were.
