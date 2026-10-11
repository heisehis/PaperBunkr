# Metron: change sweep, exact id lookups, cover-hash matching — design

**Status:** built 2026-10-05 as designed, uncommitted. Never run against the real Metron API; results are in
`docs/paperbunkr-todo.md` under the same date.
**Origin:** the same Metron-Project research as [2026-10-05-metroninfo-write-back-design.md](2026-10-05-metroninfo-write-back-design.md).
Account sync (pull list, collection, scrobble, wish list, reading lists) is its own spec:
`2026-10-05-metron-account-sync-design.md`.

## Facts this design is grounded on

- **Where requests go.** `AcquisitionCycle.RefreshFollowedVolumesAsync` fetches `series/{id}/issue_list/` for every followed,
  unpaused series whose `LastRefreshedAt` is older than 12 h, up to 15 a cycle, one request (or more) per series.
- **`modified_gt`** is a filter on every Metron list endpoint, and the API README states a parent's `modified` is bumped
  whenever its issues are added, edited or removed. `series/?modified_gt=T` therefore answers "which series changed since
  T" for the whole database in one paged request.
- **`If-Modified-Since`** is supported on detail and issue-list endpoints, but a series under 100 issues is one request
  either way, and the docs don't say a 304 is free against the rate limit. Not used.
- **Exact lookups.** `issue/?cv_id=N` and `issue/?upc=CODE` are exact-match filters; `upc_starts_with` exists for 12-digit
  scans. The list row carries `id`, `series{id,name,volume,year_began}`, `number`, `image`, `cover_hash`.
- **`cover_hash`** is `imagehash.phash` (Python): greyscale, 32×32 Lanczos, 2-D DCT-II, top-left 8×8, one bit per
  coefficient above the median, 16 hex digits. Metron-Tagger accepts a match at Hamming distance ≤ 10
  (`HAMMING_DISTANCE = 10`, `talker.py`). It is on issue list rows, so it costs no extra request.
- **Our cover check today** (`ScrapeOrchestrator.PassesCoverHashGateAsync`) is CE's: `CoenM.ImageHash.AverageHash`,
  similarity > 0.87, comparing the local cover to a *downloaded* candidate image. That library's own `PerceptualHash`
  resizes to 64×64, so its values are not Metron's; a faithful port is needed. A Metron series has no cover image, so for
  Metron the check rests entirely on the matched issue's image.
- **Metron-Tagger's order** (`talker.py`): an id already in the file → `cv_id` lookup (accepted only on exactly one
  result) → name search → cover hash to confirm one result or pick among several.
- **A scrape with Metron today** always searches by series name; it never uses the Comic Vine issue id or UPC the issue
  already has (`ComicMetadataExternalId` kind `Issue`, `Issue.Upc`).
- CE has no Metron support at all, so nothing here has a CE counterpart to match; the Comic Vine paths are not touched.

## Decisions

| # | Decision |
|---|---|
| D1 | Background refresh uses one `series/?modified_gt=` sweep per cycle and fetches only the followed Metron series it names (plus any never fetched). `If-Modified-Since` is not built. Comic Vine is unchanged. |
| D2 | Every series still gets a real fetch at least every 7 days, sweep or not. |
| D3 | For a Metron match, the cover check compares our cover's pHash to the issue row's `cover_hash` (≤ 10 bits), with no image download. Comic Vine keeps CE's check exactly. |
| D4 | The hash also breaks ties: when the top candidate fails the check, the next two are tried, and a candidate is taken only if it is the one that matches. A top candidate that passes is never second-guessed. |
| D5 | Before the name search, a Metron scrape tries exact ids in order: the issue's Metron id, its Comic Vine id (`cv_id`), its UPC. Accepted only on exactly one result; otherwise the normal search runs. |

## Design

### 1. Change sweep (D1, D2)

- `MetronClient.GetSeriesModifiedSinceAsync(DateTime sinceUtc)` → `IReadOnlyDictionary<int, DateTime>?` (series id → `modified`),
  paging `series/?modified_gt=`; **null** when the page cap is hit, meaning "too much changed to tell".
  Exposed through a small `ISeriesChangeSource` interface so the cycle doesn't know about Metron.
- `WatchedSeries.LastCatalogFetchAt` (new, nullable): when the issue list was last really fetched. `LastRefreshedAt` keeps its
  meaning (catalog known current as of). `WantedService.RefreshCatalog` sets both.
- In the cycle, for the due series of a provider whose client is an `ISeriesChangeSource`: those with a fetch inside 7 days
  are *sweep candidates*. With two or more candidates, one sweep runs from the oldest candidate's `LastRefreshedAt` (less a
  5-minute margin for clock skew). A candidate that is absent from the result, or whose `modified` is not after its own
  `LastRefreshedAt`, just gets `LastRefreshedAt = now`. Everything else is fetched as today.
- A failed or capped sweep falls back to fetching everything, exactly as before.

### 2. Exact id lookups (D5)

- `MetronClient.FindIssuesByComicVineIdAsync(int)` / `FindIssuesByUpcAsync(string)` → `IReadOnlyList<ComicIssueHit>` (issue id,
  series id). Surfaced to the scraper as an optional `IScrapeIssueLookup` on the `IScrapeComicVine` adapter.
- `ScrapeOrchestrator`, per book, before any search, when the provider is Metron and the run is choosing automatically
  (auto-choose on, or unattended). A run where the user confirms matches still shows its dialogs: that is how a wrong
  link gets corrected.
  1. A Metron issue link the book already has (including one imported from a `MetronInfo.xml`).
  2. A Comic Vine issue link → `cv_id`.
  3. `Issue.Upc` → `upc`.
  The details are fetched by id, the series from the details' series id, and both are applied through the same
  `ApplyVolumeAndIssueAsync` every other match uses. A lookup error or a count other than one falls through to the search.

### 3. Cover hash (D3, D4)

- `MetronCoverHash` (Data/ComicVine/Scraping): a port of `imagehash.phash` on ImageSharp; `FromFile`, `FromBytes`, `Parse(hex)`,
  `Distance`. Verified against the hash Metron's own docs publish for a public cover image.
- `cover_hash` rides on `ComicVineIssue` and `ComicVineIssueSummary` as an optional trailing member (null for Comic Vine).
- In the gate, when the matched issue carries a hash: pass iff the local cover hashes and the distance is ≤ 10. No hash →
  CE's download-and-compare, unchanged.
- Tie-break: on a failed gate with Metron, candidates 2 and 3 are checked the same way (one issue-list request each); if
  exactly one passes it becomes the choice.

### Out of scope

`If-Modified-Since`; `upc_starts_with`; a barcode-scanning UI; changing the Comic Vine matcher; Bearer-token login.

## Testing

- `MetronClientTests`: the sweep (paging, the cap → null, the `modified_gt` value sent), both lookups, `cover_hash` on issue rows.
- `MetronCoverHashTests`: hex parse/round-trip, distance, a synthetic image is stable under resize; **one opt-in live check**
  against the documented public image (skipped without network).
- `AcquisitionCycleTests`: unchanged series are not fetched, changed and never-fetched ones are, the 7-day rule, a failed
  sweep fetches everything, Comic Vine untouched.
- Scrape orchestrator tests: each id step, "more than one result" and "lookup failed" fall through, the confirm-dialog run
  still shows dialogs, the hash gate passes/fails without a download, the tie-break picks the single match.
- Migration `AddWatchedSeriesCatalogFetch` in the Data suite's round-trips.
- Not checkable here: real quota saved, and match quality on a real library.
