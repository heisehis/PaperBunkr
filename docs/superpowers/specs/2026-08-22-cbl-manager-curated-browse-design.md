# CBL Manager — Curated Browse — Design Spec

*Date: 2026-08-22. Scope: a same-day follow-up to the CBL Manager arc-lookup pass (docs/
superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md) - after live-verifying all six
sources against real requests, the user asked for a way to browse each source's catalog without
already knowing a title to search for, plus visibility into how large each source's catalog
actually is.*

**Verification behind this:** live-tested all four non-credentialed sources' real endpoints via
`curl` before designing anything (not assumed from the earlier port). ComicArc's sitemap currently
lists 3 reading orders; ReadingOrders.com's homepage payload lists 6 events; ReadThingsRight's
`hubDicts.js` lists 41 comic entries, and a real per-arc page (Astro City) still parses correctly
end-to-end (35 issues including its half-issue). Comic Book Reading Orders was confirmed working
live by the user directly. All four adapters' existing `SearchAsync` already filters via
`Name.Contains(query, OrdinalIgnoreCase)` - an **empty** query therefore already returns the entire
cached catalog for free, no new adapter method needed.

## 1. Design

Added `IReadingListSource.HasBrowsableCatalog` - true for the four scraped Tier-2 sources (a real,
small, fixed list `SearchAsync("")` can enumerate), false for ComicVine/Metron (open-ended API
search against a live database, no fixed catalog to browse). This line falls exactly along the
existing `RequiresCredentials` split in this codebase's six sources today, but is a deliberately
separate property - the two are correlated by coincidence of which six sources exist, not by any
actual relationship, and conflating them would misfire the day either a credentialed *or* a
scraped-but-unbounded source is added.

`ReadingScreenViewModel`: when the user selects a browsable source in the picker (or opens the
panel with one already selected), `BrowseSourceCatalogAsync` automatically calls
`SearchAsync(string.Empty, ...)` and populates `ArcSearchResults` with the whole list,
alphabetized, with `ArcSearchStatus` reporting the count (e.g. "41 title(s) available from
ReadThingsRight - browse below or narrow with a search."). Selecting a non-browsable source instead
shows a plain hint ("ComicVine is a live search - type a story arc or event name above.") rather
than silently showing nothing, since a blank panel with no explanation reads as broken.

The user can still type into the query box and hit Search to narrow the browsed list (or, for
ComicVine/Metron, to run a real search) - `SearchArc`'s existing behavior is unchanged, this is
purely an additional auto-populate-on-select step layered on top.

No new UI surface beyond the existing Arc Search panel - the source dropdown, results list, and
status line already there just get populated automatically instead of staying empty until a query
is typed.

## 2. Testing

No new unit tests - this is UI-orchestration logic (which adapter method to call and when) over
already-tested pieces (`SearchAsync`'s empty-query-matches-everything behavior is exercised
implicitly by every existing adapter-parsing test, and the reconciliation/build logic this reuses
is unchanged). Verified live: real catalog counts cross-checked against each source's actual
current HTTP response during design, not assumed from the original port.
