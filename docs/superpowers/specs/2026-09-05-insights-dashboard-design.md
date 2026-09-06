# Insights Dashboard — design

**Date:** 2026-09-05
**Status:** design — awaiting user review before `writing-plans`
**Topic:** a nav-rail "Insights" screen: actionable reading attention + reading-habit analytics, backed by a new append-only reading-event log.

---

## 1. Summary

Add an **Insights** destination to the nav rail. It answers two questions:

1. *What should I do something about?* — stalled series, series I'm almost done with, holes in
   runs I partly own, arrivals I never opened.
2. *What are my reading habits?* — lifetime totals, completion split, reading pace over time,
   streaks, library composition, my rating distribution.

The habit analytics need history the app does not currently keep. Today the only reading state is
one `Issue.OpenedTime` / `Book.LastOpenedTime` per item, overwritten on every re-open. This design
adds a small append-only **`ReadingEvent`** table, written where read state already changes, plus a
one-time backfill from existing timestamps so the screen is not empty on first launch.

Everything is local and deterministic. No LLM, no network, no external service.

## 2. Goals / non-goals

**Goals**

- One scrolling screen, one nav-rail entry, reachable in one click.
- Actionable section first; curiosity section below.
- Covers the whole library: comics, manga (both `Issue`), and novels (`Book`).
- A durable reading-event log that later features (year-in-review, recommendations tuning) can also read.

**Non-goals (v1)**

- Activity calendar, time-of-day heatmap, "most-read this year", backlog-trend, and format-split
  tiles — deferred; the log will have accrued real history by the time they're built.
- Configurable thresholds / a Preferences surface for any of this.
- Incrementally-maintained aggregate tables. Stats are computed on screen open.
- Live chart restyle when the skin changes mid-session.
- Exporting stats.

## 3. CE-parity note

ComicRack CE has **no** reading-stats screen and **no** reading-history log. Its
`ComicBookSeriesStatistics` / `IComicBookStatsProvider` produce *series* metrics (gap count, page
counts, average rating) surfaced in cover-view grouping — not habit analytics. CE tracked the same
point-in-time `OpenedTime` / `OpenedCount` / `LastPageRead` fields Paperbunkr does.

Therefore: the `ReadingEvent` log and the time-series tiles are a **deliberate deviation** from CE,
justified by the "make the app smarter" goal. The snapshot tiles (completion, composition, ratings,
gaps) stay close to CE's own series-stats concepts. The read threshold reuses CE's exact constant
(`ReadPercentageAsRead = 95`, already `IssueMetadataExtensions.ReadThresholdPercent`).

## 4. Data model — `ReadingEvent`

New entity in `Paperbunkr.Data/Entities/ReadingEvent.cs`. A plain growable table (same category as
`ActivityRun` / `Workspace` / `KeyBinding`), **not** part of `AppSettings`.

```csharp
public class ReadingEvent
{
    public int Id { get; set; }

    /// <summary>Which schema the item lives in. Comics and manga are both Comic.</summary>
    public ReadingItemType ItemType { get; set; }   // Comic | Novel

    /// <summary>Issue.Id when ItemType == Comic, Book.Id when Novel. No FK — the row must
    /// survive deletion of the item it describes (lifetime totals shouldn't drop when a file
    /// is removed from the library).</summary>
    public int ItemId { get; set; }

    public ReadingEventKind Kind { get; set; }      // Opened | Finished

    public DateTime TimestampUtc { get; set; }

    /// <summary>Pages read during the session this row represents. Null for backfilled rows and
    /// until a session's teardown fills it in. An <c>Opened</c> row is updated in place with the
    /// session's page delta on teardown; a <c>Finished</c> row carries the delta up to the finish.
    /// For EPUB novels this is an estimate (§6).</summary>
    public int? PagesRead { get; set; }

    /// <summary>Denormalised so tiles don't need to join back to a possibly-deleted item.
    /// Frozen at write time.</summary>
    public int? SeriesId { get; set; }              // Issue.SeriesId / Book.BookSeriesId, nullable
    public string? Publisher { get; set; }          // effective publisher / novel: null
    public string? PrimaryGenre { get; set; }       // first genre tag / null
}

public enum ReadingItemType { Comic = 0, Novel = 1 }
public enum ReadingEventKind { Opened = 0, Finished = 1 }
```

**Why denormalised `SeriesId` / `Publisher` / `PrimaryGenre`:** the pace and "most-read" style
queries group reading events by series/publisher over a date range. Without the snapshot columns
every query re-joins to `Issue`/`Series` and silently loses rows for deleted items. Freezing the
three fields we group by keeps the log self-sufficient and the queries flat. Everything else
(titles, covers) is looked up live from the item when a tile needs to render it, and simply omitted
if the item is gone.

**Indexes:** `(TimestampUtc)` for range scans; `(ItemType, ItemId)` for "did I already emit a
Finished for this today" checks and per-item history.

**Retention:** none. Rows are kept forever. One row is ~80 bytes; 100 reading sessions ≈ 8 KB. No
startup prune (unlike `ActivityRun`).

**Migration:** `AddReadingEventLog` — creates the table, then runs the backfill (§4.1) in the same
migration's `Up()` via raw SQL / a data-seeding pass.

### 4.1 Backfill (one-time, in the migration)

Best-effort reconstruction from what already exists:

- **Comics/manga** — for every `Issue` with `OpenedTime != null`: insert one `Opened` at
  `OpenedTime`. If the issue also satisfies `HasBeenRead()` (`ReadPercentage() >= 95`): insert one
  `Finished` at `OpenedTime`.
- **Novels** — for every `Book` with `LastOpenedTime != null`: insert one `Opened` at
  `LastOpenedTime`. If `Book.Finished`: insert one `Finished` at `LastOpenedTime`.
- `PagesRead` is `null` for all backfilled rows (historical page deltas are unknowable).
- Snapshot columns are filled from the item's current values.

Consequence the user has accepted: pre-existing history collapses onto single timestamps, so early
pace bars and streaks reflect "when I last touched each book", not true reading cadence. This
self-corrects as real events accrue.

## 5. Event emission

Three reader view-models already own the write paths for read state. Each gains a call into a new
`IReadingEventRecorder` (App-layer service, writes via its own `PaperbunkrDb` context — same
pattern as `IActivityService`).

| Reader | `Opened` | `Finished` | `PagesRead` |
|---|---|---|---|
| `ReaderScreenViewModel` (comic/manga) | in `Load`, alongside the existing `OpenCount++; OpenedTime = now` (`ReaderScreenViewModel.cs:829`) | when a `LastPageRead` write first pushes `ReadPercentage()` from `< 95` to `>= 95` — detected in `GoToPage` (`:1910`) and `FlushPendingPositionSave` (`:1981`) | `max(LastPageRead this session) − LastPageRead at open`, floored at 0, written on the `Finished` row and on session end |
| `BookReaderScreenViewModel` (EPUB) | in `Load` where `LastOpenedTime` is set (`:417` / `:1052`) | when `Book.Finished` transitions false→true (the existing "paged past last chapter" logic) | estimated — see §6 |
| `PdfReaderScreenViewModel` (PDF novel) | on load | on reaching the last page | real page delta |

**Session boundary.** A "session" is one `Load`→(navigate away / close) span in a reader VM. On
teardown (the same hook that flushes the pending position save) the recorder writes a trailing
event carrying `PagesRead` for the session even if no `Finished` occurred, so the "pages read"
metric and reading-day streak capture partial sessions. To keep the table lean this trailing write
is folded into the `Opened` row (updated in place with `PagesRead` on teardown) rather than adding a
third `Kind`. If the app is killed, the `Opened` row simply keeps `PagesRead == null` — acceptable.

**Re-reads.** Finishing an already-finished item emits a *new* `Finished` row every time. Each is a
distinct reading act and counts toward pace / streaks on its own day.

**De-dupe.** None needed. Multiple `Opened` rows for one item on one day is fine (each is a real
open). Streak math operates on distinct dates, not row counts.

## 6. EPUB "pages"

EPUB is reflowed text with no intrinsic page count (`Book` stores `LastChapterIndex` +
`LastCharacterOffset`, not pages). Per the user's decision, estimate:

```
estimatedPages = totalCharacters / 1800
pagesReadThisSession = (charsAtSessionEnd − charsAtSessionStart) / 1800   // floored at 0
```

`totalCharacters` is already computed when the book is parsed for the reader; persist it on `Book`
(`CharacterCount`, new nullable column, populated lazily the same way `ChapterCount` is) so lifetime
"pages" totals don't require re-parsing every EPUB.

The pace chart's "pages" toggle and the lifetime "pages" figure show a footnote: **"pages for
reflowed EPUBs are estimated at ~1,800 characters per page."** PDF novels and comics contribute
real page counts.

## 7. Query layer — `InsightsResolver`

New `Paperbunkr.Data/Metadata/InsightsResolver.cs`, mirroring `HomeFeedResolver` /
`RecommendationResolver`: static, testable, no persistence of its own, takes a
`PaperbunkrDbContext`.

One entry point builds an immutable `InsightsSnapshot` record holding every tile's data for a given
`DateRange`:

```csharp
public static InsightsSnapshot Build(PaperbunkrDbContext ctx, InsightsRange range, DateTime nowUtc);
// InsightsRange: Days30 | Days90 | Months12 | AllTime
```

- One `AsNoTracking()` pass over `Issues` (+ `Series`, `Tags`), one over `Books`, one range-scoped
  pass over `ReadingEvents`. Same shape as `LibraryScreenViewModel`'s snapshot load.
- All computation in memory after that — no per-tile round trips.
- Pure function of `(db state, range, now)` → fully unit-testable with an in-memory context.

**Caching.** `InsightsScreenViewModel` holds the last `InsightsSnapshot` for the session, keyed by
range. It is rebuilt when:
- the screen is opened and no cached snapshot exists for the active range, or
- an `Opened`/`Finished` event fires while the app is running (the recorder raises an event the VM
  subscribes to; it invalidates all cached ranges), or
- the user switches range and that range isn't cached.

No background recompute, no incremental aggregates.

## 8. The screen

### 8.1 Nav-rail entry

- Label **"Insights"**.
- Icon: FluentIcons `DataHistogram`.
- Position: directly below **Home** in the rail.
- Wiring follows the existing pattern in `MainViewModel`: `CurrentScreen == "insights"`,
  `IsInsights` bool, `GoInsightsCommand`, added to the rail-command list
  (`MainViewModel.cs:143`) and the `OnCurrentScreenChanged` notify block, content via
  `ActiveScreenContent`. It's a lateral screen (no contextual sidebar), participating in the
  existing lateral transition system.

### 8.2 Layout (reframed 2026-09-06 — "what to read", not "what to fix")

Header + range selector are a **fixed top row** (not inside the scroll region — matches EventsScreen;
fixes a header-clip found on the first GUI pass).

```
┌ Insights ─────────────────────────────  [30d] [90d*] [12mo] [All time] ┐
│                                                                        │
│  READING                                                               │
│  ┌ Continue ─────┐ ┌ Almost done ┐ ┌ Dive in ──────────┐               │
│  │ 3             │ │ 2           │ │ 6                  │               │
│  │ Saga  12/54 · │ │ Berserk     │ │ Invincible        │               │
│  │   dropped 5wk │ │  1 left     │ │  own 47 · #1–47    │               │
│  └───────────────┘ └─────────────┘ └────────────────────┘  (empty cards hidden)
│                                                                        │
│  AT A GLANCE                                                            │
│  ┌ Read all time ┐ ┌ Reading-day streak ┐ ┌ Finish streak ┐ ┌ 90d ─┐   │
│  │ 1,240 issues  │ │ 11  (best 34)      │ │ 3 (best 9)    │ │ 38   │   │
│  └───────────────┘ └───────────────────┘ └──────────────┘ └───────┘   │
│  ┌ Collection health ─────────────────────────────────────────────┐    │
│  │ 7   ·  7 near-complete runs have a few issues missing           │    │
│  │ Hellboy  #5   ·   Fables  #61, #63   ·   …                       │    │
│  └────────────────────────────────────────────────────────────────┘    │
│                                                                        │
│  ┌ Reading pace ──────────────┐ ┌ Completion ──────────────┐           │
│  │ [ScottPlot bar chart]      │ │ [hand-rolled donut]      │           │
│  │ issues/wk • toggle: pages  │ │ read / in progress / un  │           │
│  └────────────────────────────┘ └──────────────────────────┘           │
│  ┌ Library composition ───────┐ ┌ Your ratings ────────────┐           │
│  │ by: publisher|genre|format │ │ [ScottPlot histogram]    │           │
│  │ |decade  [h-bars]          │ │ 1★…5★                    │           │
│  └────────────────────────────┘ └──────────────────────────┘           │
└────────────────────────────────────────────────────────────────────────┘
```

The range selector (top-right) drives the pace chart, the "finished · last N" stat tile, and the
streak windows' display context. **Lifetime totals, completion donut, library composition, and
ratings ignore the range** (they're absolute).

### 8.3 Tiles

Each tile is a small self-contained control (`Views/Insights/<Tile>.axaml`) bound to one slice of
`InsightsSnapshot`. Attention tiles list up to 3 items with a "+N more" affordance that deep-links.

**READING section** (top; reframed 2026-09-06). Cards are a `WrapPanel` of content-sized tiles;
empty cards hidden; all three empty → one "start anything from the Library" line.

| Tile | Data | Click target |
|---|---|---|
| **Continue** | every series with ≥1 `IsInProgress()` issue (not just stale ones — absorbs the old "Stalled" card). Subtitle = "`{read} of {total} read`", plus "· dropped off `N`wk ago" when the resume issue's last touch is > **21 days** old. Most-recently-touched first. `ReadingStatus.Dropped` excluded. | reader at the resume issue |
| **Almost done** | series started (≥1 `HasBeenRead()` issue) with **1–3** `IsUnread()` issues left; fewest-remaining first. | series detail |
| **Dive in** *(replaces "Untouched arrivals")* | series **never opened** (no issue has an `Opened` event or `OpenCount`) where the owned numeric run: has issue **#1 or #2**, is **≥5** issues, and owns **≥70%** of its min→max span. Biggest owned run first. Subtitle = "own `N` issues · #`min`–`max`, never opened". | series detail |

**AT A GLANCE section**

| Tile | Data | Empty state | Click target |
|---|---|---|---|
| **Collection health** *(demoted from the top 2026-09-06)* | one card, not an alert. `GapCount` + a line ("`N` near-complete runs have a few issues missing"), then the top runs. Gap rule unchanged: series with **≥3** numeric owned issues, `ownership = owned / (max−min+1) ≥ 0.75`, missing count `≤ 10`, fewest-missing first. | "No near-complete runs with holes." | a row → Library filtered to that series |
| **Read all time** | distinct items with ≥1 `Finished` event; total pages across those items (real + estimated, counted once each — re-reads don't inflate this); distinct series count | n/a — shows 0s | — |
| **Reading-day streak** | current run of consecutive calendar days (local time) with ≥1 `Opened` **or** `Finished` event; plus longest-ever | "Start reading to build a streak." | — |
| **Finish streak** | same but days with ≥1 `Finished` event; plus longest-ever | "No finishes logged yet." | — |
| **Finished · last {range}** | `Finished` count + pages within the selected range | "Nothing finished in this window." | — |
| **Reading pace** | `Finished` events bucketed by week (30d/90d) or month (12mo/All), within range; toggle switches the series to pages-read; optional stacked-by-media breakdown on hover | "Not enough history yet — check back after a few reading sessions." | — |
| **Completion** | donut: `HasBeenRead()` / `IsInProgress()` / `IsUnread()` counts across all comics + novels (novel "read" = `Book.Finished`, "in progress" = has position & not finished) | n/a | — |
| **Library composition** | horizontal bars, dimension switch: publisher / primary genre / format / decade (by publication year); counts issues+books | n/a | a bar → Library filtered to that value |
| **Your ratings** | histogram of `Issue.Rating` (1–5, rounded), excludes unrated | "No ratings yet." | a bar → Library filtered to that rating |

**Constants** (fixed in v1, no Preferences surface): stalled-tag threshold = 21 days, almost-done
max = 3, gap ownership floor = 0.75, gap missing cap = 10, dive-in min issues = 5, dive-in
starts-by = #2, dive-in ownership floor = 0.7, read threshold = 95% (existing), EPUB chars/page =
1800, default range = 90 days.

**"Because you read…"** — deliberately **not** in Insights. That's Home's job
(`RecommendationResolver` via `HomeFeedResolver`); Insights is the dashboard, not a second feed.

## 9. Charting

### 9.1 ScottPlot for the two bar charts

Add `ScottPlot.Avalonia` (≥ 5.1.59 — first version with Avalonia 12 support, `Avalonia.Skia >=
12.0.0`; pin an exact version verified against the repo's Avalonia 12.1.1 during planning) to
`Paperbunkr.App.csproj`. SkiaSharp is already present transitively via `Svg.Skia`; align versions.

Used for: **Reading pace** (bar, with a second bar set for the pages toggle / stacked media) and
**Your ratings** (bar histogram).

**Theming.** ScottPlot plots are drawn imperatively. A small `InsightsChartTheme` helper reads the
active skin's resource brushes (`Application.Current.FindResource(...)` for the same keys the rest
of the app binds via `DynamicResource`) at render time and applies them to `Plot.FigureBackground`,
axis/tick/label colors, and the palette. Charts re-render on every navigation *to* the Insights
screen, so a skin changed elsewhere is picked up on next visit. Live restyle while the screen is
visible is explicitly out of scope.

Interaction: pan/zoom disabled — these are display charts, not exploratory plots. Tooltip on hover
for bar values.

### 9.2 Hand-rolled donut

The completion donut is a `Control` with an `OnRender` override drawing three arcs
(`StreamGeometry`, `DrawingContext.DrawGeometry`) from the skin's brushes. ScottPlot's pie output
doesn't match the app's visual language and would need the same theming bridge anyway for worse
results. ~40 lines, themes natively, re-renders on skin change for free (it binds
`DynamicResource`).

Library-composition and attention-list "bars" are plain `Border` width bindings, not charts.

## 10. Testing

- **`InsightsResolver`** — the bulk of coverage. In-memory `PaperbunkrDbContext`, seed
  `Issue`/`Book`/`ReadingEvent` rows, assert every tile's computed slice: streak math across
  date boundaries and gaps, pace bucketing (weekly vs monthly cutover), stalled/almost-done
  threshold edges, gap detection with non-numeric issue numbers mixed in, completion counts across
  both schemas, range-in/range-out filtering, deleted-item rows still counting toward totals.
- **Backfill** — a migration test (there's precedent, e.g. the view-mode remap migration): seed a
  pre-log DB state, run `Up()`, assert the synthesised events.
- **`ReadingEventRecorder`** — `Finished` fires exactly once on the 94→96% crossing, again on a
  re-read; `PagesRead` delta math; EPUB estimate; no event when opening a reader in a headless test
  doesn't crash (`InternalsVisibleTo` + direct calls, same as `FlushPendingPositionSave` tests).
- **`InsightsScreenViewModel`** — cache reuse across range switches, invalidation on a recorder
  event, empty-library and empty-log render without throwing.
- **No full-suite reliance** — targeted `--filter` per the known headless flake.
- ScottPlot rendering itself is not unit-tested (third-party draw path); the theme helper's
  brush-resolution is.

## 11. Decisions resolved during grilling

| # | Decision |
|---|---|
| Data source | Append-only `ReadingEvent` log + one-time backfill (not snapshot-only). |
| Media scope | Comics + manga + novels, one unified screen. |
| Placement | New nav-rail destination "Insights", own screen, below Home. |
| Framing *(orig.)* | Actionable ("Needs attention") first, curiosity ("At a glance") below. |
| **Framing *(reframed 2026-09-06 after 1st GUI pass)*** | For a big mostly-unread library "Needs attention" was noise (untouched = whole library, gaps = bulk-import artefacts). Top section is now **READING** = "what to read next": **Continue** (in-progress, stale ones tagged — absorbs old Stalled), **Almost done** (kept), **Dive in** (never-opened owned full runs — replaces "Untouched arrivals" entirely). **Gaps** demoted to a single **Collection health** card in At a Glance. **"Because you read…"** left to Home, not duplicated here. |
| v1 tiles | READING (Continue / Almost done / Dive in) + Collection health + 6 curiosity (§8.3). Calendar, heatmap, most-read, backlog-trend, format-split deferred. |
| Event shape | `Opened` + `Finished` + per-session `PagesRead`; session-level, not full start/end rows. |
| "Read" | `Finished` = `ReadPercentage() >= 95` (comics) / `Book.Finished` (novels). Re-reads re-emit. |
| Streaks | Reading-day streak (any activity) as headline **and** finish streak as secondary. |
| Charting | ScottPlot.Avalonia for the two bar charts; hand-rolled donut; `Border`-width bars elsewhere. |
| Range | 30d / 90d / 12mo / All; default 90d; lifetime & composition tiles ignore it. |
| Retention | Keep every event forever; no prune. |
| Thresholds | Fixed constants, no Preferences surface (stalled 21d, almost-done ≤3, untouched 7d). |
| Compute | Snapshot on screen open + session cache, invalidated on a new reading event. |
| Multi-media numbers | One lumped count; pace chart offers stacked-by-media view. |
| Pace unit | Issues/period default, pages toggle; weekly buckets ≤90d, monthly beyond. |
| EPUB pages | Estimated at 1,800 chars/page, with a footnote. Novels still fully count toward finishes/streaks/completion. |
| Skin caveat | Charts re-theme on navigation, not live. Accepted. |
| Spec shape | One spec (this doc); `writing-plans` will phase the implementation (log infra → screen). |

## 12. New / changed files (orientation for the plan)

**New**
- `Paperbunkr.Data/Entities/ReadingEvent.cs` (+ `ReadingItemType`, `ReadingEventKind` enums)
- `Paperbunkr.Data/Migrations/*_AddReadingEventLog.cs` (table + backfill)
- `Paperbunkr.Data/Metadata/InsightsResolver.cs` + `InsightsSnapshot` record + `InsightsRange` enum
- `Paperbunkr.App/Services/ReadingEventRecorder.cs` (+ `IReadingEventRecorder`)
- `Paperbunkr.App/ViewModels/InsightsScreenViewModel.cs`
- `Paperbunkr.App/Views/InsightsScreen.axaml` (+ `.axaml.cs`) and `Views/Insights/*Tile.axaml`
- `Paperbunkr.App/Views/Insights/CompletionDonut.cs` (hand-rolled control)
- `Paperbunkr.App/Services/InsightsChartTheme.cs`
- Tests mirroring the above

**Changed**
- `Paperbunkr.Data/Entities/Book.cs` — add `int? CharacterCount`
- `Paperbunkr.Data/PaperbunkrDbContext.cs` — `DbSet<ReadingEvent>`, model config, `Book.CharacterCount`
- `Paperbunkr.App/ViewModels/MainViewModel.cs` — `insights` screen wiring
- `Paperbunkr.App/Views/MainWindow.axaml` — rail button, screen host
- `Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` — recorder calls at open + finish-crossing
- `Paperbunkr.App/ViewModels/BookReaderScreenViewModel.cs` — recorder calls + `CharacterCount` persist
- `Paperbunkr.App/ViewModels/PdfReaderScreenViewModel.cs` — recorder calls
- `Paperbunkr.App/Paperbunkr.App.csproj` — `ScottPlot.Avalonia` package
- `Paperbunkr.App/App.axaml` / DI composition root — register `IReadingEventRecorder`

**Roadmap**
- `docs/alpha-todo.md` — record the new tile in the Beta backlog once shipped; the "smarter app"
  brainstorm's other candidates (gap detection as its own surface, OCR search) remain unstarted.
