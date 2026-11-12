# Series structure and discovery — design

Date: 2026-11-10. Source: "Smart features pitch 2" in `docs/Paperbunkr-Roadmap.md`, items #6 (arcs inside a series), #13 (creator runs),
#15 (character pages) and #18 ("more like this" without ML). The user added two requirements during the session: character, team and creator
images (Metron, with ComicVine as fallback), and links between these pages and the Continuity section. Grilled over three rounds; every
decision below was confirmed. Layouts confirmed from two mockups in the visual companion (v1: page and arc sections; v2: images, continuity
switch, Continuities/Events cards, continuity strips, chip images).

## 1. Scope

Four slices, built in order; D2 and D4 need D1's creator index.

| Slice | Items | Surface |
|---|---|---|
| D1 | #15 + images + Continuity links | Creator index rebuild; the entity page (character, team, location, creator, publisher); images; Continuity Overview strips; chip links |
| D2 | #13 Creator runs | Detail "Creator runs" grouping; creator page Runs card |
| D3 | #6 Arcs | Detail "Story arcs" grouping upgrade |
| D4 | #18 More like this | The shared recommendation engine (Detail rail, Home "Because you read", Insights) |

Plus two small CE parity additions: Library group-by Writer and by Main character or team (§7).

**Out of scope:** CE's column browser (`SearchBrowserControl`) — recorded in the roadmap as a parity gap. Story Event ordering for "Read in
order" (cover date is used). Metron's per-character `universes` field (continuity comes from series membership).

**ComicRack CE** (checked in `_reference/ComicRackCE`): CE has StoryArc, Characters, Teams, Locations and MainCharacterOrTeam fields and
groupers for all of them including Writer (`ComicRack.Engine/Metadata/ComicBook/Group/`). It has no character or creator pages, no arc progress
or arc reading lists, no run detection, and no similar-items feature: D1–D4 are deviations. Paperbunkr's Library group pool lacks CE's Writer
and Main character/team groupers; §7 adds them.

## 2. Foundations

**Code placement.** Detection and scoring are pure functions in `Paperbunkr.Data/Metadata` (next to `CharacterResolver`, `CreatorResolver`,
`RecommendationResolver`); fetch and cache services in `Paperbunkr.App/Services`; views in `Paperbunkr.App`.

**Schema** (one migration per slice that needs one; migration tests; hand-edit any generated `Down()` that drops an `AppSettings`/`Issues`
column):

| Slice | Change |
|---|---|
| D1 | On `Character`, `Team` and `Creator`: `ImageUrl` (string?), `ImageSource` (enum None/Metron/ComicVine/Custom), `ImageCheckedAt` (DateTime?), `CustomImagePath` (string?), `Description` (string?), `Aliases` (string?, comma list). |

D2, D3 and D4 add no columns (all derived on read).

**Reader queue.** "Read in order", "Read this run" and "Read this arc" open the first unread issue and walk the rest with the in-memory queue
from slice B of `docs/superpowers/specs/2026-11-10-smart-features-2-design.md` (plan correction 1). Build slice B first. "Save as reading
list" creates a normal reading list in the same order.

**UI rules.** The `paperbunkr-design-language` skill: tokens only, shared families, images as squircles (`PbRadius`/`PbRadiusLg`, never a
circle), `Border.card` sections, Reveal/Replace motion, the 1366×768 minimum. Lint set and `avalonia-pro-max/review-checklist` per slice.

## 3. D1 — Pages, images and Continuity

### 3.1 Creator index rebuild

`CreatorResolver.RebuildAll` (`Data/Metadata/CreatorResolver.cs:88`) has never been called, so libraries tagged only by ComicInfo or migrated
from CE can have almost no `CreatorCredit` rows. Run it once at startup behind a done-flag, the way the character index is backfilled
(`App/Services/PaperbunkrDb.cs:79-87`), off the UI thread. Then keep it in sync on scan and on Issue Properties / bulk edit saves, as
characters already are.

### 3.2 The entity page

Upgrade `MetadataEntityDetailScreenViewModel` (+ view), already routed (`MainViewModel.cs:1144` "metadataEntity", history replay :3186) but
unreachable (`GoMetadataEntityDetail` :2648 has no callers). One page for Character, Team, Location, Creator and Publisher; each section shows
only where it fits the kind.

**Entry points:** Detail band pills for writers, artists, characters, teams and locations (`DetailBandViewModel.cs:233-244`), the Details-tab
credit chips (`DetailTabsViewModel.cs:640-692`), and the reader info-panel chips (`ReaderScreen.axaml:320, 1002`, made clickable). All call
`GoMetadataEntityDetail` instead of `GoLibraryWithSearch`; the page's "Show in Library" runs the old search.

**Header:** image (3.3) 112×140, overline with the kind, name (`pbTextHeading`), "Also known as …" + counts (appearances, series, read),
a two-line description with "Show more", then the continuity switch (3.4) and buttons: Read in order (`primary`), Save as reading list
(`secondary`), Show in Library and Change image (`ghost`).

**Cards** (two-column grid of `Border.card`, one column below ~1000 px):
- **Continuities** — each continuity with its appearance count; opens the Continuity screen (`ContinuityScreenViewModel.LoadContinuity`).
- **Events** — story events with at least one member issue featuring the entity, ordered by `EventChronology`, with cover, year, continuity and
  issue count; opens the event page.
- **First appearance in your library** — the earliest by cover date (Year/Month/Day; undated issues last); cover, label, date, read state.
- **Series** — series with appearance and read counts, most appearances first; opens Detail.
- **Appearances by year** — a small bar chart (static, no sweep animation).
- **Unread appearances** — cover strip, in reading order.
- **Creator only:** Roles (counts per role) and Runs (D2).

**Read in order:** all appearances (narrowed by the switch) by cover date, ties by series sort name then `NumberSortKey`.

### 3.3 Images

- **Fetch on open:** when a page opens and `ImageCheckedAt` is null or older than 30 days, fetch in the background: the Metron record
  (`character/{id}/`, `team/{id}/`, `creator/{id}/`) when the entity has a Metron id in `ComicMetadataExternalId`, otherwise the ComicVine
  record when it has a ComicVine id (characters and teams from `character_credits`/`team_credits`, creators from `person_credits`). Store
  `ImageUrl`, `ImageSource`, `Description`, `Aliases`, and stamp `ImageCheckedAt` (a miss is stamped too; a failure leaves it unset so the
  next open retries). Modelled on `StoryEventDetailsCompletion.CompleteAsync`.
- **Rate limits:** Metron calls go through `MetronHttp` (spacing, per-minute budget, background share) and `MetronQuota` (daily reserve) at
  Low priority; ComicVine through its existing client throttle.
- **Background fill:** a scheduled task "Fill character images" (Low priority, about 20 entities per run, entities with an id and no check
  stamp first, most appearances first), pattern `ContinuityUniverseSweep`. Stops on a provider error.
- **Cache:** download to an on-disk cache keyed by `SHA256(url)` (temp file, decode check, move), the `StoryEventCoverCache` pattern; a custom
  image is stored under the entity's own key.
- **Kinds without images:** a publisher page shows its `BrandMark` (the existing logo resolver); a location page shows initials. Neither is
  fetched.
- **Display order:** `CustomImagePath` > cached provider image > the first appearance's cover, cropped to the 4:5 tile > initials on
  `PbAccentSoftBrush`.
- **Change image:** pick a file or an issue cover (the existing cover picker); sets `CustomImagePath` and `ImageSource` Custom; "Reset image"
  returns to the provider image.
- **Provider-schema check (resolved in §9 from authoritative server source, without authenticated requests):** that Metron's character/team/creator records
  carry `image`, `desc` and `alias`; whether Metron credits carry a creator id (the code comment at `MetronClient.cs:628` and the 2026-09-20 spec
  disagree). Record the answers in §9. ComicVine covers whatever Metron lacks.

### 3.4 Continuity links

- **Continuity switch** on the page: All plus each continuity the entity appears in (`segGroup`, or a `SuggestBox` when more than 4). An
  appearance belongs to a continuity when its series has a `ContinuityMembership` there. The switch narrows every card, the counts and Read in
  order. Not persisted.
- **Continuity › Overview:** two new strips, "Key characters" and "Key teams" — the top 10 by appearances in the continuity's member series,
  as image tiles (72×88) with name and count; a click opens the entity page with that continuity selected. Hidden when empty. Computed with the
  rest of `ContinuityOverview`.
- **Series Detail › Related:** the Continuity chips (`DetailTabs.axaml:455-472`) open the continuity (they only had Remove).
- **Detail chips with images:** character and team chips show a 20 px squircle image once one is cached (no fetch from the chip itself).

### 3.5 Tests (D1)

Creator rebuild runs once and syncs on edit. Entity page: each card's query, the switch narrowing, Read in order ordering, kind-specific
sections. Images: Metron → ComicVine → first-appearance → initials order; 30-day recheck; miss stamped, failure not; custom wins; background
task batch size and order. Continuity strip counts and hide-when-empty. Navigation from every entry point. Headless render in three themes;
keyboard reach in the real window.

## 4. D2 — Creator runs (#13)

### 4.1 Detection

`CreatorRunDetector.Find(seriesIssues, role)` for Writer and for Penciller/Artist (one combined artist role):
- Walk the series' issues by `NumberSortKey` (placeholders for unowned numbers count as neither breaking nor extending).
- A run is a maximal stretch where one creator is credited in that role, allowing up to 2 consecutive owned issues without them ("fill-ins").
- Keep runs of 6 or more of that creator's issues. A creator can have several runs in one series.
- Output: creator, role, first and last number, issue count, read count.

### 4.2 Where runs show

- **Detail › Issues:** a new grouping option "Creator runs", offered only when the series has at least two runs, or one run plus issues outside
  it (so single-creator manga never gets it). Section header "Claremont · #94–#279 · 140 of 180 read" with a progress bar, Read this run
  (`secondary`, `primary` on the first section) and Save as reading list (`ghost`). Issues in no run go under "Other issues". The existing
  "Run" grouping (per volume) keeps its name and stays the default.
- **Creator page › Runs:** every run of that creator across series; a run covering a whole series shows as that series.

### 4.3 Tests (D2)

Detector table: clean run, fill-ins at 1/2/3, unowned gaps, below 6, two runs in one series, writer vs artist, whole-series manga. Offer rule
for the grouping. Read this run uses the queue in order.

## 5. D3 — Arcs (#6)

### 5.1 Detection

`SeriesArcDetector.Find(seriesIssues, contentType)`, sources in order of trust (an issue takes the first that applies):
1. **Story Arc field** — each comma-separated arc name; order within an arc by `StoryArcNumber`, else issue number. An issue in two arcs
   appears in both.
2. **Titles** (comics only) — patterns "Part N of M", "Part N", "Book N:", "<Arc name>, Part N", "<Arc name> Part N" (numbers as digits or
   words up to twenty); the arc name is the text before the part marker, or the shared title prefix of the neighbouring issues.
3. **Shared prefix** (comics only) — two or more consecutive issues whose titles share the text before ":" or " Part".
- "Chapter N" counts only as part of a shared-prefix arc (source 3), never alone. For manga only source 1 is used.
- Issues with no arc go to "Other issues".
- Display only: nothing is written to `StoryArc` or proposed.

### 5.2 On Detail

The existing Story Arc grouping (`DetailGrouping` via a Library group field, `DetailListLayoutState.cs:79`) is replaced, under the same option
name "Story arcs", by detector-driven sections: header with arc name, "#7–#12 · 4 of 6 read · from the Story Arc field" (or "from titles"),
progress bar, Read this arc and Save as reading list; "Other issues" last. Ordered by each arc's first issue. The default grouping is
unchanged.

### 5.3 Tests (D3)

Detector table: multi-arc field, StoryArcNumber order, each title pattern including words-as-numbers, shared prefix, lone "Chapter N" ignored,
manga uses the field only, precedence when sources disagree. Grouping shows sources and progress.

## 6. D4 — More like this (#18)

Changes the one shared `RecommendationResolver` (`Data/Metadata/RecommendationResolver.cs`), so the Detail Related rail, Home "Because you
read" (with its existing dismissals) and Insights "Because you finished" all change together.

- **Candidates:** any owned series sharing at least one of: creator (via `CreatorCredit`), character, genre, tag, publisher or imprint with the
  seed — not only series linked by relation/continuity/event/collection. Pre-rank by count of shared signals and keep the top 200.
- **Scoring** of those 200:

| Signal | Weight | Measure |
|---|---|---|
| Shared creator | 0.20 | overlap of credited creators (writer and artist roles weighted ×2) |
| Shared characters | 0.15 | Jaccard on `CharacterAppearance` (both `,` and `;` split, via the index rather than raw strings) |
| Direct relation | 0.15 | existing MediaRelation signal |
| Weighted tags | 0.10 | overlap weighted by tag weight (the weighted-tags model, 2026-10-14) |
| Genre | 0.10 | Jaccard |
| Continuity | 0.10 | shared continuity membership |
| Publisher / imprint | 0.05 | same imprint 1.0, same publisher 0.5 |
| Era | 0.05 | start years within 5 → 1.0, falling to 0 at 15 |
| Event | 0.05 | shared story events |
| Collection | 0.05 | shared collections |

  Weights are a starting point; tune against tests and record the final values in §9.
- **Explanation:** the top two or three contributing signals in words, joined with " · " ("same writer · 4 shared characters · same
  imprint").
- **Performance:** computed off the UI thread; Detail opens without waiting and the rail fills when ready. A perf test on a 10k-issue fixture
  bounds the run.

### 6.1 Tests (D4)

Candidate pool includes unlinked series; pre-rank cap at 200; each signal's measure; weights sum to 1; explanations pick the top signals;
existing dismissals still respected; perf bound.

## 7. CE parity: Library group-by Writer and Main character or team

Add `Writer` and `MainCharacterOrTeam` to `IssueListGroupField` and register them in `App/Models/IssueListFieldCatalog.cs`. Both group on
the complete field string, matching CE's `ComicBookGroupWriter` / `ComicBookGroupMainCharacterOrTeam` and `SingleGrouper.IsMultiGroup = false`.
The earlier interpretation of `ComicBookStringGrouper`'s `ListContains` matcher as multiple groups was incorrect: that matcher is for searches.
Tests: grouping counts on a multi-writer fixture, retaining one combined Writer group. The separate CE column browser is multi-value.

## 8. Roadmap note

Record CE's column browser (`SearchBrowserControl`: Writers, Characters, Teams, Main Characters/Teams, Story Arcs columns) as a parity gap in
`docs/ce-feature-inventory.md` / the roadmap.

## 9. Implementation notes

**Implemented 2026-11-10, uncommitted working tree at base HEAD `28e60a1`.** Step 0, D1–D4 and §7 are implemented. The continuation
finished Claude's partial D1.5 services, repaired the queue-aware test overload and completed the remaining pages, groupings and integration.
The user's request to finish the whole spec superseded the plan's pauses between slices. Existing unrelated working-tree changes remain.

- **Index/schema:** `AddEntityProfiles` also includes `AppSettings.EntityIndexVersion`, rather than a second migration. The four-index rebuild
  is a default-enabled daily Activity Center task, doing work only while its version is behind. This replaces the synchronous character-only
  startup backfill. Scan, rescan, properties and bulk properties synchronize all four indexes. Cancellation leaves the version unstamped;
  rebuilding clears stale entries for blank fields. Pruning retains custom-image entities.
- **Profiles and images:** per-field fallback prefers Metron, with ComicVine filling missing bio/aliases/image. A successful lower-priority
  provider does not replace a higher-priority field. Misses are stamped; provider/image errors retry. Custom-image display provenance survives
  provider refresh. Page loads run on fresh worker contexts with cancellation and generation guards, and profile completion refreshes only the
  current entity. Small cached thumbnails decode on workers and dispose on detach/replacement. No chip initiates a provider fetch.
- **Provider evidence:** inspected the authoritative Metron server serializers (2026-11-10), rather than spending the user's API quota.
  [Creator serializer](https://raw.githubusercontent.com/Metron-Project/metron/master/api/v1_0/serializers/creator.py) exposes profile
  `desc`, `image`, `alias`; `CreditReadSerializer.id` is explicitly `creator.id`, while `role[].id` identifies the role.
  [Character serializer](https://raw.githubusercontent.com/Metron-Project/metron/master/api/v1_0/serializers/character.py) exposes those
  three profile fields. [Team serializer](https://raw.githubusercontent.com/Metron-Project/metron/master/api/v1_0/serializers/team.py)
  exposes description/image but no alias, so ComicVine can fill aliases. Credit parsing now preserves the creator ID separately from role IDs,
  including unmapped roles. Canned fetch and persistence tests cover the distinction. No authenticated live-provider smoke test was run.
- **Entity page:** date/natural-number ordered unique appearances, read/unread counts, year bars, roles, creator runs, chronological events,
  series and continuity cards; All/segments/picker keeps every continuity choice while narrowing content. Ordered queues start at the first
  unread available issue; Save creates a normal User reading list in exactly that order. Publisher pages keep their published-series list,
  including empty series, and use BrandMark; locations use initials. Custom images reuse the file/cover picker and reset to the fallback chain.
- **Navigation:** band, credit and reader chips resolve kind-scoped names before navigation, including non-ASCII case matching; missing indexed
  names retain Library search. History retains the canonical entity name and selected continuity. Related continuity chips and Overview's
  top-ten character/team tiles navigate directly; cached chip images are 20 px squircles.
- **Structure:** shared headers provide progress and ordered Read/Save actions in every comic layout and manga chapter lists. Creator runs
  allow up to two interior owned fill-ins, require six credited issues and ignore placeholders; identical writer/artist membership for the
  same person is one run. The grouping is offered only when useful. Comic title inference never writes metadata; manga uses explicit arcs.
  Same-name explicit/title arc segments merge case-insensitively, with a mixed-source label and deduplicated ordered issues. Old Detail
  `StoryArc` layouts normalize to `arcs`; Library keeps its original plain Story Arc grouper. Defaults remain volume-based.
- **Final recommendation weights:** creator .20; characters .15; relation .15; tags .10; genre .10; continuity .10; publisher/imprint .05;
  era .05; event .05; collection .05 (sum 1). No additional tuning was justified by the deterministic fixtures. Creator and tag measures use
  weighted Jaccard (sum of minimum weights / sum of maximum weights); writer/penciller/artist weigh 2, other roles 1. Tag weights are .25/.5/.75/1,
  with Unset .5. Characters and genres use Jaccard. Relations retain their evidence/direction. Candidates require an owned non-placeholder issue;
  shared content plus existing relation/continuity/event/collection anchors are pre-ranked by distinct signals, then capped at 200 before full
  scoring. Edited issue publisher/imprint (dominant value) and earliest positive year take precedence over legacy series metadata. Explanations
  show up to three weighted contributions. Detail, Home and Insights already compute on workers; Home dismissal behavior is retained.
- **CE parity:** Writer and Main character/team group the complete string, matching CE's single grouper. CE's independent multi-value column
  browser remains an explicitly documented, unscheduled gap.

### Verification

Final expanded selections passed **103/103 Data** and **394/394 App** cases. After the last progress-bar token correction,
**49/49 UI/render/style/lint cases** passed again against the rebuilt assembly; the updated render confirms the skin accent. Earlier foundation checks passed 50 Data and 189 App cases, including
scan/edit integration; expanded data checks passed 103/103, including the 10,000-issue recommendation performance bound (<10 seconds for
resolution). Checks use temporary SQLite databases and fake provider handlers. No full suite, user-database migration or commit was performed.
Builds use `RunAnalyzers=false`; App tests exclude `Speed=Slow`. One known pre-existing lint case, `FamilyButtonsCarryNoSizeOverrides`, is
excluded because four Height overrides remain in the untouched two scraper dialogs. Other selected XAML/color lint and shared-style checks
are included. Existing compiler/package/obsolete-API warnings remain.

Headless renders cover Default, Daylight and Matrix at 1366×768, plus the entity page at 1000×768. Real MainWindow tests cover keyboard
refresh and history back. The Avalonia review checklist was applied to tokens, shared styles, reflow, image lifetime and keyboard
integration (including explicit accent/surface tokens on structure progress bars);
native Narrator, OS high-contrast, 150/200% DPI, DevTools profiling and macOS/Linux manual checks remain release validation, not claimed here.

**Follow-up fixes, 2026-11-10 (after the user's first look).**
- *Index never built in practice.* The user's library (9,387 issues) still had `EntityIndexVersion` 0: the per-issue rebuild saves and prunes once
  per issue per index, so it never finished a session, and the second-most-credited artist was missing from the index. New `EntityIndexBulk`
  does the sync set-based (one text load and one diff per kind, batched writes, one prune); scans, editors, undo, re-read and the rebuild task
  all use it. Pruned creators and locations now drop their provider ids too, as characters and teams already did.
- *Clicking a writer/artist opened the Library.* `EntityNameLookup.FindOrIndex` indexes just the issues mentioning an unindexed name, off the UI
  thread, then opens the page; only a name no issue mentions falls back to Library search. `Find` no longer reads the whole table on an ASCII miss.
- *Writer/artist images missing on Detail.* Chips read their image once at build time, and the Detail screen is kept alive while visiting a page,
  so an image that arrived later never showed. Chip images are now observable, filled in one batch off the UI thread, and refreshed through
  `EntityChipImages.Changed` (raised when a profile image downloads and when a custom image is set or reset).
- *Large lists.* The cover picker's three candidate grids used a non-virtualizing `WrapPanel`; on an entity page every appearance is a candidate.
  They now use `VirtualizingWrapPanel` (72×102 cells). The page's other lists were already virtualizing and height-capped.

**Follow-up fixes, 2026-11-11 (user screenshot of a creator page).**
- *Scrollbar over the counts.* The cards' list rows ran to the list edge, under the overlay scrollbar (the year counts were covered). Rows now keep
  a 12 px right margin.
- *Empty Runs card.* With the page already on screen when a creator loaded, the Runs list's inherited-DataContext binding stayed on null (its
  `DataContextNode` never saw the page; the sibling Roles list in the same layout updated fine). A headless test reproduces it; the Avalonia-level
  root cause was not found. The list now binds through the view's own `DataContext` (`$parent[...]`) and is a plain height-capped list; the run
  button reads "Read this run". The VM property is `CreatorRuns`.

**Manga creator and location images, 2026-11-11 (user-approved design).**
- *Manga creators.* After Metron and ComicVine, a creator is matched against the staff of the series they are credited on that are linked to AniList,
  then MangaDex (`ExternalMediaIds`; at most 3 series per provider, most credited first). No open search by name; two different people with the same
  name match nothing. Names compare ignoring case, spacing and punctuation, and a two-part name also in reversed order ("Oda Eiichiro" / "Eiichiro
  Oda"); AniList native and alternative names count and become aliases. AniList's `default.jpg` placeholder counts as no image. The match is redone
  at each 30-day check; no AniList/MangaDex ids are stored (the external-id table knows only ComicVine and Metron). `EntityImageSource` gained
  `AniList` and `MangaDex`. `MangaStaffProfiles` holds the parsing and matching; the background fill also picks creators on linked manga series.
- *Locations.* `Location` gained the six profile columns (`AddLocationProfiles`) and implements `IHasEntityProfile`; ComicVine `location/4020-{id}`
  supplies image, deck and aliases (Metron has no locations). Location pages show and change images, location chips show them, the background
  fill includes them, and a location with a custom image is never pruned.
- Not checked against the live AniList, MangaDex or ComicVine services; tests use canned replies.

**Fix, 2026-11-12: page lookups starved after a big scrape.** The entity page asked Metron and ComicVine at Low priority. ComicVine's shared limiter
gives Low only 150 of each hour's 200 calls and holds it behind recent foreground (scrape) traffic; Metron's Low client refuses near the daily
reserve. Right after the user scraped many comics, every page lookup queued or was refused and was cancelled on leaving the page, so nothing filled
in (the page data itself loaded fine; one live High-priority call per provider from a copy of the library answered in ~1.5 s). The page now passes
`ComicVineRequestPriority.High` (`EntityProfileCompletion(..., priority:)`); the daily image fill stays Low. A failed lookup shows a one-line note on
the page instead of staying silently empty.

**Fix, 2026-11-12 (later): named entities that never filled in.** Checked against a copy of the user's library for 12 names they listed:
- *Had ids, still empty* (Steel, Supergirl, Omni-Man, Viltrumites): live provider calls answered in ~1 s, but the ComicVine image chosen was
  `original_url` (Supergirl's: 1.9 MB) and the download gave up after 15 s on a slow connection, so nothing was stored or stamped. ComicVine
  now prefers `medium_url`, then `super_url`, `screen_large_url`, `original_url`; the download timeout is 45 s; any download error (not a caller
  cancel) is "not downloaded", and a download that throws inside `CompleteAsync` is a failure, not an escaping exception.
- *No provider id* (ids are only recorded on scrape, since 2026-09-23): when an entity has none, `CompleteAsync` looks up up to two of its own
  issues that have a Metron (then ComicVine; ComicVine only for locations) issue id and runs `ComicMetadataExternalIdSync.SyncFromIssueDetails`,
  which attaches ids for every name on that issue. On the copy this linked Sam Humphries, Eduardo Pansica and Tom Brennan via Metron. King in
  Black, Empty Throne, Venom Horse and United Planets Corps are not listed on Metron's issue records (likely from older ComicVine tags) and
  Olgrun has no issue with a provider id; for these the page now says it is not linked yet (`EntityProfileOutcome.NoSource` note).
