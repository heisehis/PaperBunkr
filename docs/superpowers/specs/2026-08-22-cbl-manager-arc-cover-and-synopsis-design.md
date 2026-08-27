# CBL Manager — Arc Cover Art & Synopsis — Design Spec

*Date: 2026-08-22. Scope: a same-day follow-up to the CBL Manager arc-lookup pass. The user asked
"what about the visual side" - a real gap: `ReadingList.Description`/`CoverImageUrl` were being
fetched via `IReadingListSource.GetArcOverviewAsync` and saved to the database at Create/Refresh
time, but nothing in the UI ever displayed either one. The original arc-lookup spec's §5 claimed
"ArcName/description populate the existing Subtitle/list header instead of a new surface" - that
binding was never actually written.*

## 1. What was missing

- `ReadingScreenViewModel.Subtitle` was a hardcoded string
  (`"Cross-series reading order · tracked list"`), never read from `list.Description`.
- `CoverImageUrl` is a remote URL - nothing in this codebase downloads/decodes/renders a remote
  image; every existing cover mechanism (`CoverImageCache`, `BookCoverImageCache`) reads a *local*
  file by numeric id.
- No indicator anywhere showed which external source a list came from, despite `ReadingList.Source`
  already being stored specifically so `Refresh` could look the source back up.

## 2. Design

**Synopsis:** `LoadReadingList` now sets `Subtitle` from `list.Description` when present, falling
back to the original generic text otherwise - no new property, reusing the surface the original
spec always intended.

**Source badge:** new `ArcSourceLabel` (`"via {DisplayName}"`), shown as a small pill next to the
created-date label, visible only when `IsArcLinked`. Backed by a new
`ReadingListSourceRegistry.GetDisplayName(sourceKey)` helper (`ReadingList.Source` stores the
adapter's `SourceKey`, e.g. `"ComicBookReadingOrders"` - not something to show a user directly).

**Cover art:** new `ArcCoverImageCache` (`Paperbunkr.App.Services`), mirroring `CoverImageCache`'s
on-disk-cache-plus-bounded-LRU shape but for a remote URL instead of a local file:
`ArcCoverPaths.GetCachePath(readingListId)` gives a per-list on-disk JPEG (mirrors
`CoverThumbnailPaths`'s per-`Issue.Id` convention, keyed on `ReadingList.Id` instead), so a cover
only needs downloading once, not on every app launch. `Get(id)` is cache-hit-only (sync, safe to
call from `LoadReadingList` directly); `DownloadAndCacheAsync` does the actual network fetch and is
only invoked on a cache miss, from a fire-and-forget background `Task` (`LoadArcCoverAsync`) that
re-checks `_activeReadingListId` before assigning the result, so a slow download from a list the
user has since navigated away from can't clobber whatever's currently displayed. A failed/missing
cover is silent (null) - same "never blocks the caller" contract `GetArcOverviewAsync` itself
already has; the header's cover `Border` is simply not shown (`IsVisible` bound to a not-null
check) rather than rendering a broken-image placeholder.

**Header layout:** `ReadingScreen.axaml`'s header `Grid` gained a third column (cover thumbnail,
56×80, left of the title stack) and a source-badge row under the title/subtitle. No changes to any
other screen - this is scoped to the one place an arc-linked list's identity is shown.

## 3. Testing

`Paperbunkr.App.Tests`: `ArcCoverImageCacheTests` - cache-hit/miss, same-instance-on-repeated-lookup,
`Invalidate` clearing both the in-memory entry and the on-disk file. The live-download path
(`DownloadAndCacheAsync`) isn't unit-tested - same "no live network in CI" stance the adapter tests
already took. Not yet live-verified in the running app as of this write-up - whole-solution build is
clean and the new unit tests pass, but nobody has looked at a real arc-linked list's header on
screen yet to confirm the cover/synopsis/badge actually render as designed.
