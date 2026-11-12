# Continuity map M4 "Completeness" — design

*Status: approved by the user 2026-11-11 (grilling round 4, Q33–Q37, with a visual-companion screen). Source: slice M4 of the
[continuity map pitch](2026-11-11-continuity-map-pitch.md) (items 12, 15). Builds on [M1](2026-11-11-continuity-map-room-design.md); its
counts feed [M3](2026-11-11-continuity-matrix-rollcall-design.md). Implemented in the working tree at base HEAD `28e60a1` on top of the
still-uncommitted Smart fill code (2026-11-12, at the user's request); on-screen review pending.*

## Goal

Show the issues of an event that the user doesn't own as "not in your library" cards in their right place, let them go to Wanted in one
step, and point out likely gaps when no provider list is available.

## Facts this rests on (verified 2026-11-11)

- **Provider arc lists are never saved.** Smart fill keeps them in `EventEvidenceCache` for one operation
  (`EventEnrichment/EventEvidenceCollector.cs:10-14`, `EventSmartFillRunner.cs:17-18`). Its spec makes membership "evidence only: never add,
  remove, reorder, import or create local member issues". The persisted `EvidenceJson` holds one chosen value per field, not an issue list.
- **The fetchers.** Both providers implement `IReadingListSource.GetArcEntriesAsync(arcId, ct) → ArcEntryList(Issues, SkippedLines,
  ReportedCount)`.
  - The record is `ArcIssue(Series, Number, Year, CoverImageUrl, Annotation, Title, Summary, ComicVineIssueId, ComicVineSeriesId,
    MetronIssueId, MetronSeriesId, StoreDate, CoverDate)` (`ReadingLists/Sources/ArcModels.cs:13-16`).
  - Metron fills the Metron ids plus `cv_id`; ComicVine fills the ComicVine ids.
  - The Metron list is publication-ordered, not a guaranteed reading order (Smart fill spec).
- **Guided event creation** (`NewEventOrContinuityViewModel.SelectProviderArcAsync`, :647-702) fetches the full list.
  - It matches entries only by exact `ComicMetadataExternalIds` ids, and keeps unowned entries as text labels in memory.
  - Arc-import-as-reading-list creates `IsPlaceholder` issues and `ReadingListItemSource` rows. Those belong to reading lists, not events.
- **Wanted needs a tracked series and a provider issue.**
  - `WantedIssue` hangs off `WatchedSeries`.
  - `WantedService.Request(context, watched, ComicVineIssue | CatalogIssue)` (`Acquisition/WantedService.cs:113, 141`) and
    `RequestAllMissing` are the entry points.
  - `ArcRequestService.RequestPlaceholdersAsync` (:51) matches a series to a ComicVine/Metron volume for placeholder issues.
  - There's no "series name + number" request.
- **Library gaps.** `CollectionGapResolver.Compute(issues, options)` returns `CollectionGap(SeriesId, SeriesName, MissingNumbers)`
  (`Metadata/CollectionGapResolver.cs:39`).
- **Verified event identity.** Smart fill's identity rules (verified arc ids, cross-provider bridges, no name-only matches) decide which arc
  an event is.
- ComicRack CE has no events; no parity applies.

## Decisions

| # | Decision |
|---|---|
| Q33 | A new cache stores each event's provider issue list with source and date. It's filled by Smart fill runs, guided creation and a "Check for missing issues" action, never by opening the map, and it notes its age after 30 days. |
| Q34 | Exact provider id match first. A series-name + number match only *hides* a ghost (likely owned). Nothing is written. |
| Q35 | A ghost sits in its series' lane right after the owned issue that precedes it in the provider list. A series with nothing owned gets its own lane marked "none owned". |
| Q36 | Add to Wanted tracks the series if needed, then requests the issue. There's also "Add all missing". One confirmation lists any series that will start being tracked. |
| Q37 | A gap check (numbers missing between a lane's first and last) runs only when there's no provider list, and opens a short list. |
| Pitch #12 | Ghosts are off by default ("Show missing" toggle). |

## Design

### 1. Data: `EventArcSnapshot` (one additive migration)

| Column | Type | Notes |
|---|---|---|
| `Id` | int | |
| `StoryEventId` | int | FK, cascade delete |
| `Provider` | string | `Metron` / `ComicVine` (text enum) |
| `ArcId` | string | the provider arc id the list came from |
| `FetchedAt` | DateTime | UTC |
| `ReportedCount` | int? | the provider's own count |
| `IsComplete` | bool | false when paging stopped early or lines were skipped |
| `IssuesJson` | string | ordered list of `{ Series, Number, Year, CoverDate, StoreDate, CoverImageUrl, Title, ComicVineIssueId, ComicVineSeriesId, MetronIssueId, MetronSeriesId }` |

- Unique on `(StoryEventId, Provider)`; a new fetch replaces the row.
- An event merge (`StoryEventMerger`) keeps the newer snapshot per provider.
- Check the generated `Down()` drops only this table (`project_paperbunkr_migration_rollback_orphan_column_bug`).

**Writers** (`EventArcSnapshotWriter.Save(context, eventId, provider, arcId, ArcEntryList)`):
- **Smart fill:** after evidence collection, for each provider list fetched under a *verified* arc identity for that event. Disputed or
  candidate-only identities aren't saved.
- **Guided creation:** when the user picks a provider arc, after Create succeeds.
- **"Check for missing issues"** (map toolbar More menu): fetches for the event's verified arc ids, at Low priority through `MetronHttp` /
  the ComicVine throttle, as an Activity Center job.
  - If the event has no verified arc id, it shows the inline message "Link this event to a Metron or ComicVine arc first" with a button
    that opens Smart fill › Compare sources.
  - On a continuity map it checks every visible event, at most 20 per run.

Nothing fetches on map open.

### 2. Matching (pure, `EventGhostResolver.Resolve(snapshot, members, libraryIndex) → GhostSet`)

- **Which list:** Metron if present, else ComicVine. If both exist, ComicVine entries whose `ComicVineIssueId` matches no Metron entry's
  `cv_id` are added.
- **Per entry:**
  1. **Exact id**, through `ComicMetadataExternalIds` (Metron issue id, else ComicVine issue id), to an owned issue:
     - a member of the event → nothing to show
     - owned but not a member → an **"owned, not in this event"** ghost with an **Add to event** action (an explicit user action
       through `EventMembershipResolver.AddMember`, the only membership write, and only on click)
  2. **Likely owned:** an owned issue whose series name matches (`TitleNormalizer.NamesMatch`, ignore volume, trailing year stripped as
     in the spine resolver), whose normalized number is equal and whose year is within ±1 → hidden, no ghost.
  3. Otherwise a **missing** ghost.

  Nothing is written by matching.
- **Placement (Q35):**
  - Walk the provider list in order and remember the last entry that maps to a member card. A ghost is placed in its series' lane right
    after that card's position (it gets a fractional sort position between the two neighbouring members).
  - A ghost before any member goes before the first card.
  - A series with no member cards gets its own lane (after the last real lane, before the folded tie-ins), with the relation-label slot
    showing "none owned".
- **Layout:** ghosts enter the layout as rows with `IsGhost = true`. They take part in packing (M2) and get chain edges in dashed muted
  ink.
  - They're **skipped** by `Next`/`Prev` reading order for the reader, `FirstUnread` and every read count except "missing".
  - Arrow-key navigation does step onto them, so keyboard users can reach the Add to Wanted action.

### 3. On the map, Matrix and roll-call

**Toggle.**
- "Show missing" (`Button.secondary` toggle, FluentIcon BookQuestionMark) in the toolbar, shown when the map has a snapshot (or, on a
  continuity map, any visible event has one).
- Persisted as `AppSettings.EventMapShowMissing` (bool, default false).
- When on with a snapshot older than 30 days, the status line adds "missing issues checked 41 days ago".

**Ghost cards** (M1 card looks):
- a dashed `PbDangerBrush` border on `PbBgBrush`, no fill, a lane bar at 40 % opacity
- the badge and "Not in your library"; at Standard, the provider cover through a URL-keyed cache (`EntityImageCache.DownloadAndCacheAsync`
  pattern), dimmed 50 %
- "owned, not in this event" ghosts use a dashed `PbBorderBrush` and the text "In your library"

**Inspector for a ghost:**
- the cover, series #number, the provider's cover date, "From Metron · checked 3 days ago"
- buttons: **Add to Wanted** (primary), **Open on Metron/ComicVine** (ghost, external link); for owned-not-member ghosts, **Add to event**
  (primary) and **Open issue**
- Open reader and Mark read are hidden

**Matrix (M3):** the cell's dashed ring and "{m} not owned" in its peer name.

**Roll-call (M3):** a "1 missing" line per series. The corner label adds "· 3 missing".

### 4. Add to Wanted (`EventWantedRequester`, App service over `WantedService`)

- **One ghost:**
  - Find the `WatchedSeries` for the entry's provider series id (Metron series id, else ComicVine series id).
  - If none exists, it will be created through the same path the Wanted screen uses to track a series by provider id.
  - Then request the issue through `WantedService.Request` with a `CatalogIssue` / `ComicVineIssue` built from the snapshot entry: the
    provider issue id, number, title, store date and cover URL.
- **"Add all missing"** (inspector footer and More menu): every missing ghost in the map.
- **Confirmation** (a `DialogCard` Small, only when at least one series isn't tracked yet): "This starts tracking 3 series: Weirdworld,
  Thors, A-Force. Their missing issues go to Wanted." with Cancel / Track and add.
- **Results:**
  - Toasts follow the Activity Center rule (one summary, with Undo removing the WantedIssues and any WatchedSeries this action created).
  - A requested ghost shows a "Wanted" status chip in place of its button.
- A series that can't be matched to a provider volume is reported in the summary ("2 couldn't be matched — open in Wanted to search"). No
  half-created rows are left.

### 5. Gap check (`EventGapCheck`, pure)

- **Runs** only when the event has **no snapshot** (with a snapshot, ghosts are the better answer).
- **Per lane:** the integer issue numbers strictly between the lane's lowest and highest numeric numbers, minus the lane's members.
  - Of those, only numbers the library doesn't own at all in that series count (`CollectionGapResolver` semantics restricted to that
    range).
  - Owned-but-not-member numbers aren't gaps: the event may skip them on purpose.
- **Shown** as a ghost-button note in the toolbar, "Possible gaps · 4", opening a `Border.dropdown` list (`PopupFit` + ScrollViewer).
  - Rows read "Incredible Hulk #94" with **Open series** (Detail) and, when the series is tracked, **Add to Wanted** (through the Wanted
    screen's own missing-issue request).
  - The header says "These may not be part of this event."
- **Continuity maps:** per event block, capped at 50 rows with "and 12 more".

## Testing

**Data:**
- snapshot writer: replace on re-fetch, cascade delete, merge keeps the newer one, incomplete flag
- forward-only migration test
- `EventGhostResolver`:
  - exact-id member (no ghost)
  - exact-id non-member ("in your library")
  - likely-owned name+number hides
  - year window
  - missing ghost
  - Metron-first with ComicVine extras de-duplicated by `cv_id`
  - placement after the preceding member
  - before-first
  - none-owned lane
- `EventGapCheck`:
  - range interior only
  - owned-not-member excluded
  - non-numeric numbers ignored
  - no snapshot required

**App (headless, fake providers):**
- Show missing toggles and persists.
- Ghosts render dashed and are skipped by the reader's Next and by FirstUnread.
- The inspector shows Add to Wanted.
- Add to Wanted:
  - with a tracked series: requests the issue
  - with an untracked series: shows the confirmation, then tracks and requests; Undo removes both
  - an unmatched series is reported, and no rows are left behind
- Add to event writes one membership only on click.
- "Check for missing issues":
  - without a verified id: the message plus the Compare sources link
  - with one: saves the snapshot as an Activity job
- The Gap note lists the expected numbers.

**UI checks:** the lint set; renders of ghost cards at Chips/Standard and the ghost inspector in three themes; the review checklist.

**Not automated:** a live Metron/ComicVine fetch. Use the fakes; the user's on-screen check covers a real event with a verified Metron arc.

## Risks

- **Provider lists are publication-ordered.** The anchoring places ghosts near the right spot without claiming exact reading order; the
  inspector says where the list came from.
- **Creating WatchedSeries is a lasting change.** It's always confirmed and always undoable. It never happens from matching or display.
- **API quota.** Fetches only happen on explicit user actions or inside Smart fill runs, at Low priority through the existing budgets.
- **Shared tree:** the Smart fill code (`EventEnrichment/`, `EventSmartFillRunner`, migration `AddEventSmartFill`) is uncommitted. This
  slice extends it, so build after it's committed.

## Implementation notes (2026-11-12)

Built directly from this design on top of the uncommitted Smart fill code (the user chose not to wait for it to be committed). Code:
Data `Entities/EventArcSnapshot.cs`, `PaperbunkrDbContext.EventArcSnapshots.cs`, `Metadata/EventArcSnapshots.cs` (save, load, merge),
`Metadata/EventGhostResolver.cs`, migration `20261111235213_AddEventArcSnapshots` (the table plus `AppSettings.EventMapShowMissing`, default
off; `Down` drops only those); App `Services/EventMap/EventMissing.cs` (loader, placement, gap check), `EventMissingServices.cs` (Check for
missing issues, Add to Wanted + Undo), `ViewModels/EventMapViewModel.Missing.cs`, ghost drawing in `EventMapCanvas.Polish.cs` /
`.Dots.cs`. Writers: `EventSmartFillRunner` saves the lists it fetched under the event's stored arc ids; guided creation saves the
previewed list once the event holds that identity; `StoryEventMerger` keeps the newer list per provider. Where the build differs:

- **Ghost lanes order by first appearance**, like every other lane, not "after the last real lane".
- **No provider covers yet.** Ghost cards and the ghost inspector draw no cover (the map's cover source reads local files only); the
  inspector shows the source, Add to Wanted / Add to event and "Open on Metron/ComicVine".
- **Undo** for Add to Wanted is an "Undo add to Wanted" toolbar button for the session's last request, not a toast (the Activity Center
  has no undo action). It removes only the wants and tracked series that request created.
- **Add to Wanted creates a WatchedSeries from the snapshot entry alone** (provider series id, name, year) through
  `WantedService.TrackVolume`; it doesn't call the provider. The Wanted screen's own refresh fills in the rest later.
- **The gap list opens the series** (Detail); it has no Add to Wanted, because a bare number has no provider issue id.
- **Hidden-event ghosts:** on a continuity map, ghosts of events hidden with the Events picker aren't placed or counted.
- **Verified:** Data `EventMissingDataTests` (11: save/replace/incomplete, cascade delete, merge keeps the newer list, resolver anchoring by
  id and by name, owned-not-member, likely owned, ComicVine extras de-duplicated by `cv_id`, name matching, forward-only migration and its
  `Down`) and the existing `StoryEventMergerTests`; App `EventMissingTests` (9: placement and negative ids, gap check, Show missing with
  ghosts never read/next, no reader or Mark read on ghosts, Add to Wanted with confirmation and Undo, declining requests nothing, Add to
  event writes one membership only when asked, Check for missing issues needs an arc identity). Not verified: a live provider fetch, a real
  Add to Wanted against the Wanted screen, the look on screen.
