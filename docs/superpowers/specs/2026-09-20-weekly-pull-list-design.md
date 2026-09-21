# Weekly pull list — Design

*Date: 2026-09-20. Status: implemented on `feat/weekly-pull-list` (see the status section). Grilling round 1 answered "go with recommendations".*

## 1. Problem

Upcoming stays empty. Mylar builds its list from a weekly pull list, so future issues arrive on their own. Paperbunkr only knew a future issue once ComicVine or Metron already listed it under a series the user followed, and ComicVine lists them late.

## 2. Decisions

| # | Decision |
|---|----------|
| Q1 | Source is **Metron** (store-date query across all publishers), or **ComicVine** when no Metron login is saved (added later, see the follow-ups). The list comes from one source at a time. |
| Q2 | A new **Releases** tab on the Wanted screen, grouped by week, each row with **Request** and **Follow**. Releases of followed series also flow into Upcoming on their own. |
| Q3 | A release is matched to a followed series by Metron's series id; a ComicVine-tracked series through Metron's `cv_id`. |
| Q4 | Window is last week to four weeks ahead, refetched by the daemon about twice a day (manual "Search now": at most hourly). |
| Q5 | Filters: publisher, and "followed only". |
| Q6 | Cached in the database (`PullListRelease`), refreshed by the daemon. |
| Q7 | A followed series' upcoming release becomes a want (Upcoming until its store date); nothing changes on the download side. |

## 3. Verified Metron facts (read from `github.com/Metron-Project/metron`, 2026-09-20)

- The issue endpoint filters by `store_date_range_after` / `store_date_range_before` (also `foc_date_range`, `publisher_name`, `publisher_id`). There is no weekly or upcoming endpoint; a week is a date-range query. Page size 100.
- An issue *list* item carries only `id`, `series {id, name, volume, year_began}`, `number`, `issue`, `cover_date`, `store_date`, `image`. **No publisher and no `cv_id`.** Both come from the series (`series/{id}/`), which is why series info is cached separately (`MetronSeriesInfo`).

## 4. Design

- `IPullListSource` (implemented by `MetronClient`): `GetReleasesAsync(from, to)` and `GetSeriesInfoAsync(id)`.
- `PullListRelease` (the cached list) and `MetronSeriesInfo` (publisher, year, ComicVine id by Metron series id), plus `AcquisitionSettings.PullListRefreshedAt`. One additive migration, `AddWeeklyPullList`.
- `PullListService`: `RefreshAsync` fetches, upserts, drops rows the source no longer returns, then looks up unknown series (followed series' names first, capped at 150 per refresh, stopping on a rate limit; each series is looked up once, ever). `PromoteFollowedReleases` requests releases dated today or later for followed, unpaused series; last week's releases are shown but never auto-requested.
- `WantedService.RequestFromRelease` creates the want with Metron's ids (so the import-time details lookup asks Metron even for a ComicVine-tracked series) unless the library owns that number or the series already has a want for it. `GetMissing` now also treats a number wanted from the other provider as taken, so the ComicVine catalog can't request the same issue again once it lists it.
- `AcquisitionCycle` refreshes the list before promoting, only with a saved Metron login; a rejected login raises the existing Metron alert and never stops the cycle.
- UI: `Releases` tab (`WantedScreenViewModel.Releases.cs`): week groups, publisher suggest box, "Followed only" toggle, status chips (Wanted / Following), and an empty state that says why (no Metron login vs. nothing fetched vs. filtered out). Request tracks the series on Metron (not following) and requests just that issue; Follow tracks, follows and requests everything upcoming.

## 5. Out of scope

A per-publisher default filter; cover-date (as opposed to store-date) listing; auto-following series; using `modified_gt` to fetch only changed series on later refreshes (Metron supports it; the series cache already makes refreshes cheap).

## 6. Status

| Step | State |
|------|-------|
| Metron client: releases by store date, series info | Done, fixture-tested |
| Cache tables, migration, `PullListService`, `RequestFromRelease`, cross-provider dedupe | Done |
| Daemon cycle hook (twice-daily refresh, alert handling) | Done |
| Releases tab (filters, Request, Follow) and empty states | Done |

Verified: Data, Daemon and Wanted view-model tests for all of it. Not yet verified: a live fetch against Metron (fixtures only), the Releases tab on screen, and how long a first refresh takes on a real account (up to about 150 series lookups at the background rate of 14 requests a minute, so roughly ten minutes, then cached).

### Follow-ups (2026-09-20)

- **Hide a release.** `PullListRelease.IsHidden` (migration `AddPullListReleaseHidden`): a hidden release leaves the tab ("Show hidden" brings it back with a Restore button), is never turned into a want, and keeps the choice through refreshes.
- **New-release notice.** When the daemon makes wants from the list it publishes one `NewReleasesEvent` per batch; the Activity Center shows an info notice ("6 new releases from series you follow", naming a few) linking to Wanted. Each batch is its own notice, since the alert's dedupe key would otherwise keep a stale count.
- **Wording.** Text that said "ComicVine" where a want may be Metron's (import-details setting, the needs-attention list, the failure notice, the Detail panel's intro) is now source-neutral.

### Gap closers (2026-09-21)

- **Arcs.** Reading-list arc requests (`ArcRequestService`, the daily arc follow, the reading screen) follow a series' own source: one already tracked on Metron is read through Metron, and works without a ComicVine key. A series not tracked yet is still matched on ComicVine, and says so when there is no key.
- **Metron daily quota.** `MetronQuota` reads `X-RateLimit-Sustained-Limit/-Remaining/-Reset` from every response (verified against Metron's own `api/RATELIMIT.md`: the reset is a Unix timestamp, and the limit varies per account, so nothing hardcodes 5,000). Background (low priority) requests stop with the usual "rate limited" error once the remaining count is at or under 10% of the limit (never under 50), so interactive requests are never the ones that find the day used up.
- **Recorded source.** `Issue.MetadataSource` (migration `AddIssueMetadataSource`) is set whenever a scrape or an import-time lookup applies details. A re-scrape starts on the source most of the chosen comics came from (ties and never-scraped go to the default), because `Issue.Volume` is only an id.
- **Covers.** A catalog refresh gives a series with no cover of its own (every Metron series) the cover of its earliest issue, the Wanted Series tab shows it, and Metron search results borrow the newest cached weekly-list cover when the series is in the window (no request is spent looking).
- **ComicVine weekly list.** `ComicVineClient` implements `IPullListSource` (`issues?filter=store_date:from|to`, 100 a page). The daemon uses Metron when its login is saved, else ComicVine. `PullListRelease.Provider` and `ReleaseSeries` (was `MetronSeries`, now keyed by provider and series id) hold either; switching source replaces the list. ComicVine series lookups (for publisher names) are capped at 40 per refresh, since its whole budget is 200 an hour. A followed ComicVine series matches by volume id; a Metron-tracked series is not matched by a ComicVine list (Metron's cross-reference is only cached from a Metron list). Migration `AddReleaseListProvider` carries the existing cache over as Metron's; its test caught that a model default of Metron (1) would have turned every ComicVine (0) row into Metron's, so the model has no default and only the migration's column default is 1.
