# Manga Detail Screen — Design

*Stage 6 of `docs/tracker-manga-ui-research.md` (Mihon/Komikku tracker-and-UI research memo).
Stages 1–4 of that memo (Track data model, tracker service abstraction, sync engine, token
security) already shipped inline in `DetailTabsViewModel`'s Details tab. This spec covers only
the manga-specific detail-page presentation.*

## Problem

Paperbunkr's `DetailScreen` is a single Western-comic-style layout (cover + credits pills + a
4-tab strip: Issues/Related/Details/Activity) used for every `ContentType` value — Comic, Manga,
Manhua, Manhwa, Unknown. Manga/manhwa/manhua readers expect a denser, chapter-list-first
presentation (per the Mihon/Komikku research): blurred-cover header, expandable synopsis,
outlined-pill tag chips, icon-led metadata rows, an at-a-glance tracker indicator, and a dominant
chapter list with read-state dimming — distinct from cover-tile browsing, because unlike Western
comic issues, manga chapters don't have variant covers, so a cover-tile grid adds no information
a list row doesn't already carry.

## Screens & routing

- New `MangaDetailScreenViewModel` (`src/Paperbunkr.App/ViewModels/`) and
  `MangaDetailScreen.axaml` (`src/Paperbunkr.App/Views/`), parallel to the existing
  `DetailScreenViewModel`/`DetailScreen.axaml`.
- `MainViewModel` constructs `MangaDetail = new MangaDetailScreenViewModel(...)` eagerly as a
  field, the same way `Detail` is constructed today (`MainViewModel.cs:24`).
- `GoDetailForSeries(seriesId)` (`MainViewModel.cs:358`) becomes the routing choke point: it
  already loads the series before flipping `CurrentScreen`. It gains a branch on
  `series.ContentType`:
  - `Manga` / `Manhua` / `Manhwa` → `MangaDetail.LoadSeries(seriesId)`,
    `CurrentScreen = "mangaDetail"`.
  - `Comic` / `Unknown` → existing `Detail.LoadSeries(seriesId)`, `CurrentScreen = "detail"`
    path, unchanged.
- `GoDetailAfterIssueEdit()` (`MainViewModel.cs:352`) gets the same content-type branch instead of
  unconditionally reloading `Detail`.
- A new `IsMangaDetail => CurrentScreen == "mangaDetail"` property drives view visibility,
  alongside the existing `IsDetail`.
- Both screens keep an editable `ContentType` picker in their header (porting
  `DetailScreenViewModel`'s existing `SelectedContentType` pill). Changing it re-invokes
  `GoDetailForSeries(seriesId)`, so switching a series between the Comic and manga families
  live-routes to the correct screen — there's no dead end from picking the "wrong" screen.
- `MangaDetailScreenViewModel` embeds an instance of the existing `DetailTabsViewModel` for the
  Related / Details (tracker linking + external metadata) / Activity tabs — **not duplicated**.
  Only the header and the chapter list (replacing the Issues tab) are new code. This keeps the
  tracker-linking, external-metadata, and continuity logic that already works in one place.

## Header

New header section in `MangaDetailScreen.axaml`, replacing `DetailMeta`/`DetailPills` for this
screen only:

- **Backdrop.** The series cover bitmap rendered once through Avalonia's `BlurEffect` plus a dark
  scrim, cached per series alongside the existing `CoverImageCache` (not a live/animated blur —
  computed once when the view loads). The sharp cover thumbnail sits on top, unblurred.
- **Title + expandable synopsis.** `Series.Summary`, truncated to a few lines with a chevron
  toggle (`IsSynopsisExpanded` bool on the VM).
- **Icon-led metadata rows**, replacing plain label:value text: a status icon next to the
  `SeriesStatus` (Ongoing/Completed/Cancelled/Hiatus/Unknown), a type icon next to the
  `ContentType` display name, a source icon next to aggregated `Issue.ScanInformation` (already a
  real ComicInfo-backed field; "Unknown" when empty/mixed across issues).
- **Tag chips** in an outlined-pill style (thin colored border, transparent fill, rounded-full) —
  a new chip style distinct from the existing filled `PbSurfaceAltBrush` pills used elsewhere in
  `DetailTabs.axaml`. Same Genre/Tags aggregation source as today's `DetailPills`.
- **Reading-mode badge.** A small icon + label next to the header showing RTL / vertical /
  webtoon, sourced from `Series.ReadingMode`.
- **Action row.** A wrap-capable `ItemsControl` of icon-over-label buttons — deliberately not a
  fixed grid, so more buttons can be added later without a relayout. Initial buttons:
  - **Continue Reading** — ports `DetailScreenViewModel.Continue()`'s existing pick logic
    (in-progress issue first, else next unread, else lowest-numbered as a re-read fallback) and
    opens the reader at that issue.
  - **Edit** — opens the existing Issue Properties editor.
  - **Tracker status** — shows "N trackers" / "Not tracked"; opens the tracker section that
    already lives in the embedded `DetailTabsViewModel`'s Details tab.

## Chapters tab

Replaces the tile grid for this screen, becomes the first tab in the strip:

- New list-row control (not the existing tile `WrapPanel`): each row shows chapter number + title,
  dimmed styling when fully read (`LastPageRead >= PageCount`), a thin in-progress indicator when
  partially read, a bookmark icon when `Issue.Bookmarks.Count > 0`, a missing-file icon (reusing
  the existing `MissingFileRowViewModel` concept) in place of Mihon's "download" icon — Paperbunkr
  has no download concept since files are local — `ScanInformation` text, and file date.
- **Row click opens the reader directly at that issue.** Reuses the `OpenIssue`/
  `Action<int> goReaderForIssue` pattern already used in `IssueListScreenViewModel.cs:239-245`.
- Lightweight sort (chapter number / date) and filter (unread / bookmarked / missing) controls in
  the tab header. Kept as session-only view-model state, not wired into the persisted
  `IssueListSortField`/`IssueListGroupField` layout system Library/IssueList screens use — scoped
  down deliberately, since this list is a single series' chapters, not a cross-library view.
- Tab strip becomes: **Chapters** (new) | Related | Details | Activity — the last three rendered
  from the embedded `DetailTabsViewModel`. The exact mechanism for suppressing
  `DetailTabsViewModel`'s own Issues tab and stitching its remaining three tabs into one visual
  strip alongside the new Chapters tab (most likely a `ShowIssuesTab` bool flag on
  `DetailTabsViewModel`, defaulted `true`, set `false` when embedded in `MangaDetailScreen`) is an
  implementation detail left to the planning pass.

## Explicitly deferred (from the research doc, not part of this pass)

- Floating "Resume" pill button pinned over the chapter list while scrolling — the header's
  Continue Reading button already covers this; scroll-tracking overlay logic is deferred.
- Suggestions/recommendations row (horizontal-scroll "more like this" covers) — a Komikku-specific
  extra, not core to the Mihon pattern.
- Any UI for "Add to library" / update-frequency / WebView actions — none of these map to
  Paperbunkr's local-file model (everything scanned is already in the library; there's no source
  site to browse).

## Testing

- Unit tests for `MangaDetailScreenViewModel`'s Continue-pick logic (ported from
  `DetailScreenViewModel`), chapter dimming/in-progress computation, and sort/filter state,
  following the existing `*ScreenViewModelTests.cs` pattern.
- Routing test: `GoDetailForSeries` picks `Detail` vs `MangaDetail` correctly for each
  `ContentType` value, and re-routes correctly when `ContentType` changes mid-session.
- On-screen verification (per this project's standing UI-testing practice) that the blurred
  backdrop renders, chapter rows open the reader at the right issue, and the embedded
  `DetailTabsViewModel` tabs (tracker linking, external metadata, continuity) still work
  identically inside the new screen.
