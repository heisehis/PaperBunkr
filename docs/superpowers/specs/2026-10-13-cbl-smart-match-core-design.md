# CBL Manager Smart Match, part 1: the matching core

Date: 2026-10-13. Status: design, awaiting review.
Source brief: the user's "Smart Match & Resolve" specification (pasted into the session of this date).
Background on how things work today: [docs/cbl-manager-context.md](../../cbl-manager-context.md).

## 1. Scope

The brief is seven phases. It is split into three sub-projects, each with its own spec and plan:

1. **Matching core (this document):** correctness fixes, a record of what each source asked for, provider ids carried through,
   a candidate engine, honest import counts, and basic healing.
2. **Review UI (later):** the resolve side panel, the bulk review dialog, the import preview step, undo, and reuse of confirmed matches across lists.
3. **Healing, remainder (later):** relink after scrapes, metadata edits and renames; a source refresh that keeps hand-added issues.

Not in this document: any new screen or dialog, contextual (story-arc) ranking signals, writing provider ids into exported .cbl files,
network lookups to bridge providers.

## 2. What the audit found

Code, as of this date:

- `ReadingListMatcher.FindSeriesByCascade` returns the **first** series row whose name matches and searches only that row.
- Issue numbers are compared as raw text there, although `IssueNumbers.Equal` (leading zeros) exists and `PlaceholderRelinker` uses it.
- `Series` is a name and nothing else. Volumes of one title share a row and differ only by each issue's `Volume` and `Year`.
- Year is consulted only when several candidates remain, so a lone candidate from the wrong decade is linked.
- Provider ids already persist in `ComicMetadataExternalIds` (`EntityKind` Issue and Series, `Provider` ComicVine and Metron),
  written by the scraper and the embedded-metadata reader. `ArcIssue` does not carry the ids the ComicVine and Metron adapters receive.
- `ComicBookReadingOrdersSource.ParseIssuesFromHtml` silently skips any line that is not `Series #digits`.
  `ComicVineSource.GetArcIssuesInOrderAsync` silently drops any issue its batch call does not return.
- A placeholder is relinked automatically only by the acquisition daemon (`ImportProcessor` → `PlaceholderRelinker`).
  An ordinary scan, the live folder watcher and drag-and-drop do nothing.
- `.cbl` import adds an entry twice when two `<Book>` rows resolve to one issue; arc refresh keeps the first and says nothing.
- Each entry reloads the whole series table, and each new placeholder saves on its own.

The user's database (read-only query; 5,574 owned issues, 577 list entries, 20 lists, all built from ComicVine, Metron or Comic Book Reading Orders):

| The 80 placeholder entries | Count |
|---|---|
| Owned in the same series row with the same number; never relinked | 50 |
| Owned in a second series row with an equivalent name | 17 |
| Series owned, that number not owned | 7 |
| Series not owned | 6 |

Also: 41 series names exist as more than one row, 66 series rows own no issue, 150 placeholder issues belong to no list,
and 3,508 issue-level provider ids are stored (2,908 Metron, 600 ComicVine).

Conclusion: 67 of 80 misses are comics already in the library. The larger cause is that nothing re-matches after a scan; the smaller is first-series-wins.

## 3. Decisions (all confirmed by the user)

| # | Decision |
|---|---|
| Q1 | Three sub-projects; this one first. |
| Q2 | The matcher searches every series row with an equivalent name. A one-time repair fixes existing damage, with a report before any change. |
| Q3 | No id, no year, and the number is owned in two volumes: do not link; mark ambiguous. |
| Q4 | Entry year and the only candidate's year disagree by more than one: do not link; ask. |
| Q5 | Matching makes no network calls. |
| Q6 | Import stays one click and reports real counts. The preview-before-commit step belongs to sub-project 2. |
| Q7 | Basic healing (a recheck pass, run after scans and drag-and-drop) moves into this sub-project. |
| Q8 | "Recheck library" is one item in the open list's ⋯ menu. The automatic pass covers all lists and reports through the Activity Center. |
| Q9 | The automatic pass changes placeholders only, on a unique match. It never touches a link to a real issue or a link the user made. |
| Q10 | Placeholders no list uses are reported. They are removed only if provably list-created (see §8: they are not distinguishable, so they stay). |
| Q11 | Backfill: an existing placeholder entry gets a source record from the placeholder's own series, number and year. Entries linked to real issues get none. |
| Q12 | Two source entries resolving to one issue: keep the first, report the second as a duplicate by name. |
| Q13 | A source line that cannot be parsed is counted as skipped and its text shown. No number is guessed. |
| Q14 | The repair is a card in Library Health: preview, then Apply. |
| Q15 | An ambiguous entry's row reads "needs a choice". The existing Find & link drawer lists the engine's candidates first, each with a one-line reason. |
| Q16 | Same number: `1`, `001`, `#1`; and `½`, `1/2`, `0.5`; ignoring case and spaces. Different: `1` / `1.AU` / `1A` / `Annual 1`. |
| D1 | The source record is its own one-to-one table, not columns on `ReadingListItem`. |
| D2 | No persistent identity index table. Ids are already stored; everything else is loaded once per operation into an in-memory snapshot. |

D2 departs from the brief (§5 asks for a synced index). Reason: the brief lists eight events that would have to update it, and one missed call site
gives silent wrong matches. The engine sits behind an interface, so a table can replace the snapshot later without touching callers.

## 4. Data model

New entity `ReadingListItemSource`, one-to-one with `ReadingListItem`, deleted with it.

| Field | Meaning |
|---|---|
| `ReadingListItemId` | Owner; unique. |
| `Series`, `Number`, `Volume`, `Year`, `Format`, `Title` | What the source asked for, as given. |
| `SeriesKey`, `NumberKey` | Normalized forms (§5.1, §5.2), stored so healing can look up by index. |
| `SourceKey`, `SourceEntryId` | Which source, and its own id for the entry when it has one. |
| `ComicVineIssueId`, `ComicVineSeriesId`, `MetronIssueId`, `MetronSeriesId` | Only when the source supplied them. Separate columns per provider. |
| `Method` | `ProviderId`, `Metadata`, `User`, `Backfilled`, `None`. Stored as text, like every enum in this database. |
| `State` | `Linked`, `NeedsChoice`, `Unresolved`. |
| `LastCheckedUtc` | Last time the engine looked at it. |

Indexes: `ComicVineIssueId`, `MetronIssueId`, and (`SeriesKey`, `NumberKey`).

Rules:

- The record is written once, at import, and its "asked for" fields are never overwritten by a relink.
- `Method = User` is set by Find & link. Such an entry is never re-evaluated by the engine.
- A list item with no source record (blank lists, hand-added issues, legacy linked entries) behaves exactly as today.
- No provider id is ever derived from a title.

Migration: one EF migration adds the table. A data step creates a `Backfilled` record for each list item that points at a placeholder,
from that placeholder's series name, number, volume, year and format.

## 5. Components

All in `Paperbunkr.Data/ReadingLists/Matching/`. None of them references the UI.

### 5.1 `IssueNumberKey`

One function, `IssueNumberKey.Of(string?)`, implementing Q16. `IssueNumbers.Equal` and the matcher both call it; the string compare in
`StoryEventIdCompletion.SameIssue` is switched too. Source adapters do no number clean-up of their own.

### 5.2 Series keys

`SeriesKeys.For(name)` returns the forms `TitleNormalizer.NamesMatch` already compares (exact lower-case, volume marker removed, stripped down).
Two names are equivalent when any form is shared. A series' `Titles` (alternate titles already in the database) contribute keys too.
`Issue.AlternateSeries` does not: it describes crossover placement, not an alias.

### 5.3 `ComicReference`

A record of one requested entry: the "asked for" fields of §4. `ArcIssue` gains the four id fields so adapters can fill them:

- ComicVine: issue `id` and `volume.id`, both already in the response it requests.
- Metron arcs and Metron reading lists: issue `id` and `series.id`; a ComicVine id as well if the row carries one
  (to be checked against Metron's serializer during implementation, not assumed).
- The scraped sources supply none.

### 5.4 `LibraryIndex`

Built by one set of queries per operation: every non-placeholder issue with series id, series keys, effective number key, effective volume,
effective year, format, file state, and its provider ids. It exposes lookups by (provider, issue id) and by series key.
"Effective" means through `IssueMetadataExtensions`, because 1,826 issues in the user's library hold their number as an accepted proposal.

### 5.5 `MatchEngine`

`Match(ComicReference, LibrarySnapshot)` returns a `MatchResult`: a classification, the ranked candidates, and for each candidate typed
evidence in three lists (agrees, unknown, conflicts). It reads nothing but its arguments and writes nothing.

Tier 1, ids:

- The reference has a ComicVine or Metron issue id and exactly one owned issue carries that id: **Verified**.
- Several owned issues carry it: if they are copies of one comic (same series row, number key and year), the best copy by
  `DuplicateKeeperRanker` is **Verified**; otherwise **Conflict**, no link.
- A reference with ids from both providers that point at different owned comics: **Conflict**.
- "Cross-provider" in this sub-project means the reference and the owned issue share an id from either provider. A ComicVine-only reference
  cannot reach a Metron-only issue by id; it falls to tier 2. No bridge is fetched (Q5).

Tier 2, metadata, when tier 1 found nothing:

- Candidates are owned issues whose number key equals the reference's, in **any** series row whose keys are equivalent to the reference's.
  Copies of one comic collapse to one candidate.
- Evidence per candidate: year (agrees within one, unknown if either side lacks it, conflicts otherwise), volume (agrees if equal text,
  unknown if either side lacks it, conflicts otherwise), format (agrees or unknown; a difference is noted, never a conflict).
- No candidate: **Unresolved**.
- One candidate: **Strong** if nothing conflicts; **Possible** if year or volume conflicts (Q4).
- Several candidates: discard those with a conflict. If exactly one survivor positively agrees on year or volume, it is **Strong**.
  Otherwise **Ambiguous**, listing the survivors (Q3).

Only **Verified** and **Strong** may be linked without the user. Classifications are labels; no percentage is produced or shown.

### 5.6 `ReadingListResolver`

The one entry point every import path uses.

- `Resolve(context, references)` builds the snapshot once, runs the engine per reference, and applies Q12 (first wins, later ones become duplicates). It writes nothing.
- `Commit(...)` creates the list items, their source records, and placeholders for everything not linked, then saves once.
  A placeholder goes into the equivalent series row that owns issues if there is one, else an existing equivalent row, else a new row.
- `ReadingListMatcher.FindExisting` and `ResolveOrCreatePlaceholder` stay as thin wrappers for the callers that pass a bare series and number
  (the Library's "add a physical book", existing tests).

Callers moved onto it: `CblReadingListIO.Import`, `CsvReadingListIO.Import`, `ReadingListFolderIO`, `ArcReadingListBuilder.CreateFromArcAsync`
and `RefreshAsync`, `ReadingListCanonicalDiff`.

### 5.7 `ImportReport`

Returned by every import and refresh: entries the source reported (when it says), parsed, matched by id, matched by metadata, needing a choice,
missing, duplicates (with names), skipped (with text), failed. `parsed` equals the sum of the outcome counts; a test asserts it.
When fewer entries were fetched than the source reported, the result says the import is incomplete.

To carry skipped lines, `IReadingListSource` gains `GetArcEntriesAsync`, returning the issues plus skipped lines and the reported count,
with a default implementation that wraps `GetArcIssuesInOrderAsync`. Only the two adapters that drop entries today override it.

### 5.8 .cbl ids

The `.cbl` reader also reads provider ids from a `<Book>`'s child element when present, and ignores anything it does not recognize.
Files without ids import exactly as now. The element's exact shape in community-made files must be confirmed against real files before
this is implemented; it is not documented in this repository or its reference sources.

### 5.9 `ReadingListHealer`

`Recheck(context, scope)` where scope is one list, all lists, or a set of newly added issue ids.

- It considers only source records whose item points at a placeholder and whose `Method` is not `User`.
- For a set of new issue ids it selects candidates through the §4 indexes instead of walking every list.
- A **Verified** or **Strong** result is applied with `ReadingListItemLinker.Relink`, which already preserves order, role and notes and
  removes a placeholder nothing else uses. If the issue is already in that list, the entry is left and reported.
- One transaction per pass. Returns relinked, now needing a choice, and still missing.

Triggers: the end of a `LibraryFolderScanner` run that added issues (manual scan, live folder watcher, drag-and-drop), and the daemon's
`ImportProcessor`, whose `PlaceholderRelinker` call becomes a call to the healer. Runs off the UI thread. It posts an Activity Center entry
only when something was relinked.

## 6. One-time repair

A Library Health card, "Reading list repairs". Preview lists, Apply performs in one transaction:

1. **Relinks:** every placeholder entry the healer can now link.
2. **Empty duplicate series rows:** a row owning no issue whose keys are equivalent to a row that owns issues. Its placeholders move to the
   owning row; the empty row is deleted only if nothing else in the database references it, else it stays and is named in the report.
3. **Reported, not changed:** pairs of equivalent rows that both own issues (for example "The Ultimates" and "Ultimates"), and the
   placeholders no list uses.

The card disappears when the preview is empty.

## 7. Visible changes

Small, and all inside existing surfaces:

- Import and refresh messages show the §5.7 counts.
- A row whose source record is `NeedsChoice` reads "needs a choice" where it now reads "missing".
- The Find & link drawer shows a "Suggested" group above the search results: each candidate with its series, number, year and one line
  built from the engine's evidence (for example "same number · year agrees · volume unknown"). Picking one relinks and records `Method = User`.
- ⋯ menu: "Recheck library".
- The Library Health card of §6.

Avalonia constraints to respect when building these: the drawer rows and the health card reuse existing templates and theme tokens (no fixed
colours); a pick in the drawer relinks and reloads on the next dispatcher tick, because the row being clicked is removed (the project's
"don't detach a control from inside its own routed event" rule); suggested rows are plain buttons so they work from the keyboard without new
key handlers. The `avalonia-data-templates` subskill and the `avalonia-pro-max/review-checklist` are to be read before this part is written.

## 8. Facts checked for this design

- Placeholders made by the Library's "add a physical book" use the same function and flags as list-made ones, so the 150 unused placeholders
  cannot be told apart. They are reported and left (Q10).
- The reference CBL Manager plugin matched ids through a ComicRack custom field, not through the `.cbl` file, so it offers no precedent for §5.8.
- All scans go through one class, `LibraryFolderScanner`, so one hook covers manual scan, live watch and drag-and-drop.

## 9. Performance

- One snapshot per operation replaces a full series-table load per entry.
- One save per import replaces one per placeholder.
- Imports and rechecks run on a background task as Activity Center jobs and honour cancellation between entries; a cancelled import writes nothing.
- A test imports a 300-entry list against 6,000 library issues and asserts a time bound, recorded against the current code first for a baseline.

## 10. Failure handling

- A source error during fetch aborts before anything is written.
- `Commit` and each healer pass are single transactions.
- If the library changed between `Resolve` and `Commit` (an issue deleted), that entry falls back to a placeholder and is counted as missing.

## 11. Tests

Engine (pure, no database): id match per provider; unknown id; ids pointing at two comics; duplicate copies; same-named rows; number is found in
the second row; volume and year disambiguation (three volumes of one title); lone candidate with a conflicting year; missing year with two
volumes; every Q16 pair, both the equal and the distinct ones.

Resolver and imports: `.cbl` without ids; `.cbl` with ids; `.csv` with missing columns; arc create and refresh; duplicate source entries;
skipped lines surfaced; counts add up; cancellation writes nothing; no placeholder or series row exists after `Resolve` alone.

Healer: placeholder relinked after a scan adds the comic; `Method = User` entry untouched; real links untouched; placeholder shared by two
lists; issue already in the list; order, role, notes and reading progress unchanged; source record's "asked for" fields unchanged after relink.

Repair: preview changes nothing; apply relinks, moves and deletes as §6; referenced empty row is kept.

Migration: up on a copy of a populated database; backfill count equals the placeholder entry count.

View tests (targeted classes only): "needs a choice" row; suggested group appears and a pick relinks; recheck command updates the list.

## 12. Acceptance for this sub-project

1. Run against a copy of the user's database, the repair preview accounts for all 67 entries the audit found owned: each is either proposed
   for relinking or shown as needing a choice with the owned comic among its candidates. Apply performs the relinks.
2. A list whose comics arrive by ordinary scan fills in without any user action.
3. Every import reports counts that add up, and no entry is dropped without being named.
4. No entry is linked to a comic from a conflicting year or to one of several unresolved volumes.
5. Existing reading-list tests pass unchanged, apart from those asserting the old first-row behaviour.

## 13. As built (2026-10-13)

Differences from the sections above, each made during implementation:

- **§5.4:** the class is `LibraryIndex`; an entity named `LibrarySnapshot` already existed.
- **§5.8:** confirmed. Community files write `<Database Name="cv" Series="…" Issue="…" />` inside each `<Book>` and a `<NumIssues>` count,
  checked against the public CBL-ReadingLists collection. `Name="metron"` is accepted the same way but was not seen in a real file.
- **§5.9:** the daemon keeps its own `PlaceholderRelinker` call (it knows the exact series it downloaded); the scan hook covers everything
  else. The targeted pass after a scan looks entries up by the comic's primary series key and its provider ids, not by alternate titles;
  "Recheck library" and the repair consider those too.
- **§6:** the repair relinks and reports; it does not move placeholders between series rows or delete emptied rows. A series row is
  referenced by many tables, some with cascading deletes, and Library Health's existing Empty Rows queue already removes such rows safely.
  The section is on demand (Check, then Link) like Publisher logos, instead of appearing by itself.
- **§5.7:** "failed" entries are folded into "skipped", each named.
- **Result on a copy of the user's database:** 63 of the 80 waiting entries linked, 4 need a choice, 13 are not in the library.
  The 4 are owned comics whose stored year is the series' start year, two or three years from the issue's own.
