# Weekly pull list — Design

*Date: 2026-09-20. Status: implemented on `feat/weekly-pull-list` (see the status section). Grilling round 1 answered "go with recommendations".*

## 1. Problem

Upcoming stays empty. Mylar builds its list from a weekly pull list, so future issues arrive on their own. Paperbunkr only knew a future issue once ComicVine or Metron already listed it under a series the user followed, and ComicVine lists them late.

## 2. Decisions

| # | Decision |
|---|----------|
| Q1 | Source is **Metron** (store-date query across all publishers). Without a Metron login there is no weekly list; ComicVine is not used for it (its issue filter can't page a whole week reliably), so this is a deviation from the "ComicVine fallback" first proposed. |
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

A ComicVine-sourced weekly list; a per-publisher default filter; cover-date (as opposed to store-date) listing; auto-following series; a daily request counter for Metron (its 5,000 a day is far above a personal library's use and the 429 cool-off covers a burst); recording which source scraped an issue (nothing would read it yet); Metron series covers (Metron has none).

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
