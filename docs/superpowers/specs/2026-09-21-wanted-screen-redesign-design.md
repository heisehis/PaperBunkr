# Wanted screen redesign — Design

Redesign of the Wanted screen (`WantedScreen.axaml` / `WantedScreenViewModel`), built in
[2026-09-19-comic-acquisition-daemon-design.md](2026-09-19-comic-acquisition-daemon-design.md) §7 and extended by
[2026-09-20-weekly-pull-list-design.md](2026-09-20-weekly-pull-list-design.md). Not a ComicRack CE feature, so the CE-parity rule does not apply.
Approved 2026-09-21 after a grilling pass and three visual-companion rounds (layout A, Releases cover shelf, week navigator + calendar popup).

## 1. Problems being fixed

- Cards were left-aligned and sized to content, so rows had ragged widths and the right of the window was empty.
- The Wanted tab stacked Downloads, scrape-review and the wanted list in three nested scrollers; one row per issue repeated the series name.
- Downloads showed only "Downloading 0%", although speed and ETA were already published by the daemon (`DownloadProgressEvent`).
- A stale inline status line ("Cancelled.") persisted across tabs.
- Five tabs mixed pipeline stages (Wanted, Upcoming, Candidates) with management screens (Series, Releases).

## 2. Decisions

| # | Decision |
|---|---|
| Structure | Three tabs: **Queue**, **Series**, **Releases**. `WantedScreen` is a thin shell (title, tabs, Search now, banner); `QueueView`, `SeriesView`, `ReleasesView` are separate user controls. |
| Queue | One full-width virtualized flat list of series **group headers**, **issue rows**, and (when expanded) **candidate rows**. Stage filter chips with live counts replace the Wanted/Upcoming/Candidates tabs: All, Wanted, Upcoming, Has candidates, Downloading, Failed, Needs details. |
| Downloads | A slim strip above the queue: cover, title, progress, `38% · 2.1 MB/s · 4m`, Cancel. Failed downloads flag inline in their issue row with Retry. |
| Candidates | Inline "n candidates" expander on the issue row, best first, each with Grab / Copy link / Reject. No "Grab best" (auto-grab stays in the daemon backlog). |
| Groups | Collapsed by default except groups that need the user (failed, candidates, downloading), and every group while a chip filter is active. User expand/collapse choices persist for the session. Sort: Attention (default) / A–Z / Recently added. |
| Bulk | Group-header buttons only, confirmed via `ConfirmDialog`: **I have all**, **Remove all** (over the issues currently listed, never ones downloading). "Request all" from the mockup is dropped: everything in the Queue is already requested. |
| Needs details | A chip in the Queue; Retry all / Dismiss all appear while it is active. Library Health remains a possible later home. |
| Messages | The inline status line is removed. Results go through the toast host (`ShowToast`). One dismissible banner covers "automatic searching is off". |
| Live updates | Refresh reconciles the flat list in place (keyed by group/issue/candidate id) so stage changes never reload the list, jump the scroll or collapse a group. `DownloadProgressEvent` speed/ETA is applied straight onto the row. |
| Series | Full-width list (cover, name, publisher/year, source chip, "n missing · n wanted", Follow, Open). "Track a series" is a header button opening a search flyout. New **Untrack** in a row menu, confirmed; it deletes the watched series with its wants and catalog (cascade). |
| Releases | Cover-tile shelf, **one week per page**, grouped by day. Header: ‹ › Today, a date label opening a month-calendar popup (dots: releases / followed), publisher, Followed-only, Show hidden, freshness note. Tiles: state badge on the cover, one always-visible primary button (Request, or Follow when not followed), Hide/Restore in a "…" menu that doubles as the right-click menu. |
| Release data range | The background refresh window is unchanged (7 days back, 28 ahead). Picking a date outside the cached range fetches just that week on demand (foreground priority) and stores it; it lasts until the next scheduled refresh replaces the cache. |

## 3. Architecture

- `WantedScreenViewModel` stays one class, split into partials: `.cs` (shell, data load, shared commands), `.Queue.cs`, `.Series.cs`, `.Releases.cs`.
  The per-tab row collections (`WantedRows`, `UpcomingRows`, `DownloadRows`, `CandidateGroups`) are replaced by `QueueItems` (flat, reconciled) and `ActiveDownloads`.
- `QueueItemViewModel` (observable base) with `QueueGroupViewModel`, `QueueIssueViewModel`, `QueueCandidateViewModel` (wraps the existing `CandidateRowViewModel`) and `ScrapeReviewRowViewModel`.
- New non-UI pieces: `PullListService.FetchRangeAsync` + range-scoped `Store`, and `PullListSourceFactory` (the Metron-else-ComicVine choice `AcquisitionCycle` makes inline, exposed for the foreground fetch).
- `AcquisitionActivityBridge` gains an optional `onDownloadProgress` callback.
- No migrations.

## 4. Cross-cutting

- Colors from `Pb*` tokens only. Row buttons that remove or restructure the list they live in defer through `_post` (`Dispatcher.UIThread.Post`).
- Existing AutomationIds are kept; new controls get ids. Actions have accessible names.
- Avalonia limits respected: no native sticky group headers; no virtualizing wrap panel, so tiles use a wrap panel per day with the existing off-UI-thread cover decoding (verified with a 150-tile week).
- A month calendar with dots is a small custom day grid (Avalonia's `Calendar` cannot decorate days).

## 5. Testing

Headless view-model tests (grouping, sorting, chip counts, expand/collapse persistence, in-place reconcile identity, bulk actions, untrack, week paging, on-demand fetch), a `PullListService` range test, the compiled-view construction tests, and a virtualization test over 3000 queue issues. On-screen verification is the user's.
