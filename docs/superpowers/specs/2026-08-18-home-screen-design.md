# Home Screen: Recommendation & Discovery Modules

**Date:** 2026-08-18
**Status:** Approved, pending implementation
**Related:** [[phase6a-recommendation-engine]] (backend this reuses as-is), inspired by three
features from a companion web project's ("Omnibus") changelogs — genre-frequency spotlight,
reading-list spotlight synopsis/tags derivation, and its general home-page shelf layout —
adapted to Paperbunkr's own data model rather than ported verbatim.

## Context

Phase 6a shipped `RecommendationResolver` — a real, tested, relationally-anchored "because you read
X, try Y" engine — explicitly with no caller: no homepage screen exists in Paperbunkr today (rail
nav is Library/Books/Detail/Smart Lists/Reading Lists/Story Events/Plugins/Preferences, landing on
Library at launch). This spec adds that screen.

**No CE precedent** — checked `_reference/ComicRackCE` and `docs/onboarding.md` first, per this
project's standing rule; neither mentions a Start Page/Home/Dashboard concept. This is a genuinely
new feature, not CE parity, same category as continuous scroll was.

A real risk surfaced while designing this: `RecommendationResolver`'s candidate pool is
relationally-anchored (`MediaRelation`/`Continuity`/`StoryEvent` data), which most libraries won't
have curated yet — a homepage built from *only* that resolver could render mostly empty on day one.
Two other module types need no new resolver logic at all (`Issue.AddedTime`/`OpenedTime`/
`IsInProgress()` already exist) and guarantee something useful shows up immediately. A third,
genre-frequency-based module (adapted from a companion project's "Something For You" spotlight)
closes the same gap from a different angle — it works from data every library already has
(`Issue.Genre` + read history), so it doesn't go empty just because nothing's been manually related
yet.

## Scope

Five modules on one new screen, described below in on-screen order. All five re-query fresh on
every visit to Home — no caching, matching every other screen in this codebase (Library, Smart
Lists, Reading Lists all already reload on navigation rather than persisting a snapshot).

### Screen & navigation

- New rail-nav screen, `CurrentScreen == "home"`, its own rail icon as the **first** rail entry
  (above Library). No icon in the existing `coolicons` asset pack reads as "Home" — left as a text
  glyph placeholder for now rather than forcing a wrong-feeling icon, matching this codebase's
  existing precedent for genuine icon-pack gaps (rail-nav back arrows, sort/group carets in the
  Alpha icon-pack sweep).
- `HomeScreenViewModel(Action<int> goDetailForSeries, Action<int> goReaderForIssue)` — reuses the
  exact two callback signatures `LibraryScreenViewModel`/`SmartScreenViewModel` already take from
  `MainViewModel`, wired the same way.
- **`CurrentScreen`'s default changes from `"library"` to `"home"`** — Home becomes the app's
  launch screen; Library remains one rail click away, unchanged otherwise.

### Module 1 — Continue Reading (row)

Series with at least one issue where `Issue.IsInProgress()` is true (`IssueMetadataExtensions.cs` —
`ReadPercentage()` between 0 and the 95% "read" threshold, already exists, no new logic). Sorted by
that in-progress issue's `OpenedTime`, most recent first. Up to 10 series.

Card click → `goReaderForIssue(issueId)` on the in-progress issue directly — one click back into
reading, matching how `LibraryScreenViewModel.ContinueReadingCommand` already behaves for the
series-card overlay button.

Empty state: "Nothing in progress yet" when no series has any in-progress issue.

### Module 2 — Recently Added (row)

Series sorted by `Issues.Max(AddedTime)` descending — same aggregate `SeriesCardSample.LastAddedTime`
already computes for Library's own sort field. Up to 10 series.

Card click → `goDetailForSeries(seriesId)`.

Empty state: "Nothing added yet" (only reachable on a genuinely empty library, since any imported
issue has an `AddedTime`).

### Module 3 — Because You Read (up to 3 rows)

Seed series: the 3 most-recently-opened series (by `Issues.Max(OpenedTime)`, reusing the same
aggregate as Module 1/2). For each seed, one row: `RecommendationResolver.GetRecommendations(context,
seedSeriesId, limit: 10)`, used exactly as Phase 6a shipped it — no new resolver logic.

A seed whose row comes back empty (no relation/continuity/event data for that series) is dropped
entirely — no visibly-empty row. If all 3 seeds produce nothing, one single combined message
replaces the whole section: "No recommendations yet — link related series from a series' Detail
screen to see them here." (Points at the existing Related-tab UI, the actual way to populate
`MediaRelation` data today.)

Card click → `goDetailForSeries(seriesId)`.

### Module 4 — Spotlight (single wide card, new)

Adapted from the companion project's genre-frequency "Something For You" module, using only fields
this codebase already has:

1. Build a genre-frequency map: tokenize `Issue.Genre` (comma-split, same convention
   `RecommendationResolver`'s private `Tokenize` already uses — exposed as a small shared helper
   rather than duplicated, exact location decided at plan time) across every issue where
   `HasBeenRead()` is true.
2. Candidate pool: every issue that is **neither** `HasBeenRead()` **nor** `IsInProgress()` (i.e.
   genuinely untouched — this exclusion is what keeps Spotlight from ever double-showing something
   Module 1 already surfaces).
3. Weighted-random pick from the candidate pool: each candidate's weight is the sum of the
   frequency-map counts for its own genre tokens (a candidate matching two genres the user reads a
   lot gets a proportionally higher chance than one matching a single rarely-read genre; a candidate
   matching nothing in the map gets weight 0 and drops out of this step, not the fallback below).
   Not deterministic highest-match every time, so it varies across visits the way the companion
   project's re-rolling spotlight did.
4. Fallback: if there's no read history yet (empty frequency map) or no candidate matches any known
   genre, pick uniformly at random from the same untouched-issue candidate pool instead.
5. Empty state: "Your library's all caught up" when the candidate pool itself is empty (every issue
   is either read or in progress).

Card click → `goReaderForIssue(issueId)` directly — a direct "read this now" nudge, matching the
companion project's "Read Now" framing, distinct from Modules 2/3's "go look at this series" framing.

**Not ported from the companion project:** the 12-hour per-device cache and the auto-advancing
single-card carousel mechanic (dot pagination, 7s auto-advance) — both add real state/complexity
this spec doesn't need; a fresh pick every visit is simpler and matches this codebase's no-caching
convention everywhere else. HTML-stripping (the companion project's Problem 1) isn't applicable
today — checked `AniListNormalizer.cs`: nothing in Paperbunkr renders `ExternalMetadataSnapshot.
Description` on screen yet (Phase 5b's adapter is backend-only), so there's no field to strip HTML
from. Worth remembering if that UI gets built later.

### Module 5 — Try This Reading List (single wide card, new)

Candidate pool: every `ReadingList` with at least one `ReadingListItem` whose `Issue.LastPageRead is
null or 0` (an unread item). Uniform-random pick among candidates, fresh every visit.

Card shows:
- `ReadingList.Name`
- Synopsis: `ReadingList.Description` if set, else a generated fallback — `"A {N}-issue reading
  order starting with {series}."`, where `{N}` is `Items.Count` and `{series}` is the first item's
  (by `SortOrder`) issue's series name.
- Up to 5 tags: top genres by frequency across the list's linked issues' `Genre` fields (same
  tokenization helper as Module 4).

Card click → `goReaderForIssue(issueId)` on the list's first unread item (by `SortOrder`) — same
direct-to-reading pattern as Module 4, reusing the identical callback with no new navigation
plumbing.

Empty state: "No reading lists with anything left to read" (only reachable if every list is either
empty or fully read, or no lists exist at all).

## Explicitly out of scope

- **Updates feed** (day-grouped, per-tracked-series "what's new" page, Komikku/Tachiyomi-style) —
  Paperbunkr already has the right schema for "which series am I tracking"
  (`Series.TrackingLinks`/`TrackingLink`), but this is a whole new rail-nav screen with its own
  grouping/filtering, not a Home module. Separate future spec.
- **Reading History screen** (grid/compact/list view modes, search, sort, persisted view
  preference) — Paperbunkr has no History screen at all today; also screen-sized, not shelf-sized.
  Separate future spec.
- Nav entries for either of the above — moot until they exist.

## Testing

- `HomeScreenViewModelTests` (new, mirrors `LibraryScreenViewModelTests`' pattern: isolated temp
  SQLite via `PaperbunkrDbContext.DatabasePathOverride`, `AvaloniaTestCollection`) — one or more
  cases per module covering: correct candidates selected, correct sort/pick logic, the
  Module-1/Module-4 mutual-exclusion (an in-progress issue never appears in Spotlight's candidate
  pool), Module 3's per-seed-empty-row-dropped and all-empty combined-message behavior, and each
  module's own empty state.
- One new `Paperbunkr.App.UiTests` case (FlaUI/UIA3, real compiled exe) confirming: the app now
  launches to Home by default, the rail nav entry exists and is clickable, all 5 module
  headers/cards render, and a card click navigates correctly for one row-module (Continue Reading)
  and one single-card module (Spotlight).
