# Insights → History tab — design

Date: 2026-09-29. Status: ~~approved in brainstorming, not yet implemented~~ **Built 2026-09-29, uncommitted, not yet seen on screen** (see "Implementation notes" at the end). Plan:
[2026-09-29-insights-reading-history-plan.md](2026-09-29-insights-reading-history-plan.md).

A Mihon/Tachiyomi-style reading **History**: a newest-first list of what you've been reading, one row per
series, with resume / open / remove actions, living as a new tab on the Insights screen.

## CE parity

ComicRack CE has **no** history view and no event log (verified in `_reference/ComicRackCE`). What it has:
two per-book fields, `OpenedTime` / `OpenedCount` (`ComicRack.Engine/ComicBook.cs:315,334`); a File →
"Recent Books" menu (top 20 by `OpenedTime`, `ComicDatabase.GetRecentFiles`, `Settings.RecentFileCount = 20`);
a default "Recently Read" smart list (`OpenedTime` within 14 days, `ComicLibrary.cs:286-292`); Quick Open's
Reading / Recently Read / Recently Added groups (10 each); and a coarse "group by Opened" date bucketer
(`cYo.Common/ComponentModel/GroupInfo.cs:57-87` — Today / Yesterday / … / This Week / Last Week). CE has **no**
"remove from history" or "clear history" action; the only reset is `MarkAsNotRead()` zeroing `OpenedTime` /
`OpenedCount`. This tab is therefore a deliberate Paperbunkr-original deviation, like the rest of Insights.

## Why this shape

Paperbunkr already records every reading act in the append-only `ReadingEvent` log (2026-09-05 Insights
design). It's indexed on `TimestampUtc` and on `(ItemType, ItemId)`. What's missing is a surface that
shows it as a history, plus the two things the log can't do today: remember an item's name after the item is
deleted, and hide rows without deleting them. Stats, Goals, Recap, `ReadingListForecast` and the Insights
Today tab all read the same log, so deleting rows would quietly rewrite them. History hides rows instead.

Existing "recent" surfaces this does **not** replace: Quick Open's empty-query recents (8 items), Home's
Continue Reading (in-progress only, 10), the series detail Activity tab (20 events for one series).

## Decisions (grilling log)

| # | Question | Decision |
|---|---|---|
| Q1 | Row granularity | **One row per series, ever (Mihon-style).** Each series appears once, at its most recent read. Reading it again moves the row to the top. Comic series, book series and standalone books are each a "series". |
| Q2 | "Remove from history" | **Hide only.** Sets a flag on the log rows. Stats, streaks, goals and forecasts are unaffected. |
| Q3 | Clear all | **Yes.** Hides every row, behind a confirm dialog that says stats are kept. No "clear older than X". |
| Q4 | Content scope | **One list for comics, manga and books,** with squircle filter chips: All · Comics · Manga · Books. |
| Q5 | Row actions | Clicking the row opens series or book detail. ▶ resumes. Right-click → Resume/Read next, Open series, Remove from history. |
| Q6 | PDF resume | The PDF reader saves no position, so PDF rows show **"Open"** instead of ▶. Saving PDF position is a separate backlog item. |
| Q7 | Search | **Yes.** A title filter box at the top of the tab. |
| Q8 | Long lists | **One virtualized list,** everything loaded at once (row count is bounded by distinct series read, not by events). No "Load more". |
| Q9 | Day headers | **One per local day:** TODAY, YESTERDAY, a weekday name within the last 7 days (TUESDAY), then SEP 14, with the year added when it isn't the current year. |
| Q10 | Items deleted from the library | **Shown greyed out,** with a "NO LONGER IN LIBRARY" tag. |
| Q11 | Tab placement | **Today · Trends · History · Recap.** The range chips are hidden while History is selected. |
| Q12 | Incognito mode | **Not now** (backlog). |
| Q13 | Row layout | As in the approved mockup (§4). |
| Q14 | What ▶ does | If you stopped partway, resume that issue. If you finished it, read the next issue in series order. If there's no next issue, the row shows "CAUGHT UP" and has no ▶. |
| Q15 | Remove granularity | **One action:** remove the whole series. There's no per-chapter fall-back. Reading the series again makes it reappear. |
| Q16 | Names for greyed rows | Snapshot `SeriesTitle` + `ItemLabel` onto each log row at write time. A one-time backfill covers rows whose item still exists. Rows whose item was deleted *before* this ships stay nameless and are **skipped**. |
| Q17 | When a row is greyed | Only when the **whole series (or standalone book)** is gone. If just the last-read issue was deleted, the row falls back to the newest read issue still in the library. Remote-library items whose source was removed are greyed the same way. |
| Q18 | Book series | Collapse into one row per book series, like comics. Standalone books get one row each. |
| Q19 | Navigating from History | Doesn't record anything. Only opening a reader writes events. |
| — | "Read next" for books | `Book` has **no series position column**, so a book series has no defined order. A finished book row shows FINISHED and has no ▶, and there's no "read next" for books. (Found while specifying Q14. The fix belongs to whenever book-series ordering gets designed.) |

## 1. Data model

One migration, `AddReadingHistoryColumns`, on `ReadingEvents`:

| Column | Type | Meaning |
|---|---|---|
| `SeriesTitle` | `TEXT NULL` | Frozen display name of the row's group at write time: `Series.Name` for comics, `BookSeries.Name` for a book in a series, `Book.Title` for a standalone book. |
| `ItemLabel` | `TEXT NULL` | Frozen per-item label: for comics/manga `#{EffectiveNumber()}`, else `Vol. {Volume}`, else `Issue.Title`, else null. For books, `Book.Title` when in a book series, otherwise null (the title is already `SeriesTitle`). |
| `HiddenFromHistory` | `INTEGER NOT NULL DEFAULT 0` | Set by Remove / Clear all. Read **only** by the History resolver. Every other consumer ignores it. |

- **Entity:** `ReadingEvent` gains `SeriesTitle`, `ItemLabel` (both `string?`) and `HiddenFromHistory` (`bool`). The class doc
  comment is updated: the log is still append-only for *what happened*. The one-time `PagesRead` fill and
  the `HiddenFromHistory` flag are the only in-place writes.
- **Backfill:** `ReadingHistoryBackfill` in `Migrations/` is a static SQL-statement class, the same shape as `ReadingEventBackfill`, so
  it can be unit-tested. It `UPDATE`s `SeriesTitle`/`ItemLabel` from live `Issues`/`Series` and
  `Books`/`BookSeries` for rows whose item still exists (remote rows included, since the migration runs raw SQL
  with no query filters). `ItemLabel` for comics in SQL follows the same precedence as the C# formatter, using the
  stored `Number` column (`EffectiveNumber()` may consult metadata proposals; the backfill accepts plain
  `Number`. The difference only matters for issues whose number came from a proposal, and it is cosmetic).
- **`Down()`:** ~~drops nothing~~ really drops the three columns (changed during implementation). The orphan-column
  rule behind the no-op Downs (`AddNavRailHoverExpandEnabled.Down`) is about AppSettings/Issues, whose SQLite rebuild
  would lose unmapped legacy columns. `ReadingEvents` has none, so its rebuild is safe.
- **Index:** none added. The resolver reads the whole non-hidden log grouped in memory. §2 explains why that's fine.

### Recorder change

`ReadingEventRecorder.Insert` fills `SeriesTitle`/`ItemLabel` itself before saving, looking the item up by
`(ItemType, ItemId)` in the same short-lived context with `IgnoreQueryFilters()`. **No interface or caller
change.** The three reader VMs keep calling `RecordOpened`/`RecordFinished` exactly as today. A failed lookup
(the item was deleted mid-session) leaves both columns null and still inserts the row. It never throws into the
reader. `IReadingEventRecorder` also gains:

```csharp
/// Sets HiddenFromHistory on every existing row of one history group (or all rows when group is null).
void HideFromHistory(ReadingHistoryGroupKey? group);
```

It raises `ReadingEventRecorded` afterwards, so History (and anything else listening) reloads. `ReadingHistoryGroupKey`
is a small record `(ReadingHistoryGroupKind Kind, int Id)` where `Kind` is `ComicSeries | BookSeries | Book`.

## 2. Resolver — `ReadingHistoryResolver`

`src/Paperbunkr.App/Services/History/ReadingHistoryResolver.cs`, static,
`Resolve(PaperbunkrDbContext ctx) → IReadOnlyList<ReadingHistoryRow>`. It lives in App rather than Data because
"next issue in series order" must use the same `IssueOrdering.OrderByNumber()` the reader uses
(`ReadingOrderResolver`'s series branch), and that extension is App-side.

**Steps:**
1. Load the non-hidden log rows, projecting only `Id, ItemType, ItemId, Kind, TimestampUtc, SeriesId, SeriesTitle, ItemLabel`.
   Loading the whole table is what Stats/Insights/Recap already do on every refresh (~80 bytes/row).
2. Group by `ReadingHistoryGroupKey`:
   - Comic rows use `ComicSeries(SeriesId)`.
   - Novel rows use `BookSeries(SeriesId)` when `SeriesId` is non-null, else `Book(ItemId)`.
   - **Always key on `ItemType` as well.** Comic and book series ids share one integer space. This is the same trap
     as the Activity-tab bug fixed separately on 2026-09-29.
3. Per group, order events newest-first by `(TimestampUtc, Id)`, and batch-load the live items/series they reference with
   `IgnoreQueryFilters()` (`Series`+`Issues`, `BookSeries`, `Books`).
4. **Pick the row's item:**
   - The newest event whose item still exists.
   - If none exists but the group's series/book still does, the group is live with no resumable item. This is rare
     (all read issues deleted) and is treated as live with ▶ = none.
   - If the group's series/book itself is gone, the row is **greyed**. It uses the newest event's snapshot
     `SeriesTitle`/`ItemLabel`, and is **dropped** if `SeriesTitle` is null (pre-feature deletions, Q16).
5. **Row fields:**
   - Display: title (live name, falling back to the snapshot), item label (live, falling back to the snapshot), `LastReadUtc` (the newest event's timestamp, from any event in the group, not only a surviving item's), cover key (series → `SeriesCardSample.FromSeries(s).CoverKey`; book → `CoverFingerprint.Stem(book.Id, book.FilePath, null)`; greyed → null), and `ContentKind` (Comic / Manga via `IsMangaFamily(Series.ContentType)` / Book).
   - Status: `IsInLibrary`, `IsFinished` (from the item's **live** read state, not from the log, because mark-as-read from a context menu writes no event: comics use `issue.ReadPercentage() >= IssueMetadataExtensions.ReadThresholdPercent`, books use `Book.Finished`; greyed rows use whether the newest event is `Finished`), and `ProgressText`. `ProgressText` is `page N of M` for an in-progress comic from `LastPageRead`/`PageCount`, `NN%` for an EPUB from `LastProgressionFraction`, and null otherwise.
   - Action: `Action` (see below) and `TargetId` (issue id or book id for the action), plus `BookFormat` for books.
6. Return rows sorted by `LastReadUtc` descending.

**The ▶ action (`ReadingHistoryAction`):**

| Row | Action |
|---|---|
| Greyed | `None` |
| Comic/manga, item not finished | `Resume` → that issue |
| Comic/manga, item finished, a next issue exists in `OrderByNumber()` order that isn't `FileIsMissing` | `ReadNext` → that issue |
| Comic/manga, item finished, no next | `None` + `IsCaughtUp` |
| Book, PDF | `Open` → that book |
| Book, EPUB/FB2/other reflow, not finished | `Resume` → that book |
| Book, finished | `None` (no book-series order, see the decision log) |

Search and filter are applied in the view-model, not the resolver. They're cheap in-memory filters over the resolved list, so
typing never re-queries.

**Cost:** it is one pass over the log plus one batched load of the referenced series/issues/books. Row count is bounded by
distinct groups (≤ library size). Next-issue ordering runs per finished comic row over already-loaded issues,
with no extra query. The plan includes a resolver test at ~500 groups / 20k events to keep this honest.

## 3. View-model — `HistoryTabViewModel`

`src/Paperbunkr.App/ViewModels/HistoryTabViewModel.cs`, owned by `InsightsScreenViewModel` as `History`,
the same pattern as `Stats` / `Recap` / `Goals`.

**Constructor:** `(Action<int> goReaderForIssue, Action<int> goDetailForSeries, Action<int, BookFormat> goReaderForBook, Action<int> goBookDetailForBook, IDialogService dialogs, IReadingEventRecorder? recorder, IActivityService? activity, Func<PaperbunkrDbContext>? contextFactory, Func<DateTime>? nowUtc)`.
`InsightsScreenViewModel`'s constructor gains the two book delegates. `MainViewModel` passes
`GoBookReaderForBook` / `GoBookDetailForBook`, as it already does for Home.

**Tabs:** `InsightsScreenViewModel` gains `IsHistoryTabSelected` + `SelectHistoryTabCommand`, mirroring Trends/Recap.
The three flags stay mutually exclusive, and `IsTodayTabSelected` becomes `!Trends && !History && !Recap`. Selecting History
sets `History.IsActive = true` and calls `History.Refresh()`.

**State:**
- `SearchText` and `TypeFilter` (`All | Comics | Manga | Books`). Either change rebuilds the visible items from the cached rows.
- `Items`: an `ObservableCollection<object>` that interleaves `HistoryDayHeader` and `HistoryRowItem`. It's a flattened list
  because Avalonia has no native sticky/grouped headers in a virtualized `ListBox`/`ItemsRepeater`
  (`avalonia-pro-max/layout-patterns`). Headers scroll with the content.
- `IsEmpty` / `IsFilteredEmpty` drive two empty states: "Nothing read yet" and "No history matches".

**Day headers:**
- Grouping key: the local date of `LastReadUtc`, computed via `nowUtc` so tests are deterministic.
- Labels: `TODAY`, `YESTERDAY`, the uppercase weekday name for dates 2–6 days ago, else `MMM d` uppercase, and `MMM d, yyyy` when the year differs from the current year.
- The static helper `HistoryDayLabel.For(DateTime localDate, DateTime localToday)` is unit-tested.

**Commands:**

| Command | Behavior |
|---|---|
| `OpenRow(row)` | Greyed → no-op. Comic group → `goDetailForSeries(seriesId)`. Book series → book detail of the row's book. Standalone book → `goBookDetailForBook`. |
| `PlayRow(row)` | `Resume`/`ReadNext` comic → `goReaderForIssue(TargetId)` (same entry Home's Continue Reading uses, so it respects `AppSettings.OpenLastPage`). Book → `goReaderForBook(TargetId, format)`. `None` → no-op. |
| `RemoveRow(row)` | `recorder.HideFromHistory(row.GroupKey)`. The collection change comes from the reload that `ReadingEventRecorded` triggers, **deferred one dispatcher tick** via `Dispatcher.UIThread.Post`, because the click originates from a control inside a row of this list (CLAUDE.md "don't remove/detach a control from inside a routed event it's still raising"). |
| `ClearAll()` | `dialogs.ConfirmAsync("This hides every entry from History. Your stats, streaks and goals are kept.", title: "Clear reading history?", confirmLabel: "Clear")` → `recorder.HideFromHistory(null)`. The reload is deferred the same way. |

**Loading:**
- `Refresh()` runs `ReadingHistoryResolver.Resolve` on a background task with a fresh context and marshals the result to the UI thread.
- The `ReadingEventRecorded` subscription reloads only while `IsActive`; otherwise it just marks the cache stale, the same "cheap unless on show" rule `InsightsScreenViewModel` already uses.
- A stale in-flight load is discarded by a load-generation counter.

**A ▶ target that vanished between load and click:** the reader delegates already cope with a missing issue/book by not opening.
History then reloads on the next `ReadingEventRecorded` or tab visit. No special handling is needed.

## 4. View — `InsightsHistoryView`

`src/Paperbunkr.App/Views/Insights/InsightsHistoryView.axaml` + `.axaml.cs`, added **together in one step**
(CLAUDE.md "Build gotcha: adding a new Avalonia View"). `InsightsScreen.axaml` gains the `History` tab
button between Trends and Recap, and hosts the view when `IsHistoryTabSelected`. The Stats range selector's
`IsVisible` excludes History.

Layout, matching the approved mockup (`.superpowers/brainstorm/935977-1790583264/content/row-anatomy-d.html`):

- **Toolbar row:**
  - A `TextBox` watermark "Search history…".
  - Four squircle chips using `Classes="pbChip"` / `PbRadiusChip`, never `CornerRadius 999` (All · Comics · Manga · Books), with the active chip accent-filled like `rangeChip.active`.
  - An icon button (FluentIcons `Delete`) with tooltip "Clear all history", disabled when empty.
- **List:** a `ListBox` (virtualizing by default) with no selection visuals, bound to `Items`, using a `DataTemplate` per item type:
  - `HistoryDayHeader` → a `sectionLabel`-styled `TextBlock` (11px SemiBold, muted, letter-spaced).
  - `HistoryRowItem` → a 3-column `Grid`:
    - A `CoverThumb` at the row size (32×46). Greyed rows get a hatched placeholder `Border` instead.
    - A title + subtitle stack. The title has an optional tag chip: `FINISHED` (success tint), `CAUGHT UP` (success tint) or `NO LONGER IN LIBRARY` (amber/warning tint). The subtitle is `ItemLabel · ProgressText · h:mm tt`, plus `· PDF` for PDFs.
    - The action button: ▶ (FluentIcons `Play`, tooltip "Resume {label}" / "Read next: {label}"), or an `Open` text button for PDFs, or nothing.
  - Greyed rows render at `Opacity 0.45` with no hand cursor, and clicking them is a no-op.
- **Context menu:** built with the shared `MenuFlyout` mechanism (context-menu rebuild), with items Resume / Read next / Open (per action; hidden when `None`), Open series (hidden for greyed rows), a separator, and Remove from history.
- **Theming and entrance:** all colors come from `Pb*Brush` `DynamicResource`s (no hex), so skins restyle it live. The tag tints reuse existing success/warning brushes. There's no entrance animation on rows, since virtualized recycling would replay it (`avalonia-pro-max/motion`).
- **Empty states:** centered muted text. "Nothing read yet — open something from the Library and it'll show up here." / "No history matches your search."

## 5. Error handling

- **Resolver:** failures are caught in `Refresh()`. They're logged and surfaced through the Activity Center (`IActivityService.RaiseAlert`, "Couldn't load reading history"), following the project rule of using the Activity Center for notifications. The list keeps its previous contents.
- **Hiding:** a failed `HideFromHistory` also raises an Activity Center alert. The row stays, because the reload never ran.
- **Recorder snapshot lookup:** failures never block the insert (§1).
- **Missing or remote items:** handled by the greyed state, never an exception.

## 6. Testing

- **`ReadingHistoryResolverTests`** (App.Tests, temp SQLite DB):
  - Grouping: one row per series; a newer read moves a series up; comic series 5 and book series 5 stay separate groups.
  - Standalone books and book-series collapsing.
  - Hidden rows excluded; a new read after hiding makes the row reappear, with only post-hide events counted.
  - Fallback to the newest surviving issue when the last-read issue was deleted.
  - Greyed row when the series is deleted (uses snapshot names); dropped when the snapshot is null.
  - Remote-source items resolve (`IgnoreQueryFilters`); removing the source greys the row.
  - The ▶ decision table (resume / read-next / caught-up / PDF open / finished book).
  - Manga vs comic `ContentKind`.
  - Perf smoke: ~500 groups / 20k events resolve under a generous bound.
- **`ReadingHistoryBackfillTests`** (Data.Tests): titles and labels filled for live items (comic with number, comic with volume only, book in series, standalone book); rows for deleted items left null; idempotent.
- **`AddReadingHistoryColumnsMigrationTests`**: the columns exist after `Up`. Following the documented up-down-up antipattern note, it doesn't assert a rollback round-trip that the no-op `Down()` can't honour.
- **`ReadingEventRecorderTests`** additions: snapshot names are filled on insert; a missing item still inserts with nulls; `HideFromHistory(group)` hides only that group and only `ItemType`-matching rows; `HideFromHistory(null)` hides all; both raise `ReadingEventRecorded`.
- **`HistoryTabViewModelTests`**: `HistoryDayLabel` cases (today / yesterday / weekday / same-year date / other-year date); header interleaving; search + type filter; `OpenRow`/`PlayRow` route to the right delegate per row kind; greyed rows are no-ops; `ClearAll` asks for confirmation and does nothing on cancel; reload happens on `ReadingEventRecorded` only while active.
- **`InsightsScreenViewModelTests`** additions: the three-way tab exclusivity includes History; `IsTodayTabSelected` logic.
- **UI review:** run `avalonia-pro-max/review-checklist` on the new view before calling it done. Also do a full App `-t:Rebuild` (new View), and launch to confirm the XAML weave.

## 7. Out of scope → backlog

- Saving the PDF reader's position, so PDF rows get a real ▶ Resume.
- A book-series reading order (`Book` series position), so finished books get ▶ Read next.
- Incognito / private reading (don't record while reading).
- "Clear history older than X".
- Making the Trends heatmap's days clickable to jump into History.

## Implementation notes (2026-09-29)

Built to this design. The only deviations, and additions found while building:

- **`Down()` is real** (§1 above, and the reason for it).
- **`HiddenFromHistory` has a SQL default** (`HasDefaultValue(false)`): without one, raw-SQL inserts that don't name the
  column fail. That includes the older `ReadingEventBackfill`, and its test caught it. `false` is the CLR default, so the EF
  sentinel gotcha doesn't apply.
- **Labels are one formatter**, `Paperbunkr.Data.Metadata.ReadingHistoryLabels`, shared by the recorder's snapshot and the
  resolver's live rows. It lives in Data, next to `IssueMetadataExtensions`.
- **Manga check shared:** `MainViewModel.IsMangaFamily` now delegates to `Models/ContentTypeFamily.IsManga`.
- **Book covers:** these don't go through `CoverThumb`. That control's `AsyncCoverImage` reads the issue-thumbnail cache,
  while books use the separate `BookCoverImageCache`, via `BookCoverImageConverter` as elsewhere.
- **Tag chip position:** FINISHED / CAUGHT UP / NO LONGER IN LIBRARY sits at the right end of the title line, not
  directly after the title. An inline chip can't also ellipsis-trim a long title in a plain Grid/StackPanel.
- **Active filter chip text:** uses `PbBadgeTextBrush` (the app's dark text-on-accent token). The existing
  `rangeChip.active` style uses `PbAccentTextBrush` (accent-coloured text on an accent fill). That was left alone here, but it's
  worth a look.
- **Background loading:** `HistoryTabViewModel` takes an injectable background runner and dispatcher post. Tests run the
  resolver synchronously, because the pinned-thread headless framework can't survive a real `Task.Run` await (Event Map note).
- **Headless PNG check:** the tab was rendered headlessly and inspected. The search box was centred and the scrollbar overlapped
  the tags; both are fixed. The ▶ / Open targets are 36×36 per `avalonia-pro-max/review-checklist`.
