# Library book-centric redesign (Slices 1-3 shipped, Slice 4 partial)

## Status

**Slice 1 shipped 2026-08-18, complete.** `IssueListFieldCatalog` now covers essentially all of CE's
90-column catalog except the deliberately-excluded rows in the table below (State/Position/Checked/
Caption/Gap Information/Linked/B&W/Manga/SeriesComplete/EnableProposed, the CE-buggy Week/NewPages
comparers, Published-as-duplicate-of-Released, and AlternateCount which has no backing Paperbunkr
field). Added across three passes: the original field list (Volume, 6 creator roles, Characters/
Teams/Locations, Book* catalog fields, ISBN, Read, Imprint, Language, Age Rating), Story Arc/Series
Group (added specifically because they're what actually addresses the Warhammer motivating case -
Series alone can't separate anthology issues sharing one generic series name), and the remaining
file/technical fields (File Path/Name/Directory/Modified/Created/Format, Count, Alternate Series/
Number, Month, Day, Scan Information, Bookmark Count), plus **Opened** (added while porting
Slice 3's tests - the old series-level `LibrarySortField.LastRead` had no per-issue equivalent
until this point, a real gap caught by a test rather than planning).

**Slice 3 shipped 2026-08-18, same session, right after being deferred once.** Library's default
grid (`CompactGrid`/`ComfortableGrid`/`CoverOnlyGrid`/`PanoramaGrid`/`List`/`Details`/`Tiles`) now
renders one tile per `Issue` via `IssueList.Rows`/`Groups`, exactly like Comic List always did -
`LibraryScreenViewModel.LoadFromDatabase()` has one data pipeline now, not a card-branch and an
issue-branch. `SeriesCardSample`-based `Covers`/`Groups`/`SortCards`/`GroupCards`/
`LibrarySortField`/`LibraryGroupField` (as live code paths) are gone; the Sort/Group toolbar always
shows `IssueList`'s field set, no more `IsIssueListView` gating. Resolved the open questions from
the reconnaissance pass: click opens the Reader directly (`IssueList.OpenIssueCommand`, reused
everywhere); a new `GoToSeriesCommand`/"Go to Series" context-menu item is the Series Detail entry
point; series-action commands (`SetSeriesContentType*`/`SetSeriesStatus*`/`SetSeriesReadingMode*`)
now take a bare `int seriesId` instead of a card object; templates were retargeted per-template
(not a shared resource - Avalonia has no clean story for that here) with badges redesigned for
per-issue semantics (unread count → a plain unread dot via `!IsRead`, Continue Reading retired
entirely since clicking the tile already does that). Added real per-issue cover art
(`IssueListRow.CoverImage`/`PanoramaWidth`, mirroring `SeriesCardSample.FromSeries`'s pattern but
simpler - no "pick a representative issue" step needed). `LibraryViewMode` enum values needed no
migration - they kept their existing meaning as pure layout choices, just now fed by per-issue data.

**Real regression caught by a test, not planning, and fixed properly rather than papered over:**
porting `LibraryScreenViewModelTests` broke `SortField_LastRead_...` because the old series-level
sort had no per-issue equivalent (see the Opened field note above) - and separately, `IssueList`'s
own sort/group state had never been persisted (an accepted gap while it was "only Comic List's
own thing"), which became a real functional regression once it became the *only* sort/group state
for the whole screen. Fixed with new `AppSettings.LibraryIssueListSortField`/
`LibraryIssueListSortDirection`/`LibraryIssueListGroupField` columns (new EF migration,
`AddLibraryIssueListSortGroupPersistence`) - the old `LibrarySortField`/`LibrarySortDirection`/
`LibraryGroupField`/`LibraryShowContinueReadingButton` columns are left in the schema, marked
dormant in their doc comments, not dropped (unused columns are harmless; dropping them is a
separate, lower-priority cleanup with its own migration risk).

Slice 4 (settings migration) is now mostly moot per the above - the enum values didn't need
migrating. What's left of it: deciding whether to eventually drop the dormant `LibrarySortField`/
`LibraryGroupField`/`LibraryShowContinueReadingButton` columns in a follow-up migration.

## Motivation

Library's default grid (`CompactGrid`/`ComfortableGrid`/`CoverOnlyGrid`/`PanoramaGrid`/`List`/
`Details`/`Tiles`) shows one aggregated card per `Series`. This session, while enriching Library's
Sort/Group toolbar, the user hit a concrete case proving that model is too coarse for real
libraries: a "Warhammer 40" series card showed "51 issues," but those 51 issues are actually ~15
distinct mini-series/one-shots (Damnation Crusade, Dawn of War III, Forge of War, Crown of
Destruction, Sisters of Battle, Marneus Calgar, Lone Wolves, Fire & Honour, Exterminatus, Defenders
of Ultramar, Deathwatch, Revelations, Will of Iron, Fallen, Condemned By Fire) - visible as separate
cards in the user's real ComicRack CE library, but invisible in Paperbunkr because they all share
one `Series.Name` = "Warhammer 40" while their real distinguishing identity lives on each `Issue`'s
own `Title`/credits (confirmed live: one issue's Detail view showed per-issue Writer "Dan Abnett,
Ian Edginton", Artist "Lui Antonio", Colorist "JM Ringuet" - all issue-specific, with the series
card itself showing just whichever issue's cover/summary happened to be picked as "first").

This is a real, common tagging pattern (anthology/event imprints tagging many one-shots under one
umbrella `Series` value) that a series-aggregate card model structurally cannot represent, no
matter how correct the migration/scan logic is. CE never has this problem because CE's real
architecture (confirmed by reading `_reference/ComicRackCE` this session) never aggregates to
series-cards at all - it has one flat per-book list, and "group by Series" is just one grouping
choice on that list, showing the real books underneath rather than collapsing them.

## CE's real architecture (verified against source, not assumed)

- **One registry, not several.** `ComicRack/Views/ComicBrowserControl.cs:755-848` registers 90
  columns via `itemView.Columns.Add(new ItemViewColumn(id, name, width, field, comparer, grouper,
  ...))` - this single list is the source of truth for what's sortable *and* groupable, both by
  name and behavior. `ComicRack.Engine/Metadata/ComicBook/ComicBookMetadataManager.cs:15-144`
  builds a lookup from it at startup, keyed by each column's integer id (persisted sort-key strings
  are comma-separated ids, e.g. `"1,2"` = Series then Number).
- Each column wraps a `ComicBookXxxComparer : Comparer<ComicBook>` and usually a
  `ComicBookGroupXxx`/`ComicBookStringGrouper<TMatcher>` via thin pass-through adapters
  (`CoverViewItemBookComparer<T>`, `CoverViewItemBookGrouper<T>`) - all real logic lives in the
  individual comparer/grouper classes.
- **Grouping is a display option on the one flat list, not a separate aggregation.** "Group by
  Series" (`ComicBookGroupSeries`) buckets the same per-book list into per-series sections, each
  showing the actual books in it - it does not collapse a series down to one representative card.

## Full CE field catalog (reference table)

90 columns from `ComicRack/Views/ComicBrowserControl.cs:755-848`. All comparer/grouper classes live
under `ComicRack.Engine/Metadata/ComicBook/{Comparer,Group}/` unless noted. Already-built columns
(in Paperbunkr's `IssueListSortField`/`GroupField`) are marked ✅; real CE fields with no Paperbunkr
equivalent yet are the gap this redesign's Slice 1 would close.

| Id | Column | Backing property | Status |
|---|---|---|---|
| 101 | State | `ComicBook.Status` bitflag (file-missing / dirty-metadata) - **not** read/unread | Skip - UI/session concept, not real metadata worth persisting as a sort field |
| 100 | Position | `CoverViewItem.Position` - transient UI list index, not a stored field | Skip - not a real sortable field, CE itself doesn't persist it |
| 102 | Checked | `ComicBook.Checked` bool, real persisted XML attribute (bulk-selection flag) | Gap - Paperbunkr has no equivalent concept/UI yet; new feature, not a field addition, if wanted |
| 1 | Series | `ShadowSeries` + Format→Volume→Number tie-break | ✅ `IssueListSortField.Series` |
| 2 | Number | Parsed numeric issue # | ✅ `IssueListSortField.Number` |
| 3 | Volume | `ShadowVolume` int | Gap |
| 4 | Title | `ShadowTitle` | ✅ `IssueListSortField.Title` |
| 5 | Opened | `OpenedTime` (last-read timestamp) | Gap at issue level (Library has `LastRead` as a *series* aggregate only) |
| 6 | Added | `AddedTime` | ✅ `IssueListSortField.Added` |
| 7 | Pages | `PageCount` | ✅ `IssueListSortField.PageCount` |
| 39/71 | Published (+regional) | `Published` DateTime | Gap (Paperbunkr has `Released`/`ReleasedTime`, a distinct field - confirm these aren't meant to be the same before merging) |
| 9/10/41 | File Path / File Name / File Directory | `FilePath` / filename-no-ext / directory | ✅ |
| 11-14,22-24,72 | Writer/Penciller/Inker/Colorist/Letterer/Cover Artist/Editor/Translator | 8 separate creator-role strings | Writer ✅; other 7 roles are a gap |
| 15 | My Rating | `Comic.Rating` (stack-aware average) | ✅ `IssueListSortField.Rating` |
| 16 | Opened Count | `OpenedCount` | ✅ `IssueListSortField.OpenCount` |
| 17 | Read Percentage | stack-aware average | ✅ `IssueListSortField.ReadPercentage` |
| 18/42/19 | File Modified / File Created / Genre | timestamps / `Genre` | ✅ all three |
| 20 | Publisher | `Publisher` | ✅ |
| 21 | Count | `ShadowCount` (declared series total) | ✅ |
| 25 | File Size | `FileSize` | ✅ |
| 26-28 | Alternate Series / Number / Count | `AlternateSeries`/`CompareAlternateNumber`/`AlternateCount` | Alternate Series/Number ✅; `AlternateCount` remains a confirmed genuine gap (no backing Paperbunkr field, from the earlier Comic List spec) |
| 29/68/69 | Month / Day / Week | `Month`/`Day`/`Week` | Month/Day ✅ - **Week is CE-buggy** (`ComicBookWeekComparer.cs:9` compares `x.Day` to `y.Week`) - do not replicate |
| 30 | Caption | Reuses Series comparer verbatim, no distinct logic | Skip - not a real distinct field |
| 31 | Tags | `Tags` | ✅ |
| 32/33 | Imprint / Language | `Imprint`/`LanguageISO` | Gap |
| 34 | Format | `ShadowFormat` | ✅ |
| 35/36 | B&W / Manga | bools | Gap (Manga: Paperbunkr uses `ContentType`/`ReadingMode` instead, a deliberate deviation already documented elsewhere - don't reintroduce as a raw bool) |
| 37/214 | File Format / Actual File Format | extension / probed file header | File Format ✅; Actual File Format (requires opening the file - `SniffActualFileFormatAsync`) still a gap |
| 38 | Age Rating | `AgeRating` | Gap |
| 8 | Year | **CE bug**: no override, silently sorts by full `Published` DateTime | ✅ already fixed correctly (`IssueListSortField.Year` uses real `Issue.Year`, not replicating the bug) |
| 40 | Characters | `Characters` string | Gap (Paperbunkr's field is named `MainCharacterOrTeam`) |
| 43 | Bookmark Count | `BookmarkCount` | ✅ (`Issue.Bookmarks.Count`) |
| 44 | New Pages | **CE bug**: `ComicBookNewPagesComparer.cs:9` compares `x.NewPages` to `y.BookmarkCount` | Do not replicate - would need its own correct implementation if wanted |
| 45/46/47 | Teams / Locations / Web | strings | Gap |
| 48 | Community Rating | stack-aware average | ✅ |
| 49 | Linked | `IsLinked` | Skip - CE plugin/linked-book concept, no Paperbunkr equivalent |
| 50-57 | Book Price/Age/Store/Owner/Condition/CollectionStatus/Location/ISBN | the "Catalog" bucket | ✅ all 8 (also searchable via `SearchMode.Catalog`) |
| 58/59 | Series complete / Proposed Values enabled | `SeriesComplete`/`EnableProposed` | Series complete: Paperbunkr models this differently via `Series.Status` (deliberate deviation, already documented) - don't reintroduce the raw CE flag |
| 60 | Gap Information | Reuses Series comparer, no distinct logic | Skip |
| 61 | Read | `HasBeenRead` - the *actual* read/unread field (distinct from ReadPercentage and from "State") | ✅ |
| 64/65 | Story Arc / Series Group | `StoryArc`/`SeriesGroup` | ✅ (added specifically to address the Warhammer motivating case - see Slice 1) |
| 63 | Scan Information | `ScanInformation` | ✅ |
| 66/67 | Main Character/Team / Review | strings | Main Character/Team ≈ already covered via `Characters`; Review still a gap |
| 70 | Released | `ReleasedTime` | ✅ |
| 200-214 | "Series: stats" family (Books/Pages/Pages Read/Percent Read/First-Last Number/First-Last Year/Avg Rating/Avg Community Rating/Gaps/Book Added/Opened/Released/Actual File Format) | Per-series aggregate stats object | Several already covered by Library's *series-card* fields (`IssueCount`≈Books, `DateAdded`≈Book Added, `LastRead`≈Opened) - the rest (Percent Read, First/Last Year, Avg Rating, Avg Community Rating, Gaps) only matter if series-cards are kept alongside the new per-issue default (see open questions) |

Two confirmed CE source bugs, deliberately not replicated (matching this project's existing "don't
replicate CE bugs" precedent from the original Comic List spec): `ComicBookNewPagesComparer`
compares the wrong pair of fields; `ComicBookWeekComparer` does too.

## Target architecture

Library's default grid becomes one tile per `Issue`, sorted/grouped by the full CE-faithful field
catalog above. `IssueListRow`/`IssueListFieldCatalog` (built this session for the "Comic List" work)
already model exactly this shape - Comic List turns out to be the seed of the real default view,
not a side mode. "Group by Series" becomes one more `IssueListGroupField` option, showing each
series' actual issues clustered under a header (exactly what would have surfaced the Warhammer case
immediately, instead of a single 51-issue card).

## Open questions - resolve before implementation starts, not during it

1. **Card click/double-click behavior.** Today: click a series card → Series Detail screen (Related
   issues, reading lists, series metadata). CE: double-click a book → opens the reader directly.
   Once tiles are per-issue, what does a single click do, and where does Series-level Detail get
   entered from (a right-click context action? a header click when grouped by Series? something
   else)? This is a real UX decision, not an implementation detail - get it wrong and every other
   piece of this redesign inherits the mistake.
2. **Visual templates.** Do `CompactGrid`/`ComfortableGrid`/`CoverOnlyGrid`/`PanoramaGrid`/`List`/
   `Tiles`'s existing XAML `DataTemplate`s get retargeted from `SeriesCardSample` to `IssueListRow`,
   or does each need a new per-issue-tile template? `SeriesCardSample`'s cover-art/panorama-width/
   language-badge/continue-reading-button logic was all built for a series-aggregate - decide what
   of it (if any) still makes sense per-issue versus what only made sense as a series summary.
3. **Persisted settings migration.** `LibraryViewMode`/`LibrarySortField`/`LibraryGroupField`
   already have real user data in `AppSettings` with today's series-card meaning. Once "Compact
   grid" means "per-issue tiles," does existing persisted state need an explicit migration/reset,
   or does it just silently mean something different on next launch? Needs a decision, not a
   silent behavior change.
4. **What happens to `SeriesCardSample`/`LibraryFieldCatalog`.** Retired entirely once the default
   view is issue-centric? Kept only for a hypothetical "series overview" screen reached via the
   click-behavior decision in (1)? Depends on that decision.

## Reconnaissance for Slices 2-4 (2026-08-18, deferred before implementation started)

Read through all of `LibraryScreen.axaml`'s 7 series-card templates and `LibraryScreenViewModel`'s
series-action commands before scope was judged too large for the pass it was requested in. No code
was changed - this is what whoever resumes it should already know, so the research isn't lost:

- All 7 templates (`CompactGridItemTemplate`/`ComfortableGridItemTemplate`/`CoverOnlyGridItemTemplate`/
  `PanoramaGridItemTemplate`/`ListItemTemplate`/`DetailsItemTemplate`/`TilesItemTemplate`) duplicate
  an identical ~30-line `ContextMenu` (Show in Explorer, Set Content Type, Set Reading Direction, Set
  Status) and are all `x:DataType="models:SeriesCardSample"`.
- Every series-action command (`RevealSeriesCommand`, `SetSeriesContentType{Comic,Manga,Manhua,
  Manhwa}Command`, `SetSeriesStatus{Unknown,Ongoing,Completed,Cancelled,Hiatus}Command`,
  `SetSeriesReadingMode{LeftToRight,RightToLeft}Command`) only ever reads `card.SeriesId` from its
  `SeriesCardSample` parameter internally - trivially retargetable to take a bare `int seriesId`
  instead of the whole card object, once tiles are `IssueListRow`-based. `SelectCardCommand` calls
  `_goDetail(card.SeriesId)` (opens Series Detail); `ContinueReadingCommand` calls
  `_goReaderForIssue(card.ContinueReadingIssueId)`.
- **Real gap found**: `IssueListRow` has no real decoded cover art field, only `CoverBrush` (a
  placeholder gradient) - Comic List mode's own row template never needed real covers (dense text
  rows), but a grid of per-issue tiles genuinely needs them. Would need a `CoverImage` property on
  `IssueListRow`, populated the same way `SeriesCardSample.FromSeries` already does
  (`CoverImageCache.Get(issue.Id)` - simpler than the series case even, no "pick a representative
  issue" step needed since it's the tile's own issue).
- **Architectural simplification found, worth keeping for the real implementation**: once every
  Display mode sources from `IssueList.Rows`/`Groups` instead of `Covers`/`Groups`
  (`SeriesCardSample`-based), `LibrarySortField`/`LibraryGroupField`/`SortCards`/`GroupCards`
  and the Sort/Group toolbar's `IsIssueListView` gating (built earlier this session) all become
  unreachable dead code - the toolbar can just always show `IssueList`'s rich field set
  unconditionally, and no `LibraryViewMode` enum values need renaming or removing (they keep their
  existing meaning as pure *layout* choices - "Compact grid" still means a compact grid, just of
  issue tiles instead of series tiles). This resolves the "no mode gate" requirement and the
  Slice 4 settings-migration question *for the enum itself* at the same time - no persisted
  `LibraryViewMode` value changes meaning enough to need a migration. What Slice 4 would still need:
  deciding whether to leave the now-unused `AppSettings.LibrarySortField`/`LibraryGroupField`
  columns in place (harmless but dead) or drop them in a follow-up migration.

## Phased slices (matching this project's established multi-slice pattern)

1. **✅ Shipped 2026-08-18.** Extended `IssueListFieldCatalog` to the full CE field set from the gap
   table above (Volume, the 6 other creator roles - Writer already existed, Characters/Teams/
   Locations, the Book* catalog fields, ISBN, Read/HasBeenRead, Imprint, Language, Age Rating,
   plus **Story Arc and Series Group** - added in a follow-up pass after realizing these are the
   fields that actually address the Warhammer motivating case: grouping by Series alone can't
   separate anthology issues sharing one generic series name, but Story Arc can, since it's CE's
   real per-story field for exactly this pattern). `Published` deliberately skipped as redundant
   with the existing `Released` field (no distinct Paperbunkr equivalent found). File Modified/
   Created and Bookmark Count deliberately deferred (not part of the agreed scope for this slice) -
   still a real gap if wanted later.
2. **"Group by Series" shows real nested issues** - already partially possible today (Comic List
   mode already has a Series group option), just needs on-screen verification that it actually
   surfaces cases like Warhammer correctly once Slice 1's fields are in.
3. **Resolve the open questions above**, then retarget Library's default Display modes from
   `SeriesCardSample` to `IssueListRow` - the actual rearchitecture, gated on (1) and (2) being
   solid and the click-behavior/template/migration questions being explicitly decided first.
4. **Reconcile persisted view settings** per the migration decision in open question 3.

Not attempted this session - this doc is the reference for whichever future session/pass picks
Slice 1 up.
