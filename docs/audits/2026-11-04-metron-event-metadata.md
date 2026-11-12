# Metron event metadata feasibility

Investigated 2026-11-04. Research only; no feature design or implementation approved.

## Finding

Metron can directly supply a matched event's name, description and image, subject to the record actually containing those values. It cannot directly fill every field in Paperbunkr's Edit Event dialog. Event dates can be proposed from member-issue publication dates; event types and tags require Paperbunkr rules and review rather than copying authoritative event fields.

## Field mapping

| Paperbunkr field | Verified Metron data | Assessment |
| --- | --- | --- |
| Name | Arc detail `name`; search rows have `id`, `name`, `modified` | Direct after identity is verified. |
| Description | Arc detail `desc` | Direct if populated; the inherited model permits blank descriptions. |
| Event cover | Arc detail `image` | Direct if populated; Arc image permits blank. Member-issue `image` can supply a proposed fallback, but is an issue cover rather than event artwork. |
| Start/end date | No Arc date fields; member issues provide `cover_date` and `store_date` | Derived proposal only. A min/max range represents the selected issue set's publication range, not an authoritative event duration. Missing issues/dates, tie-ins, reprints and collected editions can distort it. Choose and label the date basis consistently. |
| Event types | No Arc type field | Not directly supplied. Metron's Arc resource establishes that it is catalogued as an arc, but does not distinguish Paperbunkr's crossover/limited-series/story-arc/one-shot/other flags. Series `series_type` can provide evidence for suggestions, not an equivalent event-level classification. |
| Event tags | No Arc tags field | Not directly supplied. Series genres and issue characters/teams/universes are possible inputs to reviewed suggestions, but do not define a curated event-tag vocabulary. |

Evidence: [Arc serializer](https://github.com/Metron-Project/metron/blob/master/api/v1_0/serializers/arc.py), [Arc model](https://github.com/Metron-Project/metron/blob/master/comicsdb/models/arc.py), [common model](https://github.com/Metron-Project/metron/blob/master/comicsdb/models/common.py), [issue serializer](https://github.com/Metron-Project/metron/blob/master/api/v1_0/serializers/issue.py), [series serializer](https://github.com/Metron-Project/metron/blob/master/api/v1_0/serializers/series.py). These are schema/source findings, not proof of completeness of individual production records.

The actual Arc read serializer exposes `id`, `name`, `desc`, `image`, `cv_id`, `gcd_id`, `resource_url`, `modified`, and `attribution`. Its list serializer exposes only `id`, `name`, `modified`. The README mentions `slug` in Arc list responses, but the serializer does not expose it: prefer the actual serializer when implementing.

## Issue membership and reading order

`GET /api/arc/?name=...` searches candidates. `GET /api/arc/{id}/` retrieves the overview. `GET /api/arc/{id}/issue_list/` retrieves paginated membership; follow every `next` page before claiming a complete date span or member set. Arc issues are sorted by `cover_date`, `series`, `number` in the server mixin, not by a curated narrative reading-order field. Multi-series membership alone is evidence for a possible crossover, not proof of one.

Metron also has separate reading lists: `/api/reading_list/`, `/api/reading_list/{id}/`, `/api/reading_list/{id}/items/`. Their items have explicit `order` and `issue_type`; list metadata includes `list_type`, description and image. `EVENT` is a reading-list category, not an Arc crossover flag. Lists have owners and visibility rules; a matching name does not establish that two lists or an Arc and list are the same event. This could be a later complementary source, not a silent substitute.

Sources: [viewsets and issue-list mixin](https://github.com/Metron-Project/metron/blob/master/api/views.py), [reading-list serializers](https://github.com/Metron-Project/metron/blob/master/api/v1_0/serializers/reading_list.py), [API documentation](https://github.com/Metron-Project/metron/blob/master/api/README.md). Series types explicitly include One-Shot and Limited Series: [fixture](https://github.com/Metron-Project/metron/blob/master/comicsdb/fixtures/series_type.yaml).

## Existing Paperbunkr foundation

- `src/Paperbunkr.Data/ReadingLists/Sources/MetronSource.cs` already searches arcs, retrieves all paginated arc issues, and retrieves `desc`, `image`, `cv_id` through `GetArcOverviewAsync`.
- `src/Paperbunkr.App/Services/StoryEventDetailsCompletion.cs` already fills blank descriptions and missing provider images for verified provider identities. It preserves nonblank descriptions and manual covers; explicit refresh can update a nonmanual provider cover. Automatic completion uses a 30-day check interval.
- The current arc-issue parser retains a year derived from cover/store date, rather than the full dates. A future exact date-range proposal would need to retain full dates or use existing local issue dates.
- Parent investigation additionally verified that existing event chronology already falls back from explicit event dates to local member-date min/max. A new feature should account for that existing behavior rather than duplicate it.

## Access, limits and attribution

The published API guide supports Basic authentication and per-application Bearer tokens. The rate-limit guide documents defaults of 20 requests/minute and 5,000/day per authenticated user, with account-dependent sustained allowances; read response headers rather than hardcode assumptions. Honor `Retry-After` on 429. Conditional 304 responses still consume request slots. Paperbunkr's source already uses its shared Metron HTTP client and limiter.

Arc detail now exposes `attribution` objects (`source`, `url`). The official guide asks clients reusing descriptions to credit those sources, including when original-source licences require it. Any expansion should preserve that provenance; this report does not determine each description's licence.

Sources: [authentication and attribution guide](https://github.com/Metron-Project/metron/blob/master/api/README.md), [rate-limit guide](https://github.com/Metron-Project/metron/blob/master/api/RATELIMIT.md), [Arc read serializer](https://github.com/Metron-Project/metron/blob/master/api/v1_0/serializers/arc.py).

## Verification limit and practical implication

The public Metron Arc page returned an Anubis Access Denied page to the research browser; the live API/schema was inaccessible there. The exact Timestorm 2009/2099 record in the screenshot was therefore **not verified**. No claim is made that its Metron description or artwork is better or even present. Name/description/image support is verified in published source, not in an authenticated production response. The repository master tree reported SHA `9b7070c8745f1970501a1084f9b21c4ec6e3a355` during investigation; source deployment parity was not checked.

The defensible feature boundary is a reviewed enrichment proposal: direct provider values where available, explicitly derived dates, and optional reviewed type/tag suggestions. Metron alone cannot promise all slots filled accurately. Existing nonblank description protection also explains why a weak imported description may remain: improving it requires an explicit reviewed replacement path rather than assuming refresh replaces it.

## ComicVine comparison (follow-up, 2026-11-04)

ComicVine is another useful enrichment source, but the evidence here does not establish a way to fill every event slot authoritatively. Paperbunkr's existing `src/Paperbunkr.Data/ReadingLists/Sources/ComicVineSource.cs` already requests arc `deck`, `description` and `image`, searches arc names, and retrieves member issue dates and covers. That supports name, summary/description and image proposals; issue publication ranges are still derived dates. This investigation did not verify dedicated ComicVine arc fields matching Paperbunkr's multi-select event types or curated event tags. The official API documentation could not be fetched directly because of robots restrictions, and no authenticated live API call was made: local client expectations are distinguished from a newly verified official schema.

The parent investigation retrieved indexed content from the exact first-party [Timestorm 2009/2099 page](https://comicvine.gamespot.com/timestorm-2009-2099/4045-55897/). It showed a useful one-sentence introductory summary, six member issues, and a detailed section consisting of collected-edition and non-US-edition links. Those detail headings and edition titles match the description visible in the user's screenshot. This is evidence about this specific ComicVine web page, not proof of what the current authenticated API returns.

The local overview parser explains a plausible path to the screenshot: it prefers any nonempty `description`, strips its HTML, and uses `deck` only when the raw description is empty. Thus an edition-links-only description can win over a more useful summary. Separately, event completion preserves an already nonblank local description. Even a better provider value would therefore need an explicit reviewed replacement path to improve this event; ordinary refresh is not expected to replace it.

Practical implication: comparing both providers' descriptions and offering a summary/description choice could add more value than assuming ComicVine automatically fills the gaps Metron lacks. This is a feasibility observation, not an approved feature design. No application behavior was changed.

## GCD comparison (follow-up, 2026-11-04)

GCD has genuine story-arc metadata in its full database, but Paperbunkr's bundled GCD extract and GCD's current public API do not expose that capability. It is therefore a possible supplementary source, not a ready replacement for Metron/ComicVine event enrichment.

The current `beta` source's `StoryArc` has `name`, `sort_name`, `disambiguation`, `language`, `year_first_published`, an uncertainty flag, `description`, and `notes`. Stories link to arcs through a many-to-many relation. Arc membership is organized by issue publication dates and distinguishes reprints; this does not establish curated narrative reading order. There are no arc image, start/end date, event-type or tag fields. Arc relations represent relationships such as parent/sub-arc and translations, not Paperbunkr's event classifications. [Full arc/story model](https://github.com/GrandComicsDatabase/gcd-django/blob/beta/apps/gcd/models/story.py)

| Edit Event field | GCD potential | Accessible through current Paperbunkr extract? |
| --- | --- | --- |
| Name | Full database has arc name. | No arc records. |
| Description | Full database has arc description; issue-story synopses are additional material, not an equivalent event overview. | No arcs or story synopses. |
| Start/end date | Only arc first-publication year, possibly uncertain; exact spans need member issue dates and a defined date basis. | Issue dates can support already-matched local members, not discover arc membership. |
| Event types | No corresponding Arc fields. | No. |
| Tags | Story-level keywords and genre can support suggestions, not authoritative event tags. | No story records/keywords. |
| Cover | API issue cover URLs provide issue-level candidates; no Arc artwork field. | No covers. |

The currently published API code registers issue, series and publisher routes, plus issue/series searches and series overview. It does not register an arc route. Its nested Story serializer exposes `synopsis`, `genre`, `keywords`, title, characters and credits, but omits the `story_arc` relationship. The issue serializer includes publication/key/on-sale dates and a cover URL (empty if absent). Thus the API can enrich known issues, but its present shape cannot retrieve an arc description or resolve an event to its full arc membership. The series-overview endpoint includes each issue's longest comic-story sequence, not an event synopsis. [API routes](https://github.com/GrandComicsDatabase/gcd-django/blob/beta/apps/api/urls.py), [serializers](https://github.com/GrandComicsDatabase/gcd-django/blob/beta/apps/api/serializers.py)

Parent inspection of Paperbunkr's current integration confirmed that its schema-1 bundled extract retains series, issues/dates and series bonds, while excluding arcs, stories and covers. An older September snapshot count in a design document is not a verified current GCD arc count. Reaching full arc data would require an expanded extract/import or another verified access path; the existence of `StoryArc` in GCD source does not make it available through the shipped extract.

The official API wiki, edited 2026-10-21, describes stable endpoint URLs but unstable fields/data formats, Basic/Session authentication, and API V2 as still under development. Published limits are 25 requests/minute and 2,000/day for registered users; anonymous testing gets 30/hour and may be disabled. These are documented limits, not measurements from an authenticated request. [Official API documentation](https://github.com/GrandComicsDatabase/gcd-django/wiki/API)

GCD's FAQ distinguishes its attributable catalogue data from cover scans: data reuse requires attribution/link-back, while GCD does not own cover-image rights and does not provide a bulk cover-image grant. The API returning a URL should not be read as image ownership or unrestricted redistribution permission. [Official FAQ](https://github.com/GrandComicsDatabase/gcd-django/wiki/FAQ)

Verification used current published `beta` model/API source, cross-checked against `master` serializers/routes; the observed master tree SHA was `2321802530fa6ef0386b2b41ec788c1d9a04fbf9`. No live GCD arc payload, exact Timestorm arc, production deployment parity or current populated-arc count was verified. No application behavior was changed and brainstorming has not started.

### Full local GCD dump inspection (later fact-check for brainstorming)

The user's `C:/Users/DeeDee/Downloads/current.zip` contains the SQLite snapshot `2026-09-15.db` (6,723,248,128 bytes). After confirming 21.16 GB free space, the sole entry was extracted to `C:/Users/DeeDee/AppData/Local/Temp/paperbunkr-gcd-event-research-db123afbc8ef4cf48dca4eaa36edd7b6/2026-09-15.db`. All database connections used SQLite URI `mode=ro`; the archive and app database were not modified. The temporary full-size copy was removed after verification to reclaim space; the original archive remains. These fresh queries supersede the earlier inability to inspect local snapshot coverage; they do not establish current production GCD coverage.

- `gcd_story_arc`: **582 rows, all active; 142 nonblank descriptions**, 488 with a first-publication year and 2 uncertain-year flags.
- `gcd_story_story_arc(id, story_id, storyarc_id)`: 4,629 links covering 478 arcs and 4,586 distinct stories. Consequently 104 arcs have no membership links in this snapshot.
- Among linked active stories' active issues: 3,219 distinct issues; 2,906 have nonblank on-sale dates; 71 have the uncertainty flag set; 3,218 have nonblank key dates. Key dates may contain zero day/month components and are not necessarily valid exact calendar dates.
- Arc columns confirm name/sort name/disambiguation/language/description/notes/year plus uncertainty, with no event types, tags or cover. `gcd_issue.on_sale_date_uncertain` is separate information that an extended extract should retain. `gcd_story` has synopsis and genre. Keywords use `taggit_tag` joined to `taggit_taggeditem(tag_id, object_id, content_type_id)`; distinguish story versus issue content types through `django_content_type`.

**Timestorm in this snapshot:** no arc name contains `Timestorm`, but four series match. The primary series `35698` is explicitly `publishing_format = limited series` and contains issues 1–4. Their known on-sale dates run 2009-04-08 through 2009-08-26. Series `37419` (Spider-Man) and `37852` (X-Men) are explicitly `one-shot`, each with one issue. Spider-Man has on-sale date 2009-06-03; X-Men has no on-sale date and key date `2009-08-00`. The same-name series `233267` is explicitly a **collected edition**, issue `[nn]`, dated 2009-11-04. A name-only aggregation would therefore produce the wrong original-issue span.

The six original issues have six indexed comic-story sequences, all genre `superhero`; three have nonblank synopses (main issue 1, Spider-Man and X-Men) and three are blank. The inspected story/issue keyword rows supply only one tag, `Smithsonian`, attached to the X-Men story. This illustrates why copying every member keyword is not a dependable event-tag strategy. Series publishing formats provide grounded type suggestions, but a member one-shot does not classify the whole event as a one-shot.

The read-only verification used `sqlite_master`, `PRAGMA table_info`, small arc/link aggregates, indexed membership joins and bounded Timestorm series/story/issue queries; it did not scan/count all story content. These observations support design discussion only; no app behavior was changed.

### Later implementation package verification

After design/spec approval, the implementation's extractor built a separate schema-2 artifact from this snapshot. It contains 582 arcs, 142 descriptions and 3,416 deduplicated arc-to-issue rows, with series formats, date uncertainty and directional reprint evidence. The extract is 147.7 MB and its zip is 44.0 MB. ZIP CRC, SQLite `quick_check` and the manifest checksum passed. SHA-256: `ce55198ee234d341d4e10f3f34cbee84e3908f42e77773490f7b4cab46020167`.

Direct queries of the extract retain all six original Timestorm issues, X-Men's missing on-sale date and `2009-08-00` key date, and the distinct collected edition's format/reprint flag. The artifact and `gcd-data-v2.json` are prepared locally, not published; the existing schema-1 manifest was not replaced. This section records later implementation verification; the earlier no-code-change statements describe the research phase.

The verified zip, versioned manifest, verification report and attribution README are also saved outside Temp at `C:/Users/DeeDee/Downloads/Paperbunkr-GCD-v2-2026-09-15/`. The copied zip's SHA-256 matches the value above.
