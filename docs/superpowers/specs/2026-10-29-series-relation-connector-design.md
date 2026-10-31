# Series relation connector — design

Date: 2026-10-29. Status: agreed in a grilling pass (Q1–Q18), not built.

## 1. Problem

The Related tab on a series page is almost always empty. On the live library (2026-10-29): 1,021 series, 8,385 issues, and **6**
series-to-series relations (5 made by hand, 1 from a GCD bond). The data needed to fill it is already in the library — volumes of the
same title, Annuals and Specials, slash titles, 229 story events touching 577 series — and Metron names companion series for most of
the library, but nothing turns any of that into relations.

This design adds a **series connector**: a sweep that works out series relations from the library and from Metron, saves the strong
ones as automatic relations, and offers the weak ones as suggestions on the Related tab.

**CE parity:** ComicRack CE has no series relations at all. It only groups and sorts by `AlternateSeries` and `SeriesGroup`
(`ComicBookGroupAlternateSeries`, `ComicBookSeriesGroupComparer`). Nothing here changes how those fields behave. This is Paperbunkr's own
feature, built on its existing relation graph (`MediaRelation`, `RelationEvidence`; docs/superpowers/specs/2026-08-17-metadata-model-phase3-media-relations-design.md).

## 2. What counts as related (Q1)

Three kinds of relation, all stored as `MediaRelation` rows between two series:

1. **Lineage** — one series continues another (later volume of the same title).
2. **Companion** — a series that sits beside another: Annuals, Specials, slash-title team-ups, small crossovers, Metron's associated series.
3. **Event tie-ins** — a series with issues in a story event, linked to that event's main series.

"Same Continuity", "Same Collection" and "More Like This" stay as they are.

## 3. Signals

Every rule produces candidates `(source, target, type, confidence, reason)`. Confidence decides the tier (Q2):

- **≥ 0.75: automatic.** Saved as a relation with evidence.
- **0.5–0.75: suggestion.** Shown on the Related tab, never saved unless you accept it.

### 3.1 Curated sources (automatic)

| Source | Rule | Type | Confidence | Evidence |
|---|---|---|---|---|
| GCD bonds | Unchanged (`GcdBondSync`) | Continuation / Reboot / Related | 0.95 | `Gcd` |
| Metron associated series (Q7) | Each `associated` entry that maps to a local series through `ComicMetadataExternalId` (`EntityKind.Series`, `ComicProvider.Metron`) | `Related` | 0.85 | `Metron`, `ProviderRelationType = "associated"`, `ProviderSourceId` = Metron series id |
| Manga providers | Unchanged (`ExternalMediaRelation` already turns into `MediaRelation` once both series are local) | provider's own | provider's own | provider's own |

Metron's associated entries that aren't in the library stay as `SeriesAssociation` rows only. They are not shown in this pass.

**Using Metron's ComicVine and GCD ids.** Every Metron series record carries `cv_id` and `gcd_id` (already read by
`MetronClient.GetSeriesInfoAsync`). Both ends of the Metron step use them, so series scraped from ComicVine, or never scraped but
GCD-matched, are reached too:

- **Finding a local series' Metron id.** The fetch only asks Metron about series it can name. Today 696 series have a Metron series id
  (`ComicMetadataExternalId`, `EntityKind.Series`, Metron). A series that has none but has a ComicVine series id
  (`ComicMetadataExternalId`, ComicVine) is resolved with `FindSeriesByComicVineIdAsync` (`series/?cv_id=`). The single unambiguous
  hit is saved as the series' Metron id, in the same table the scraper uses, so it's looked up once. A series with only a GCD id
  (`Series.GcdSeriesId`) is resolved the same way through a `gcd_id` filter, **but only if Metron's series endpoint accepts it** (the plan
  checks this against the API before relying on it; if not, GCD-only series are skipped).
- **Matching an associated series to the library.** An `associated` entry gives only a Metron id and a name. Match order:
  (1) a local series with that Metron id, (2) a local series with the same ComicVine series id, (3) a local series with the same
  `GcdSeriesId`. Steps 2–3 need the associated series' own `cv_id`/`gcd_id`. They come from one `GetSeriesInfoAsync` call per associated
  id that step 1 didn't match, and are stored on the `SeriesAssociation` row, so each is asked once.
- More than one local series with the same id (rare; split series) → no match, rather than a guess.

### 3.2 Local inference (automatic)

All of these use `TitleNormalizer.NamesMatch` for title comparison and a series' start/end years from its issues.

| # | Rule | Example | Type (source → target) | Confidence | Reason text |
|---|---|---|---|---|---|
| L1 | **Volume chain** (Q4). Same normalized title, same publisher, runs don't overlap. Each volume links only to the volume immediately before it. | Superman (2018) → Superman (2023) | `Continuation` (newer → older, same direction as GCD) | 0.9 | "Later volume" |
| L2 | **Companion suffix.** The title is a base title plus a companion suffix, with the same publisher and overlapping years. Suffixes: Annual, Special, Rebirth, Secret Files (and Origins), 80-Page Giant, Giant-Size (prefix), Sketchbook, Director's Cut, FCBD / Free Comic Book Day. The base is the volume of the base title whose years overlap most. | Batman Annual (2016) → Batman (2016) | `SideStory` (companion → base) | 0.85 | "Annual" / "Special" / … (the matched suffix) |
| L3 | **Slash title.** The title splits on `/` into 2–3 names, each matching an existing series. Each named series links to the slash series, using the volume whose years overlap most. This is the only local rule that may cross publishers (Q5). | Avengers/X-Men → Avengers, X-Men | `Crossover` (symmetric) | 0.8 | "Team-up title" |
| L4 | **Small crossover.** A story event spans exactly 2–3 series and has no main series (§3.4 step 3). Events, not the raw `Issue.StoryArc` text: arcs already become `StoryEvent`s through the story-event resolver, and using both would count them twice. | Part 1 in A, part 2 in B | `Crossover` (symmetric) | 0.8 | "Crossover · {event name}" |
| L5 | **Event tie-in** (Q8, Q11). See §3.4. | Batman → Dark Nights: Metal | `TieIn` (tie-in → main series) | 0.8 | "Tie-in · #33–34" |

There is **no** collected-edition rule (Q14). Trades already merge into their ongoing series at scan time (`LibraryFolderScanner`), and
nothing records which issues a trade collects, so a local "Collected from" would be a guess.

### 3.3 Suggestions only

These are worked out when the Related tab opens (§6.3), never saved. At most **8 per series**, highest confidence first.

| # | Rule | Type | Confidence | Reason text |
|---|---|---|---|---|
| S1 | **Shared lead character.** A character appears in ≥ 50 % of the issues of both series (lead, not cameo). Same publisher. | `SharedCharacters` | 0.6–0.7 (higher share → higher) | "Shares {character} as lead" |
| S2 | **Same writer.** A writer credited on ≥ 3 issues of each series, with overlapping years. | `Similar` | 0.55 | "Same writer · {name}" |
| S3 | **Spin-off.** Series B's title contains a character name; that character appears in series A in issues before B's first issue; B started later. | `SpinOff` (B → A) | 0.65 | "{character} first appeared here" |
| S4 | **One title contains another.** The base title is contained in the longer one (not already covered by L2). Same publisher. | `Related` | 0.5 | "Similar title" |

### 3.4 Event tie-ins

For each `StoryEvent` whose member issues span **two or more series**:

1. **Find the main series** with the existing spine logic: your pick (`SpineSeriesId`) if you made one, otherwise the member series whose
   name matches the event's. Never the series with the most issues. `SpineSeriesId = 0` ("no spine") means no main series.
2. **If there is a main series:** every other member series gets one `TieIn` relation to it. The evidence records the event id
   (`ProviderSourceId`) and the member issue range (in the reason, e.g. "Tie-in · #33–34").
3. **No main series, 2–3 member series:** rule L4. The series are linked to each other as `Crossover` (3 relations at most).
4. **No main series, 4+ member series** (Q9): no relations. The Related tab shows the event as a single card (§6.2).

Single-series events (128 of 229 today) produce nothing.

A series that is a tie-in to several events gets one `TieIn` relation per event's main series. If two events share a main series, the
pair has one relation with one evidence row per event.

**Moving the spine logic:** `SpineResolver.FindNameMatch` lives in `Paperbunkr.App/Services/EventMap/`. Its name-matching part moves to
`Paperbunkr.Data/Metadata/EventSpineResolver.cs` so the sweep (in Data) and the Event Map share one implementation. `SpineResolver`
keeps its current signature and calls the moved code. Nothing about how the Event Map behaves changes.

### 3.5 Guardrails (apply to every automatic rule and every suggestion)

- **Publisher:** local rules L1, L2 and S1–S4 need the same publisher (`PublisherEntityId`, falling back to the `Publisher` text). L3, L4,
  L5 and curated sources may cross publishers (Q5).
- **Content type:** no inferred link between a comic series and a manga or book series. Provider relations (e.g. Adaptation) are unaffected.
- **Split series:** L1 needs the two runs not to overlap, so two halves of one split-up series never get "Continuation". Fixing them
  is Library Health's job.
- **Your own relations win:** if a pair has any relation with `User` evidence, no automatic relation or suggestion is made for that pair.
- **Dismissals stick:** a pair in `SeriesRelationDismissals` gets no automatic relation and no suggestion, from any rule or source.
- **Stale relations go:** an automatic relation whose evidence no longer holds (the event lost the issue, a series was renamed, Metron
  dropped the association) loses that evidence row. It's deleted when no evidence is left. Same rule as `GcdBondSync` and `EventConnectorSweep`.
- **One row per pair and type:** a relation already there, in either direction and of the same type, just gains an evidence row.

## 4. Data model changes

One migration, for the new columns below.

- **`SeriesAssociation.ComicVineSeriesId`, `SeriesAssociation.GcdSeriesId`** (`int?`): the associated Metron series' own `cv_id` /
  `gcd_id`, filled the first time it's needed for matching (§3.1). Null means not looked up yet or Metron has none. The row also gets
  `IdsCheckedUtc` (`DateTime?`), so "Metron has none" isn't asked again for 30 days.

- **`RelationType`**: add `TieIn` and `MainEventSeries`. The column is stored as text (`HasConversion<string>()`, max 32), so this needs
  no schema change. They still go at the end of the enum, so the plugin-facing int values of existing members don't shift.
  `RelationTypeCatalog`: a directional pair, each the other's inverse. The stored row is "source is a tie-in to target". From the main
  series' page the card reads **"Tie-in"**; from the tie-in's page the main series' card reads **"Main event"**. Both are added to
  `RelationTypeOption` labels and to `ContinuityMapLoader.RelationLabel`.
- **`RelationEvidenceProvider.Inferred`** is reused for L1–L5 (the reason goes in `ProviderRelationType`, as the event connector does).
  For L5, `ProviderSourceId` is the `StoryEvent.Id`. **`Metron`** is reused for associated series.
- **`Series.MetronAssociationsCheckedUtc`** (`DateTime?`): when Metron's associated list was last fetched for this series. Null means
  never. Needed because a series with no associations leaves no `SeriesAssociation` row to show it was checked.
- **`AppSettings.FindRelatedSeriesAutomatically`** (`bool`, default `true`) — Q17.
- **`SeriesRelationDismissal`**: no schema change. Its meaning widens from "deleted a GCD relation" to "deleted any automatic relation or
  dismissed a suggestion". The doc comment is updated.

## 5. The sweep

### 5.1 Units (all in `Paperbunkr.Data/Metadata/SeriesConnector/`)

Each inference rule is a pure function over a read-only snapshot of the library, so each can be tested alone:

| Unit | Does |
|---|---|
| `SeriesConnectorSnapshot` | Loads once, with no tracking: series (id, normalized name, publisher, content type, start/end year), per-series issue counts, event memberships with series and issue numbers, user-backed pairs, dismissed pairs. |
| `VolumeChainInference` | L1 |
| `CompanionSuffixInference` | L2 (suffix list as a static table) |
| `SlashTitleInference` | L3 |
| `EventTieInInference` | L4 + L5 (uses `EventSpineResolver`) |
| `SeriesRelationSuggestions` | S1–S4, for one series, at page-open time (§6.3) |
| `MetronAssociationFetch` | Resolves missing Metron series ids from ComicVine (and GCD, if supported) ids; fetches `associated` for series due a check and writes `SeriesAssociation` via the existing `SeriesAssociationSync`, stamping `MetronAssociationsCheckedUtc`; then fills `cv_id`/`gcd_id` on associations that don't match a local Metron id |
| `SeriesConnectorSweep` | Runs the local inferences + maps `SeriesAssociation` rows to local series, applies the guardrails, then does one upsert/prune pass for `Inferred` and `Metron` series-to-series evidence. Returns `SeriesConnectorSummary(Created, Updated, Removed)`. |

The upsert/prune pass follows `EventConnectorSweep.Upsert` and `GcdBondSync.Run`. It only touches evidence from `Inferred` and from
`Metron` with `ProviderRelationType = "associated"`. GCD, User, Wikidata and event-order Metron evidence are left alone.

### 5.2 When it runs (Q6, Q18)

- **Scheduled task "Find related series"** in `ScheduledTaskCatalog`: weekly interval, `DefaultEnabled: true`,
  `SchedulerResourceClass.Network`, `ActivityJobKind.SyncMetadata`, priority 19 (after GCD matching at 18, so new GCD bonds are already
  in). It runs the local inferences first (seconds), then `MetronAssociationFetch` for series never checked or checked more than 30 days
  ago. The fetch respects Metron's rate limiter, so the first run takes about 25 minutes for ~700 series and later runs are small. Then
  it runs the sweep again so the new associations land. Like every scheduled task, it shows in the Automation tab and the Activity Center.
- **After a library scan:** subscribe to `LibraryEvents.LibraryScanCompleted` (as `ScrapeOnImport` does) and run the local part of the
  sweep in the background, with no Metron calls.
- **After a scrape:** `LibraryEvents` has no scrape-completed event today. Add `ScrapeCompleted` (series ids touched), raise it where a
  scrape job succeeds, and handle it like a scan.
- **"Find relations" button** on the Related tab's "manage" area: runs the whole sweep as a normal Activity Center job.
- **Off switch:** when `FindRelatedSeriesAutomatically` is false, the scan/scrape triggers do nothing and the scheduled task and button
  report "Turned off in Preferences". Automatic relations already saved are kept, and suggestions stop showing.

### 5.3 Reporting (Q15)

Each run ends with an Activity Center summary in the event connector's wording: **"14 relations found · 2 removed"**, or **"No new
relations"**. The link opens the Library. Scan- and scrape-triggered runs report only when something changed, so a normal scan doesn't add noise.

## 6. Related tab

`DetailTabs.axaml` Related section, reusing `PosterRail` and `Border.card` (design language: shared families only, tokens only, 4 px grid).

### 6.1 Related rail (lineage + companion)

The existing **Related** rail shows every relation that isn't `TieIn`/`MainEventSeries`: your own, GCD, Metron, L1–L4. Each card's
`SubLabel` (Q16) carries the type and source:

- automatic: "Later volume", "Annual", "Team-up title", "Crossover · Dark Reign", "Metron", "GCD"
- your own: the relation type label, as today

Removing an automatic relation from this rail deletes it and writes a `SeriesRelationDismissal` for the pair, so the sweep doesn't put it back.

### 6.2 Event rows (replaces "Same Event") — Q13

The flat **Same Event** rail (`RefreshSameEvent`, `EventMembershipResolver.GetOtherSeriesInSharedEvents`) is **replaced**. It lists the
same series, with no main series or tie-in shape and no issue ranges.

- **One `PosterRail` per event with a main series** that this series is part of. Title = event name. `ContextLabel` = "{n} series ·
  open event", which opens the event page. Cards: the main series first (SubLabel "Main event"), then the tie-in series in reading order
  (SubLabel "#33–34"). The current series' own card is left out.
- Rows are ordered by event start date, newest first. The first **3** show; a `Button.ghost` "Show all {n} events" reveals the rest
  (`controls:Reveal.IsOpen`).
- **Events with no main series and 4+ series** (Q9) are gathered into one rail, **"Other events"**, with one card per event. The card
  shows the event cover (or its brush), the name, and SubLabel "{n} series", and opens the event page.

Rows are built from stored `TieIn` relations grouped by their evidence's event id, plus `EventMembership` for the "Other events" rail.

### 6.3 Suggestions (Q3)

`SeriesRelationSuggestions` runs when the Related tab loads, off the UI thread, the same way the "More Like This" rail is filled.
Suggestions are appended to the **end of the Related rail**:

- `PosterRailItem` gains `IsSuggestion` (bool). The card gets a `.dimmed` state style (allowed by the design language: a state style, not a restyle).
- `PosterRail` gains `AcceptCommand`. A suggestion card shows two `Button.iconButton sm` controls on hover/focus, with
  `AutomationProperties.Name`: **Accept** (`Checkmark`) and **Dismiss** (`Dismiss`).
- SubLabel = the reason ("Shares Superman as lead").
- **Accept** creates the relation with `User` evidence (confidence 1.0) and the suggested type. **Dismiss** writes a
  `SeriesRelationDismissal`. Both close/remove the card deferred one dispatcher tick (`Dispatcher.UIThread.Post`), since the
  button that raised the command is inside the row being removed (CLAUDE.md runtime gotcha).

Clicking a suggestion card opens that series, like any card.

### 6.4 Preferences

Preferences › Behavior gets one toggle, **"Find related series automatically"**, with the description "Links later volumes, annuals,
team-ups and event tie-ins, and suggests other related series. Relations you made yourself are never changed." It binds
`AppSettings.FindRelatedSeriesAutomatically`.

## 7. Plugins

No API change. Tie-in and inferred relations are ordinary `MediaRelation` rows, so `IMetadataGraph` (`PaperbunkrMetadataGraph`) exposes
them already. Plugins see the two new enum values through the existing relation-type mapping. The plan checks that the mapping passes
unknown values through rather than throwing.

## 8. Testing

- **Data tests** (`Paperbunkr.Data.Tests/SeriesConnector/`), one class per inference unit, using in-memory SQLite fixtures:
  - L1: chains only adjacent volumes; overlapping runs never link; a different publisher never links.
  - L2: each suffix; picks the overlapping base volume; no base series → nothing.
  - L3: 2- and 3-name slashes; a name with no local series is skipped; crosses publishers.
  - L4/L5: user spine beats auto spine; spine 0 → crossover or nothing; 2–3 vs 4+ series; issue-range reason text; single-series events
    produce nothing; two events with the same main series → one relation, two evidence rows.
  - Sweep: idempotent (a second run creates/removes nothing); user pairs and dismissed pairs are skipped; stale evidence pruned and empty
    relations deleted; GCD/User/event-order evidence never touched.
  - Suggestions: lead-character threshold; cap of 8; user and dismissed pairs excluded; content-type guard.
  - Metron ids: a ComicVine-only series gets its Metron id from the `cv_id` lookup (one hit only); an associated entry matches by
    Metron id, then ComicVine id, then GCD id; two local series with the same id → no match; looked-up ids are cached on the row.
- **App tests** (targeted classes only, per CLAUDE.md):
  - `DetailTabsViewModelTests`: event rows replace Same Event; the 3-row cap; the Other events rail; accept → user relation; dismiss →
    dismissal; removing an automatic relation writes a dismissal.
  - `ScheduledTaskCatalog` entry exists with the agreed settings.
  - The off switch blocks the triggers.
- **Lint:** `XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests` on the changed `DetailTabs.axaml` and `PosterRail.axaml`.
- **On screen:** run the sweep on the real library and check Superman Unlimited, Batman (several events), a Convergence tie-in and an
  Annual on the Related tab, in a dark and a light theme, at 1366×768.

## 9. Out of scope

- Showing Metron associated series that aren't in the library.
- A local collected-edition rule (Q14).
- Re-tagging `EventMembershipRole` (2,695 of 2,842 member issues are "Core"). Tie-ins are worked out from the main series, not from roles.
- Character-aware series families (`SeriesFamilyResolver`): it picks up the new relations automatically, but its own logic is unchanged.
