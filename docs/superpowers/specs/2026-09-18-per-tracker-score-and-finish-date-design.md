# Per-Tracker Score & Finish-Date

**Date:** 2026-09-18
**Status:** Approved, pending implementation plan
**Source:** User-reported gap ("missing stuff from Komikku") comparing Paperbunkr's Trackers UI
against Komikku's per-tracker Status/Progress/Score/Finish-date cards. Reached via a `/grilling`
pass per `CLAUDE.md`'s brainstorming-replacement rule, in the same session that fixed two real
live tracker bugs (MangaBaka's PUT-vs-POST 404, MangaDex's missing token refresh + User-Agent) -
every per-tracker API capability claim below was live-verified against real documentation/source
this session, not assumed, after those two bugs demonstrated what guessing costs here.

## Context

`ITrackerAdapter.PushEntryAsync`/`GetEntryAsync` (`src/Paperbunkr.Data/Tracking/ITrackerAdapter.cs`)
carry exactly `Status` + `ChapterProgress` today, pushed/pulled via one shared "Sync with Trackers"
button in `DetailTabsViewModel.cs`. The Trackers UI is a flat row of link/unlink chips with no
per-tracker detail view. Komikku's reference (user-supplied screenshot) shows each linked tracker
as its own expandable block with Status/Progress/Score/Finish-date, each individually editable.

Paperbunkr already has a personal rating concept - `Issue.Rating` (`float?`, 0-5, CE-parity "My
Rating" from `ComicBook.Rating`, clamped 0-5 in `_reference/ComicRackCE/ComicRack.Engine/
ComicBook.cs:405`) - but it's per-Issue, while tracker links are per-Series. No Series-level
rating exists yet.

### Verified per-tracker capability matrix

Every row below is sourced from a real fetched spec/doc/open-source-client this session (URLs in
the design-tree research, not repeated here) - not general knowledge, per the standing rule and
this session's own two prior mistakes.

| Tracker | Score field | Scale | Finish date |
|---|---|---|---|
| AniList | `score` (GraphQL) | Per-user (`mediaListOptions.scoreFormat`: `POINT_100`/`POINT_10_DECIMAL`/`POINT_10`/`POINT_5`/`POINT_3`) | `completedAt` (`FuzzyDate`) |
| MyAnimeList | `my_list_status.score` | 0-10 int | `finish_date` (`YYYY-MM-DD`) |
| MangaBaka | `rating` (same `PUT/PATCH /v1/my/library/{id}` call) | 0-100 | `finish_date` (`YYYY-MM-DD` preferred; `start_date` also exists, not used here) |
| Kitsu | `rating` (same GraphQL mutation as status/progress on **update**; not accepted on **create**) | 2-20 int, 0 invalid, `null` clears | `finishedAt` (ISO-8601, same update-only restriction) |
| MangaDex | separate `POST/DELETE /rating/{mangaId}` | 1-10 int | not supported |
| Shikimori | `score` (same `user_rates` call) | 0-10 int | not supported |
| Bangumi | `rate` (same collection-update call) | 0-10 int | not supported |
| MangaUpdates | separate `PUT/DELETE /v1/series/{id}/rating` | 0.1-10.0 decimal | not supported |

Finish date: only 4 of 8 (AniList, MyAnimeList, MangaBaka, Kitsu). The other 4 show it as
unsupported, not guessed at.

## Scope

### 1. Data model

- `TrackerPushPayload`/`TrackerRemoteEntry` (`ITrackerAdapter.cs`) gain `decimal? Score` and
  `DateOnly? FinishDate`. Both nullable/optional - an adapter for a tracker without one of these
  fields simply never sets it.
- New `Series.Rating` (`float?`, 0-5, same scale as `Issue.Rating`) - a real, separate, persisted
  field, not a live-computed property.
- New `MetadataProposalField`-style enum member isn't needed here; this isn't a metadata-provider
  proposal, it's tracker push/pull, a different pipeline (`ITrackerAdapter`, not
  `MetadataLinkResolver`).

### 2. `Series.Rating` mechanics

- **Push source:** average of the series' rated `Issue.Rating` values (nulls excluded, not
  treated as 0), recomputed whenever an `Issue.Rating` changes or a sync runs, then converted to
  each tracker's own scale at push time (see §4 for AniList's variable format).
- **Pull is display-only by default, not an automatic overwrite.** A single shared `Series.Rating`
  can't honestly reflect 8 potentially-disagreeing remote scores; auto-overwriting it from
  whichever tracker happened to sync most recently would silently clobber the others (or the
  user's own issue-rating average) with no visible cause - exactly the class of bug already hit
  twice this session with guessed API shapes. Each tracker's expanded panel shows its own pulled
  score next to the local value (e.g. "AniList: 8/10"), informational only. `Series.Rating` is
  only overwritten by an explicit "Use this score" action inside that specific tracker's panel -
  never as a side effect of the generic "Sync with Trackers" button.

### 3. Adapter changes (all 8, `src/Paperbunkr.Data/Tracking/Adapters/`)

Per-adapter, additive to the existing push/pull call already there for Status/Progress except
where a tracker needs a *separate* endpoint:

- **AniList, MyAnimeList, MangaBaka, Kitsu, Shikimori, Bangumi**: score (and date, where
  supported) ride the same mutation/PUT/PATCH call that already sends status/progress - just more
  fields on the same request.
- **MangaDex, MangaUpdates**: score needs a second HTTP call to a dedicated rating endpoint
  (`POST/DELETE /rating/{mangaId}`, `PUT/DELETE /v1/series/{id}/rating`). Clearing a score issues
  `DELETE`, not a `0`/`null` value on the same endpoint - mirrors Mihon's own verified behavior for
  both, not a new convention.
- **Kitsu specifically**: `rating`/`finishedAt` are only accepted on the **update** mutation, not
  **create** - a first-time push to a series with no existing Kitsu library entry needs a
  create-then-update two-step, same shape `MangaBakaTrackerAdapter`'s new PUT-then-POST-on-404
  fallback (landed earlier this session) and `MangaUpdatesTrackerAdapter`'s existing add-then-
  update pattern already establish - reuse that precedent, don't invent a fourth shape.
- **AniList's score format**: fetch `mediaListOptions.scoreFormat` once per push/pull (cached for
  the call, not persisted - a user could change it on AniList's own site between syncs), convert
  `Series.Rating`'s 0-5 value into that exact format going out, and the returned raw score back
  into a 0-5 equivalent coming in. Sending a raw number blind would silently misdisplay on
  AniList's own site - the same "doesn't reflect on the site" bug class already fixed for MangaDex
  this session.
- Every adapter change here follows this session's own detailed-error-return precedent
  (`PushEntryDetailedAsync`-style `(bool Success, string? ErrorDetail)`) rather than the original
  bare-bool `ITrackerAdapter` contract, extending the pattern already retrofitted onto MangaBaka
  and MangaDex to the remaining 6 adapters as part of this same pass - "polish it for all the
  current trackers" per the approved scope, and the template future adapters follow.

### 4. UI (Detail screen, Details tab)

Layout **C** from the visual companion pass: today's chip row stays as-is; clicking a chip expands
a details panel beneath it (one tracker's panel open at a time) showing Status/Progress/Score/
Finish-date, each independently editable inline. A field the linked tracker doesn't support (per
the matrix above) renders as a plain "not supported" label, not hidden entirely - matches this
session's own "n/a, not silently absent" precedent from the MangaDex External-Metadata cover work.

Editing any field in an expanded panel **pushes immediately to that one tracker**, not queued for
the shared "Sync with Trackers" button - the panel is already scoped to one service, so an
edit-then-separate-sync step would be a confusing extra action for a change already made to one
specific place.

## Explicitly out of scope

- **Tracking behavior settings** (auto-open-track-menu-on-add, auto-sync-on-read,
  auto-pull-from-trackers, MangaDex-source-metadata-autoselect - per the user-supplied Komikku
  Preferences screenshot). Explicitly deferred to its own follow-up design pass - it's a genuinely
  separate feature (automation triggers and defaults, not a data/UI gap) with its own open
  questions (what exactly "Update progress when marked as read: Always" governs, what triggers
  each auto-action) that don't belong bundled into this spec. Noted here so it isn't lost.
- **A controlled/normalized cross-tracker score scale.** Each tracker's panel shows and edits that
  tracker's own native scale (already settled by picking UI layout C, which is inherently
  per-tracker, not a side-by-side comparison needing one unified scale).
- **MangaDex/MangaUpdates finish-date, MangaDex/Shikimori/Bangumi/MangaUpdates any-date** -
  confirmed absent from all four real APIs this session, not a gap to work around.

## Implementation phasing

- **Phase A** - Data model: `TrackerPushPayload`/`TrackerRemoteEntry` gain `Score`/`FinishDate`;
  new `Series.Rating` + EF migration; `Series.Rating` recompute-from-`Issue.Rating` helper.
- **Phase B** - Adapter changes, one tracker at a time, richest-and-most-certain first: AniList,
  MyAnimeList, MangaBaka (same-call additions) - then Kitsu (create-then-update quirk) - then
  MangaDex, MangaUpdates (separate rating endpoint + DELETE-on-clear) - then Shikimori, Bangumi
  (score-only, same-call). Each adapter's detailed-error return added in the same step as its
  score/date wiring, not a separate pass.
- **Phase C** - UI: chip-click-to-expand panel (layout C), inline editing per field, immediate
  single-tracker push on edit, "Use this score" explicit-pull action, "not supported" rendering
  for unsupported fields.

## Testing

- Unit tests per adapter (fake `HttpMessageHandler`, matching this session's own established
  precedent) for: score/date included in the push request body where supported; the
  separate-endpoint + DELETE-on-clear behavior for MangaDex/MangaUpdates; Kitsu's create-then-
  update fallback; AniList's scoreFormat fetch-and-convert round-trip.
- Unit tests for `Series.Rating`'s recompute-from-`Issue.Rating` average (nulls excluded, empty
  series → null, not 0).
- ViewModel tests for the expand/collapse panel state, per-field immediate push, and "not
  supported" rendering for a tracker missing a given field.
- On-screen verification (this project's UI-automation harness): expand a real linked tracker's
  panel, edit Score/Finish-date, confirm it lands on the actual tracker site - same verification
  standard that caught the MangaBaka/MangaDex bugs this session in the first place.
