# Metron as an alternative to ComicVine — Design and Plan

*Date: 2026-09-20. Status: implemented on `feat/metron-provider` (see the status table at the end). Grilling round 1 answered "go with recommendations".*

## 1. Decisions

| # | Decision |
|---|----------|
| Q1 | **Per series, not a global switch.** Each tracked series (`WatchedSeries`) remembers its `Provider`; nothing is merged between providers. |
| Q2 | Everything, in two steps: (1) series matching, the missing-issue catalog, Wanted and acquisition; (2) scraping and scrape-on-import. |
| Q3 | `ComicProvider` (`ComicVine`, `Metron`) column on `WatchedSeries`, `CatalogIssue`, `WantedIssue` and `ComicVineMatchMemoryEntry`; the ComicVine-named id columns are renamed `ExternalVolumeId` / `ExternalIssueId`; unique indexes become `(Provider, Id)`. One migration. Existing rows are ComicVine. |
| Q4 | `IComicProvider` (a `Kind` plus the existing search / volume / issue-list / issue-details / paged-search contracts) with `ComicVineClient` and a new `MetronClient` behind it. |
| Q5 | The panel's "ComicVine" label becomes a source selector. Default ComicVine, no automatic double search. |
| Q6 | A default-provider setting under Preferences → Organize & Scrape, plus a switch inside the match dialog for one run. Scheduled scrapes use the default only. |
| Q7 | A small "Metron" chip on Metron-tracked series and wants; nothing for ComicVine. |

## 2. Metron API facts (read from `github.com/Metron-Project/metron`, 2026-09-20)

- Base `https://metron.cloud/api/`, HTTP Basic auth (the login is already stored in Connections, `CredentialStore` "Metron").
- **Rate limits (authenticated user default): 20 requests/minute (burst) and 5,000/day (sustained)**; supporters get more daily. Responses carry `X-RateLimit-Burst-*` and `X-RateLimit-Sustained-*` (`Limit`, `Remaining`, `Reset`) headers. Page size is 100 (`PageNumberPagination`; `next` URL).
- `GET series/?name=…` (multi-word), also `q=` quick search (name + alt names), `publisher_name`, `year_began`, `cv_id`. List item: `id`, `series` (display string that includes the year), `year_began`, `year_end`, `volume`, `issue_count`, `publisher {id,name}`, `series_type`, `cv_id`, `gcd_id`, `modified`. **No cover image on a series.**
- `GET series/{id}/` detail (name, issue_count, …); `GET series/{id}/issue_list/` (paginated issues); `GET issue/?series_id=&number=`.
- Issue list item: `id`, `series {id,name,volume,year_began}`, `number`, `issue`, `cover_date`, `store_date`, `image`, `modified`. Issue detail adds `title`, `name` (story titles), `desc`, `arcs`, `credits [{id, creator, role [{id,name}]}]`, `characters`, `teams`, `universes`, `cv_id`.
- Every Metron series and issue can carry the ComicVine id (`cv_id`), which allows cross-linking later.

## 3. Design

- `MetronClient` implements the same contracts as `ComicVineClient`, mapping onto the existing neutral records (`ComicVineVolume`, `ComicVineIssue`, `ComicVineIssueDetails`), so every consumer that takes those contracts works with either provider.
- `MetronRateLimitHandler`: a `DelegatingHandler` (like ComicVine's) enforcing the burst window (20/min, held to 18 to leave headroom), a daily counter (5,000, held to 4,500) and a pause when a `429` arrives (`Retry-After`/`X-RateLimit-*-Reset`). `High` requests are served before `Low` ones, as for ComicVine.
- Roles map onto Paperbunkr credit fields the way the ComicVine role map does (Writer, Penciller, Inker, Colorist, Letterer, Cover, Editor).
- `ProviderFactory` builds a provider by kind from the saved credentials (ComicVine key, Metron login) at a given priority, and reports why one is unavailable.
- Acquisition: a series is refreshed and searched through its own provider; upcoming/store dates come from that provider. Scrape-on-import fetches issue details from the want's provider.
- Match memory is keyed by provider as well as name, since ids from two providers can collide.

## 4. Out of scope

Automatic cross-provider merging; using `cv_id` to translate a tracked ComicVine series to Metron (possible later); Metron series covers (Metron has none).

## 5. Status

Recorded at the end of the implementation session.
