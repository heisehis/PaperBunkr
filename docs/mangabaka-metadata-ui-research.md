# MangaBaka Metadata Model & UI — Research Memo

*Research memo, not a design spec — feeds a future brainstorm → design-spec pass before
implementation starts, same status as `docs/tracker-manga-ui-research.md`. User-supplied interest:
MangaBaka's site is far richer than the basic search/link `IMetadataProvider` shipped this session
(`docs/superpowers/specs/2026-08-23-mangabaka-metadata-provider-design.md`), and the user wants to
draw on it for a future Paperbunkr metadata-model + UI push. Gathered two ways: browsing
mangabaka.org live (search, a real series page and all its tabs, the discover page, the search-tips
panel), and — added after the first pass, user-supplied — a genuine MangaBaka OpenAPI 3.1 spec
(`servers` confirms `https://api.mangabaka.org`, the real domain), which corrected and substantially
deepened several findings below, marked "(OpenAPI)" where they come from the spec rather than the
live browsing pass.*

## TL;DR

- **Correction after the OpenAPI spec turned up**: MangaBaka's personal library/tracking layer
  *is* a real, callable API after all — `PUT`/`PATCH /v1/my/library/{series_id}` accepts `state`
  (`considering`/`completed`/`dropped`/`paused`/`plan_to_read`/`reading`/`rereading`),
  `progress_chapter`, `progress_volume`, `rating` (0–100), `note`, `read_link`, and more, gated by
  a Personal Access Token (`x-api-key` header, format `mb-...`) or OAuth with a `library.write`
  scope. Paperbunkr's shipped `MangaBakaMetadataProvider` only calls the *unauthenticated* endpoints
  (search/get), which correctly have no such capability — but the earlier conclusion that MangaBaka
  "cannot be a tracker" was wrong. A real `ITrackerAdapter` (PAT-authenticated, simpler than the
  OAuth-based AniList/MAL/Shikimori/Bangumi ones already built) is a genuine future option, not a
  structural dead end.
- Series URLs are `mangabaka.org/{id}` (confirmed live: Kagurabachi is `/708`) — no `/manga/`
  segment, correcting an earlier guess in the metadata-provider design doc that 404'd. Worth
  backfilling `ExternalMediaMetadata.Url` with this pattern in a future pass (deliberately left
  `null` this session since the pattern wasn't yet confirmed).
- The tag taxonomy is deeply layered — **Genres, Themes, Settings, Activities, Character Traits**
  (with sub-facets like Appearance > Eyes / Hair), **Character Archetype, Character Types, Objects,
  Locations** — each tag additionally weighted **Incidental / Recurrent / Defining / Core** and
  flaggable as a spoiler. Paperbunkr's own `Issue.Genre`/`Issue.Tags` are flat CSV strings with no
  category, weight, or spoiler concept at all.
- **Typed relations** (Side Story, Alternative, etc., each with a count) and a **crowd-sourced
  multi-cover archive** (Banner / Front(Volume) / Back(Volume) / Other / Anime Promotional, each
  attributed to a source like "Manga Plus" or "Twitter / X", filterable by type + language,
  paginated) are both real, live features — directly relevant to Paperbunkr's own `MediaRelation`
  Related tab and its brand-new cover-art-override feature.
- **Two recommendation surfaces**, both explicitly labeled *(beta)*: "Similar series" (tag-based
  similarity) and "Readers also like" (shared-library-activity — collaborative filtering, not
  content-based). Paperbunkr already has a backend recommendation engine
  (`RecommendationResolver`, Metadata Model Phase 6a) with no homepage UI yet — this is a concrete
  UI reference for when that surfaces.
- **Rich cross-referencing UX**: search accepts `anilist:ID`/`al:ID`, `kitsu:ID`/`kt:ID`,
  `mangaupdates:ID`/`mu:ID`, `myanimelist:ID`/`mal:ID` prefixes, *or* accepts a pasted URL from any
  recognized reading/publisher site and resolves it to the matching series directly. Paperbunkr's
  own External Metadata/Trackers search flow is manual-title-search only today.
- A separate **"Works" tab** holds per-edition/per-printing data (format, page layout, release
  date, publisher, page count, trim size in mm) distinct from the series-level Info tab — closer in
  spirit to Paperbunkr's per-`Issue` fields, but organized as its own browsable list rather than
  folded into the main series page. A **"Collections" tab** (box sets/omnibus editions) is a
  further distinct layer neither Paperbunkr nor the six original tracker services model at all.

## Key findings

1. **Series & tab URL scheme** (confirmed live). `mangabaka.org/{numeric-id}` is the series page;
   sibling tabs are `/{id}/related`, `/{id}/covers`, `/{id}/news`, `/{id}/collections`, `/{id}/works`.
   No slug, no `/manga/` prefix — the earlier guess in the metadata-provider design doc
   (`mangabaka.org/manga/{id}`) was wrong and 404'd live; this is the corrected pattern.
2. **Info tab layout**: large cover, alt-titles ("Show N more titles"), a `Meta` badge row (format
   MANGA/NOVEL/etc., release status, licensed-in-English flag, volume/chapter counts, publication
   year, anime-adaptation flag, popularity rank overall + within format), a Staff/Publisher chip
   row, a **per-source score row** (distinct small icons each showing a 0–100-ish number — the
   homepage lists 7 tracked sources: AniList, MyAnimeList, MangaUpdates, Kitsu, Anime-Planet,
   Shikimori, ANN, matching the 7 score slots observed on a real series, one showing "-" where a
   source has no rating), description with a sourced attribution line and free-text "Note:" awards/
   trivia, a two-row external-links panel grouped by purpose (Publisher / Read Officially / Info /
   Social), and the tag panel described above. A "My library" panel (sign-in gated) and an "Edit
   series" button (community editing) sit alongside — the whole database is user-contributed/
   editable, not admin-curated.
3. **Tag taxonomy** (from a real series' rendered tag panel, not the API — the API's own `tags`
   field only exposes flat `{name, is_genre, is_spoiler}`, so this richer categorized view appears
   to be a website-only presentation layer over a flatter stored representation). Observed
   categories on one series: Genres, Themes, Settings, Activities, Character Traits (further split
   into Appearance sub-groups like Eyes/Hair), Character Archetype, Character Types, Objects,
   Locations. A tag can carry a hierarchical path (e.g. "Age > Young Male Lead", "Human Anatomy >
   Scars") and a weight tier (Incidental+/Recurrent+/Defining+/Core, filterable), plus a
   spoiler flag hiding it by default.
4. **Related tab**: relation *types* as filter toggles with live counts (e.g. "Side Story 2",
   "Alternative 1") — the same shape as Paperbunkr's own `RelationType` enum on `MediaRelation`,
   but MangaBaka's UI leads with type-grouped counts rather than one flat carousel.
5. **Covers tab**: a crowd-sourced archive, not a single canonical cover — "55 covers found" for
   one series, each tagged by type (Banner/Front(Volume)/Back(Volume)/Other/Anime Promotional) and
   frequently by source ("Banner from Manga Plus", "Cover from Jump Plus"), filterable by Type and
   Language, paginated. Directly adjacent to Paperbunkr's brand-new cover-art-override feature
   (`docs/superpowers/specs/2026-08-23-cover-art-override-design.md`) — MangaBaka treats "which
   image represents this" as a browsable many-to-one archive rather than a single override slot.
6. **News tab**: syndicated Anime News Network articles scoped to the series (with an "only direct
   news" filter implying a broader default that includes tangential mentions) — external content
   Paperbunkr has no equivalent source for; not directly portable, just a feature category worth
   naming.
7. **Works tab**: per-edition/printing records — format (Digital/Paperback), page layout, release
   unit (Volume), publish date, publisher, page count, trim size (mm), and a per-edition blurb —
   explicitly marked "work in progress and in public beta." This is edition-level granularity
   Paperbunkr doesn't model at all (an `Issue` is one file, not one of several published editions
   of that same content).
8. **Collections tab**: box-set/omnibus editions, a distinct concept from both single-volume Works
   and the flat series-level Info — another edition-adjacent layer with no Paperbunkr equivalent.
9. **Discover/browse taxonomy surfaces**: beyond the per-series tag panel, the Discover page ranks
   by dynamic categories like "Top in Late Modern & Contemporary," "Top in Time Skip," "Top in
   Transported to Another World" — confirming tags aren't just descriptive metadata, they're a
   first-class browsing/ranking axis on the site, plus separate "Trending" (7d/30d, per format),
   "Rising in libraries," "Hidden gems," and "New releases" rails.
10. **Recommendations**: "Similar series (beta)" is captioned "Based on shared tags" (content-based
    similarity); "Readers also like (beta)" is captioned "Based on shared library activity"
    (collaborative filtering off real per-user library data, which only MangaBaka's own logged-in
    userbase enables — Paperbunkr has no equivalent cross-user signal and never will for a
    single-user local library app, so only the tag-based half is realistically portable).
11. **Cross-reference matching UX** (confirmed live via the search page's own tips panel): search
    accepts `anilist:ID`/`al:ID`, `kitsu:ID`/`kt:ID`, `mangaupdates:ID`/`mu:ID`,
    `myanimelist:ID`/`mal:ID` prefixes for direct ID lookup, and separately accepts a pasted URL
    from *any* recognized site (reading site, publisher site, etc.) and resolves it to the matching
    series. A **Bookmarklet** feature lets a user viewing any manga elsewhere on the web one-click
    over to that series' MangaBaka page using the current page's URL/title. Paperbunkr's own
    External Metadata and Tracker search flows are title-text-search only today — no ID-prefix or
    URL-paste shortcut exists anywhere in the app.
12. **Personal library/tracking is real, and is an API, not just web pages** (OpenAPI, resolves
    what the live-browsing pass could only leave as an open question). `GET /v1/my/library`,
    `GET/POST /v1/my/library/batch`, and `GET/POST/PUT/PATCH/DELETE /v1/my/library/{series_id}`
    are all real documented endpoints — full CRUD on a per-series library entry, plus
    `GET /v1/my/profile`, `GET /v1/my/series/recommendations` (personalized, distinct from the
    public tag-based "similar series"), and `GET /v1/my/series/discover/top-genres`. See the
    corrected TL;DR bullet above for the write-endpoint's field set and auth requirement.
13. **Direct external-ID lookups exist as API endpoints, not just a search-box trick** (OpenAPI).
    `GET /v1/source/{anilist|anime-planet|kitsu|manga-updates|my-anime-list}/{id}` — unauthenticated,
    7-day CDN cache, returns "MangaBaka series and/or [source]'s raw API response" for that
    provider's numeric ID directly. Since Paperbunkr already stores an AniList `ExternalMediaId` for
    any series linked via the existing External Metadata flow, a future MangaBaka integration could
    resolve the *exact* same series with zero fuzzy-title-search ambiguity, rather than re-running a
    title search from scratch.
14. **Two API version families, and the shipped provider used the wrong one.** The spec confirms
    `v1` is labeled **"(stable)"** on every endpoint; `v2` (what `MangaBakaMetadataProvider` calls
    today) is labeled **"(beta)"**. `v1` is also the *richer* family — it alone exposes
    `/v1/tags`, `/v1/genres`, `/v1/series/{id}/relationships`, `/v1/series/{id}/related`,
    `/v1/series/{id}/images`, `/v1/series/{id}/works`, `/v1/collections/*`, `/v1/editions/*`,
    `/v1/publishers/*`, and the `/v1/source/*`/`/v1/my/*` endpoints above — `v2` only has a slim
    subset (search/get/match/batch/similar/readers-also-like/news/my-library). `v0` is
    moderator/admin-only (`/mod/*`), not relevant. Migrating the provider from `v2` to `v1` is a
    contained future fix, not attempted this session.
15. **The full genre list is a small fixed enum, separate from the free-form tag tree** (OpenAPI).
    `GET /v1/genres` returns 46 fixed values (action, adventure, comedy, drama, fantasy, horror,
    isekai-adjacent "school_life", mecha, mystery, psychological, romance, sci-fi, seinen, shoujo,
    shounen, slice_of_life, sports, supernatural, thriller, tragedy, yaoi, yuri, plus explicit
    content-rating-adjacent values like ecchi/erotica/hentai/smut, and demographic/format tags like
    doujinshi/josei/gender_bender). Separately, `GET /v1/tags` returns a hierarchical tree
    (`id`/`parent_id`/`name_path`/`is_spoiler`/`is_genre`) — the "Genres, Themes, Settings,
    Activities, Character Traits, Character Archetype, Character Types, Objects, Locations"
    categories observed live are top-of-tree nodes in *this* tree, not a separate category system as
    finding 3 first assumed. Whether/how the fixed genre enum and a tag node's own `is_genre: true`
    flag reconcile (duplicate systems? one derived from the other?) isn't resolved by the schema
    alone.
16. **The real relation-type enum is much larger than what one series' Related tab surfaced**
    (OpenAPI). `GET /v1/series/{id}/relationships` returns items typed as one of: adaptation,
    alternative, cameo, character_focus, compilation, contains, crossover, expansion, main, other,
    parent, parody, prequel, reboot, remake, sequel, series, side_story, source, spin_off, summary,
    uncollected — 22 values vs. the "Side Story"/"Alternative" actually present on Kagurabachi's own
    page (finding 4 only ever saw the types that series happened to have). A *second*, distinct
    endpoint, `GET /v1/series/{id}/related`, returns a pre-grouped-by-type curated view
    (`adaptation`/`alternative`/`main_story`/`other`/`prequel`/... arrays of series refs) — not the
    same data reshaped, a separate resource.
17. **The cover archive's type enum, confirmed** (OpenAPI). `GET /v1/series/{id}/images` is real
    and paginated; `available_types` is `audiobook`/`banner`/`chapter`/`other`/`season`/`volume`/
    `volume_back` — a couple of types (audiobook, chapter, season) weren't visible on the one
    series' Covers tab browsed live, so the live-browsing pass under-counted this taxonomy.
18. **Auth mechanics** (OpenAPI): two schemes, a Personal Access Token via `x-api-key` header
    (format `mb-...`, presumably generated from account settings — page not located this session)
    or OAuth via OpenID Connect (`https://mangabaka.org/.well-known/openid-configuration`). PAT is
    the simpler of the two and comparable in spirit to how `CredentialStore` already holds a bare
    API key for Bangumi.
19. **Rate limits are real and documented, not just "caching helps"** (found via the API docs page
    the user separately located, `mangabaka.org/data/api`, not the OpenAPI file itself). Limiting
    applies only to *uncached* requests (repeat identical requests are served from CDN cache,
    visible via a `cf-cache-status: HIT` response header); `GET /series/search` is capped at 30
    requests/minute per IP (leaky bucket), other endpoints at 180/minute, `/my/*` endpoints are
    never cached. Exceeding the limit returns `429 Too Many Requests`. **This session's shipped
    `MangaBakaMetadataProvider` originally paced itself at 500ms between requests (120/min) —
    looser than the real 30/min search limit — and has been corrected to 2000ms (30/min) after this
    was found**, a real fix landed alongside this memo update, not just a documentation note.

## Recommendations

Loosely staged, cheapest/lowest-risk first — none of this is scoped or approved for
implementation, this is groundwork for that future brainstorm:

- **Cheap, low-risk**: backfill `MangaBakaNormalizer`'s `Url` field with the now-confirmed
  `https://mangabaka.org/{id}` pattern instead of `null`. Migrate `MangaBakaMetadataProvider` from
  the beta `v2` API family to the stable, richer `v1` family (finding 14) — same search/get shape,
  just the correct/supported version. Add `anilist:`/`mal:`/`mu:`/`kt:` ID-prefix shortcuts to the
  existing External Metadata search box (AniList already supports numeric-id search per the
  original tracker doc's own notes on MAL's `id:`/`my:` prefixes) — a UX pattern proven across
  multiple of these sites now, not just MangaBaka's own invention. Also cheap: use
  `GET /v1/source/anilist/{id}` (finding 13) instead of a fresh title search whenever a Paperbunkr
  series already has an AniList id linked, for an exact match instead of a fuzzy one.
- **A real MangaBaka tracker adapter is now a legitimate option**, not ruled out by API shape the
  way this session first concluded (see the corrected TL;DR bullet + findings 12/18). PAT-based
  auth (`x-api-key: mb-...`) is simpler to build than the OAuth flows AniList/MAL/Shikimori/Bangumi
  already required — closer to how `CredentialStore` already holds Bangumi's bare API key. Worth
  weighing against MangaUpdates/Kitsu (the two originally-scoped-but-unbuilt trackers) next time
  tracker work is picked up, not assumed to be next in line automatically.
- **Metadata model**: the biggest single gap is Paperbunkr's flat `Genre`/`Tags` CSV strings vs.
  MangaBaka's categorized+weighted+spoiler-flagged taxonomy. A full port isn't warranted (Paperbunkr
  is a local library manager, not a crowd-sourced database — there's no community to maintain a
  taxonomy that rich), but a lighter categorized-tags model (even just Genre vs. Theme vs. Setting
  as three separate fields instead of one blended `Genre` string) would be a real, scoped
  improvement worth its own design pass. `GET /v1/genres` (the 46-value fixed enum, finding 15) is
  a ready-made reference list if a controlled Genre vocabulary is ever wanted instead of Paperbunkr's
  current free-text `Genre` string.
- **UI**: the icon-led metadata row pattern already adopted for the new manga detail screen
  (docs/superpowers/specs/2026-08-23-manga-detail-screen-design.md) matches MangaBaka's own Meta
  badge row in spirit. The outlined-pill tag chip style already shipped could adopt MangaBaka's
  weight-tier visual distinction (e.g., Core tags rendered bolder/filled vs. Incidental tags
  rendered lighter) once/if the categorized-tags model above exists to drive it.
- **Cover art**: MangaBaka's multi-cover archive (browsable by type/source, not just one override)
  is a richer model than the override feature shipped this session. Worth revisiting once there's
  a reason to store more than one custom cover per issue — not urgent, the shipped override already
  covers the immediate ask.
- **Recommendations**: when `RecommendationResolver` (Phase 6a) gets a homepage surface, MangaBaka's
  "Similar series — based on shared tags" framing/caption is a good UX reference for how to label a
  content-based (not collaborative-filtering — Paperbunkr has no cross-user data) recommendation
  rail honestly.
- **Editions/Works**: genuinely out of scope for now — Paperbunkr manages files a user owns, not a
  browsable catalog of every print/digital edition ever published. Noted for completeness, not
  recommended.

## Caveats

- Gathered two ways, noted inline throughout: browsing the live site with a real browser session
  (search, one real series page and all six of its tabs, the discover page, the search tips panel),
  and a user-supplied OpenAPI 3.1 spec whose `servers` entry points at the real
  `https://api.mangabaka.org`, giving good confidence it's genuine rather than a stale/third-party
  mirror — but it wasn't cross-checked against a second independent source beyond that. Findings
  marked "(OpenAPI)" come from reading the spec's schemas/descriptions directly (via a script, not
  by eye), not from exercising the endpoints live — field *names*/*enums*/*auth requirements* are
  as documented, but actual response *values* for anything beyond search/get (e.g. what a real
  `/v1/tags` payload looks like, or whether `/v1/my/library` write actually round-trips as
  documented) weren't exercised this session.
- Page text from the live-browsing pass was extracted via accessibility-tree/text reads, not full
  DOM inspection — some visual-only details (exact chip styling, icon meanings for the 7
  per-source score slots) are inferred from the user's own screenshots, not independently confirmed
  against MangaBaka's source.
- The per-source score icons (7 numeric values on one series) are inferred to correspond to the 7
  tracked sites named on the homepage (AniList, MyAnimeList, MangaUpdates, Kitsu, Anime-Planet,
  Shikimori, ANN) by count-matching alone — not confirmed by hovering/inspecting each icon
  individually.
- Tag taxonomy was observed live on exactly one series (Kagurabachi, a shounen action manga) —
  category presence/depth may vary by series (e.g., a romance series might surface different
  Character Archetype/Setting tags entirely). The OpenAPI spec confirms the underlying tree
  structure (`parent_id`/`name_path`) but wasn't queried live for its full contents, so the complete
  tag list itself (how many nodes, how deep) is still not verified — only the *shape* is.
- Whether the fixed `GET /v1/genres` enum (46 values) and a tag node's own `is_genre: true` flag
  are the same data exposed two ways, or two genuinely separate systems that can disagree, is not
  resolved by the schema alone (finding 15) — flagged as open, not guessed at.
- No ToS review was done beyond noting the site actively promotes API/bulk-download use for
  third-party tools (unlike AniList's explicit anti-hoarding language), and that `info.license`/
  `info.termsOfService` are both empty in the OpenAPI spec itself (terms live on the website, not
  embedded in the spec) — a real ToS check is a prerequisite before any usage beyond today's
  per-user manual search/link, not assumed clear.
