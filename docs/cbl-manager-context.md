# Paperbunkr CBL Manager: how it works today

> Historical context from before the October 13 matching-core implementation. For current matching behavior see
> `superpowers/specs/2026-10-13-cbl-smart-match-core-design.md` (especially As built); for review/import previews and undo see
> `superpowers/specs/2026-10-25-cbl-smart-match-review-design.md`. The matching, import feedback and healing descriptions below are historical.
> Current healing triggers and arc-refresh behavior are specified in `superpowers/specs/2026-10-25-cbl-smart-match-healing-refresh-design.md`.

Context for brainstorming improvements. Written 2026-10-13 from the current code, not from the design docs.
Paperbunkr is a ComicRack-inspired comic library and reader (C#, .NET, Avalonia, EF Core on SQLite).
"CBL Manager" is the name for everything that builds, imports, matches, refreshes and exports reading lists.
It started as a port of the ComicRack CE plugin of the same name and now lives in the app core.

## 1. Data model

**ReadingList**: `Name`, `Type` (Official, Community, User, Event, Chronological, PublicationOrder, Recommended, Custom),
`Description`, `CoverImageUrl`, folder (`FolderId`, `SortOrder`), tags, and three optional links:

- `Source` + `ArcId` + `ArcName`: the external source the list was built from. Only lists with these can be refreshed, followed, or bulk-requested.
- `StoryEventId`: the Story Event this list is the reading order for.
- `ContinuityId` + `ContinuityOrderKind`: the Continuity (universe) it was generated from, so it can be rebuilt.
- `FollowArc`, `LastFollowedAt`: opt-in daily re-check of the source.

**ReadingListItem**: `IssueId`, `SortOrder`, `GroupLabel` (chapter heading), `Notes`, `Role` (Core, Tie-in, Prologue, Epilogue, Optional…),
plus role-detection state (`RoleSource` user/auto, `RoleReason`, `SuggestedRole`, `SuggestedReason`, `RoleSuggestionDismissed`).

**The central rule: an item always points at a real `Issue` row.** There are no free-text entries.
An entry that matches nothing in the library becomes a **placeholder Issue** (`IsPlaceholder = true`, `FileIsMissing = true`, no file),
and a `Series` row is created for it if no series by that name exists. This mirrors ComicRack CE, which also resolves list entries to book ids.
A placeholder shows in the list as "missing" with **Find & link** and **Request** buttons.
One issue can appear only once per list.

## 2. Ways a list gets created

1. **Blank**, then add issues from the library (search by series name or number, multi-select, "add all of this series", drag-and-drop files).
2. **Import a .cbl** (section 3).
3. **Import a .csv** with header `Series,Number,Volume,Year,Format` (only Series and Number required). Reports owned, missing and skipped rows.
4. **Import a folder** of .cbl/.csv files as a folder tree of lists (CE's Export Folder in reverse).
5. **Build from a story arc** on an external source (section 5).
6. **Build from a Story Event** already in the app: copies the event's members, order and roles.
7. **Build from a Continuity**: a generated publication-order or story-order list that can be rebuilt later.

## 3. .cbl import

A .cbl is ComicRack's XML: `<ReadingList><Name/><Books><Book Series= Number= Volume= Year= Format= /></Books><Matchers/></ReadingList>`.

- `CblFile.Detect` reads the XML first. Matchers and no books means it is a ComicRack **smart list**, which is routed to the Smart List importer instead. Books (or both) means a reading list.
- The reading-list path deserializes with the ported CE classes and, for each `<Book>` in file order, calls the matcher with
  Series, Number, Volume, Year, Format. Volume or Year of -1 means "not given".
- In this historical implementation nothing else from the file was used. `FileName` was ignored on import (CE actually does use it as an import matching fallback). Provider ids were not read until the October 13 matching-core implementation.
- The list name is the file's `<Name>`, else the file name. The list is created as a plain `User` list with no `Source`, so it cannot be refreshed.
- The only feedback is "Imported 'X' with N issues". There is no preview, no owned/missing count, and no report of which entries became placeholders.
- It runs synchronously on the calling thread, one entry at a time.

## 4. Matching (`ReadingListMatcher`)

Shared by .cbl import, .csv import, arc build, arc refresh and the canonical check. One function, no scoring.

1. **Find the series.** Exact case-insensitive name match against every `Series` row. On a miss, retry each series through
   `TitleNormalizer.NamesMatch` (folds punctuation variants such as "X: Y" vs "X - Y"). The first series that matches wins.
   Volume and year are **not** used at this step.
2. **Find the issue in that series.** Issue number compared as text, case-insensitive.
3. **Narrow only if more than one candidate remains**, in this order, each filter kept only if it leaves at least one candidate:
   Volume (text equal), Year (within ±1), Format (text equal).
4. Prefer a real issue over a placeholder with the same key; take the first.
5. **No match:** create the series if needed, then a placeholder issue carrying the entry's number, volume, year and format.

Consequences worth knowing:

- Two different series that share a name (say two volumes of "Superman" stored as separate series rows) are not told apart at step 1: only the first row found is searched.
- There is no fuzzy or scored matching, no confidence value, and no "ambiguous, please choose" outcome. An entry either matches or becomes a placeholder.
- A near-miss series name that the normalizer does not fold creates a new, empty series row in the library as a side effect of importing.
- The arc paths pass only series, number and year (volume and format are always null there).
- Every entry reloads the full series table and saves once per new placeholder, so a large import is slow.

## 5. External sources ("Build from a story arc")

One interface, `IReadingListSource`: `SearchAsync(query)`, `GetArcIssuesInOrderAsync(arcId)`, `GetArcOverviewAsync(arcId)` (best-effort synopsis and cover).
A source returns `ArcIssue(Series, Number, Year, CoverImageUrl, Annotation, Title, Summary)` in its own reading order.

| Source | Kind | Notes |
|---|---|---|
| ComicVine | API, needs key | Story arcs. Live search only. |
| Metron | API, needs login | Story arcs (ordered by cover date). Live search only. |
| Metron reading lists | API, needs login | Curated lists, the user's own and public ones. Read-only. |
| Comic Book Reading Orders | HTML scrape | Marvel and DC cross-title events only. Browsable catalog. |
| ComicArc | JSON-LD scrape | Browsable, very small catalog. |
| ReadingOrders.com | Next.js payload scrape | Browsable, very small catalog, no years. |
| ReadThingsRight | JS + HTML scrape | Eras and sagas; expands ranges like "#1-6". Browsable, small catalog. |

Browsable sources list their whole catalog when picked; API sources need a typed query. Sources are searched one at a time: there is no search across all sources and no merging of results.
Several other sites were ruled out (Cloudflare-blocked, terms forbid it, or no usable data): Comic Book Herald, CMRO, League of Comic Geeks, MyComicList.

**Create from arc:** fetch the ordered issues, run each through the matcher (so missing ones become placeholders), create a `Community` list with
`Source`/`ArcId`/`ArcName`, description and cover, then run role detection.

**Refresh** (arc-linked lists only): fetch the order again, resolve every entry, then reconcile:
existing items move to their new position, new entries are added, entries no longer in the source are **removed**,
and a placeholder nothing else references is deleted. `Role` and `Notes` are never touched. Result message: "Added N, replaced N placeholder(s), N still missing".
Refresh treats the source as the truth, so issues the user added by hand to an arc-linked list are dropped on refresh.

**Follow arc:** a scheduled task runs Refresh daily on followed lists and requests whatever is newly missing.

## 6. Filling the gaps

- **Find & link** (manual): opens the library search in linking mode; picking a result repoints that row at the real issue and deletes the placeholder if nothing else uses it.
- **Request** (one row) and **Request missing issues** (whole list, arc-linked only): hands placeholders to the acquisition system as wanted issues.
  Each series is matched to ComicVine or Metron conservatively; an ambiguous volume is reported as unresolved and never guessed.
- **Automatic relink when a file arrives:** only for files brought in by the acquisition daemon's import step. It finds placeholder rows with the same series name (or series id) and issue number and repoints them.
  A file that enters the library any other way (a normal folder scan, drag-and-drop) does not trigger this; the placeholder stays until a Refresh (arc-linked lists) or a manual Find & link.

## 7. Role detection

No provider says "this is a tie-in", so roles are inferred. Strongest signal first: the source's section heading or note for the issue,
the Format field, the issue's own title, the series name, then one structural rule (first or last issue from a different series than the arc's dominant one reads as prologue or epilogue).
A confident result is written only into an empty or previously automatic slot; anything weaker, or anything that would replace a user's choice, is held as a suggestion to accept or dismiss.
A dismissed suggestion is never offered again. "Optional" is never suggested.

## 8. Checks on an open list

- **Overlap:** flags another list sharing most of this list's issues (typically the same arc pulled from two sources). Offers Compare, Merge (target keeps its order; the other list's notes and roles only fill blanks) or "not a duplicate".
- **Check against ComicVine/Metron** (lists linked to a Story Event that carries the provider's arc id): shows entries the provider has that the list lacks and how many are out of order.
  Offers "Insert missing" and "Reorder to match". Neither ever removes an item.
- **Chronology check:** compares list order with the prequel/sequel relations between the Story Events its issues belong to; offers a reorder.
- **Forecast:** "at your pace, done by ~date · N still missing".

## 9. Export

- **.cbl:** Series, Number, Volume, Year, Format per entry, plus the file path if a preference is on. Notes, roles, chapters, description, tags and the source link are **not** written, so a round trip loses them.
- **.csv:** the import columns plus Read, Owned and Note. Re-imports as-is.
- **Text / clipboard**, and a **printable PDF checklist**.
- **Folder export:** one .cbl per list, a directory per sub-folder.

## 10. Known limits (the improvement surface)

1. .cbl import has no preview and no match report; the user finds out what failed by scrolling for "missing" rows.
2. Matching is exact-or-nothing: no scoring, no candidate list, no way to resolve an ambiguous entry at import time.
3. Series identity is by name only. Volume and year never pick between same-named series, and provider ids in a .cbl are ignored.
4. A failed match creates library clutter (a placeholder issue and sometimes an empty series).
5. Imported .cbl lists have no source link, so they never update and cannot be bulk-requested.
6. Placeholders heal automatically only through the acquisition daemon; a normal library scan does not relink them.
7. Refresh removes anything not in the source, including hand-added issues.
8. Sources are searched one at a time; three of the scraped ones have very small catalogs and depend on page markup that can change.
9. .cbl export drops notes, roles and chapters.
10. Large imports are slow and block while they run.
