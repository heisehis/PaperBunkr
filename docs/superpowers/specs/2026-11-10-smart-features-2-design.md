# Smart features 2 — design

Date: 2026-11-10. Source: "Smart features pitch 2" in `docs/Paperbunkr-Roadmap.md`, items #1 ("Previously on…" card), #3 ("I've got 30
minutes") and #17 (natural-language search). Grilled over two rounds with the user; every decision below was confirmed. The three layouts
were chosen from mockups in the visual companion (recap: docked left panel; time budget: Up Next switch + dialog; search: "Understood as"
row under the toolbar).

## 1. Scope

One spec, three independent slices. Each gets its own implementation plan and its own on-screen check; they can be built in any order.

| Slice | Pitch item | Surface |
|---|---|---|
| A | #1 "Previously on…" card | Comic/manga reader |
| B | #3 "I've got 30 minutes" | Home Up Next, Quick Open (Ctrl+P), a dialog, the reader's next-issue flow |
| C | #17 natural-language search | Library search box (All mode) |

**Out of scope:** books in A and B (the book reader already has a jump-back chip and a session summary, and has a history of overlays not
drawing over its page; books use a separate per-book estimate that does not fit 15-60 minute budgets). Books search in C (the Books screen
has its own simpler search, `BooksBrowseQuery`). Both can follow later.

**ComicRack CE** (checked in `_reference/ComicRackCE`):
- A and B have no CE counterpart: CE has no recap, no reading pace and no Home screen. Both are deviations.
- C has two CE behaviours Paperbunkr lacks today, and this slice adds both:
  - CE's quick search splits the query into words and uses "contains all of" (`ComicBookAllPropertiesMatcher.Create(query, 3, …)`,
    `ComicBookStringMatcher.cs:90-94`). Paperbunkr matches the whole query as one substring.
  - In All mode, text starting with `MATCH` or `NOT` is parsed as CE's textual smart-list query
    (`ComicBrowserControl.UpdateQuickFilter`, `ComicBookGroupMatcher.CreateMatcherFromQuery`); parse errors fall back to plain search.
  - **Deliberate deviation:** CE needs every word to appear *in the same field*. Paperbunkr lets words match across fields, because the
    point of this slice is combining fields ("hickman avengers" would otherwise match nothing).
  - The natural-language parse itself is new; CE has none.

## 2. Foundations

**Code placement.** Logic lives in resolvers in `Paperbunkr.Data/Metadata` (next to `UpNextResolver`, `ReadingPaceResolver`) or a
`Paperbunkr.App/Services/LibrarySearch` class for C; view models and views in `Paperbunkr.App`. Every resolver and the parser are pure
functions with unit tests.

**Schema:** one change only.

| Slice | Change |
|---|---|
| A | `AppSettings.RecapGapDays` (int, default 21; 0 = off). Hand-edit the generated `Down()` to leave the column (the rollback-chain bug, see the `AddSmartFeaturesSettings` precedent). |

B and C add no columns. B's default pace values are constants in code.

**UI rules.** Every surface follows the `paperbunkr-design-language` skill: tokens only, shared control families, `DialogCard` for the dialog,
`Reveal.IsOpen` for the recap panel, Reveal-role motion. Each slice runs the `avalonia-pro-max/review-checklist` and the lint set
(`XamlConsistencyLintTests`, `ThemeHardcodedColorLintTests`, `SharedControlsStyleTests`) before it is called done. Keys go through
`IInputService` (no `KeyDown` hotkeys).

## 3. Slice A — "Previously on…" card

### 3.1 When it shows

On opening an issue in the comic/manga reader, when all of these hold:
- `RecapGapDays > 0`.
- The series' last reading activity is at least `RecapGapDays` days old. Last activity = the later of the newest `ReadingEvent` with that
  `SeriesId` and the newest `Issue.OpenedTime` in the series (the same inputs `SeriesProgress.Of` uses).
- At least one earlier item (3.2) has been finished.
- The card has something to show: at least one earlier item has a summary, or the resume row applies.

**Ordering trap:** `ReaderScreenViewModel.Load` overwrites `OpenedTime`/`OpenCount` and writes a new Opened event. The recap must be
computed **before** those writes, or the gap is always zero. A test pins this.

Resuming a half-read issue after a gap qualifies (it is when a recap helps most).

### 3.2 What it shows

`RecapCardResolver.Build(context, issueId, readingListId?, nowUtc, gapDays)` returns `RecapCard?`:
- **Earlier items:** the (up to) 3 finished items closest before the opened one, displayed oldest at the top and newest last (so the row
  just above the opened issue is at the bottom).
  - Opened from a reading list: the list's earlier items, in list order.
  - Otherwise: earlier issues of the series by `NumberSortKey`.
  - "Finished" = a Finished `ReadingEvent` for the issue, or `HasBeenRead()` (95 % rule) for issues read before the log existed.
  - Each row: cover, issue label, `Issue.Summary`. A row without a summary shows cover and label only.
- **Resume row** ("Where you stopped"): only when the opened issue is in progress (`LastPageRead > 0` and not read). Thumbnail of the last
  page seen and its page number.
- **Characters:** names parsed from the earlier items' `Issue.Characters` (`CharacterResolver.ParseNames`), counted across those items,
  `MainCharacterOrTeam` first, then by count; at most 5.
- **Header:** "Previously in <series or list name>", "Welcome back", "Last read N days ago".
- **Never** the opened issue's own summary (summaries can spoil; see `InfoPanelShowSummary`).

### 3.3 Presentation

- A panel docked on the reader's left edge, about 320 px wide, inset 12 px, full height minus insets; `Border.card` styling. The page stays
  visible and undimmed; in a narrow window the panel overlaps the page edge (no relayout of the page).
- Shown with `controls:Reveal.IsOpen="{Binding IsRecapVisible}"`, Reveal role, 20 px panel distance from the left.
- Closes on: Esc (the reader's Escape action handler closes the recap first and returns true), the panel's ✕ (`Button.iconButton`, named
  "Close recap"), or the first page turn in either direction.
- Never takes focus and never consumes page-turn keys (slice A rule from the 2026-09-21 reader flow spec).
- Footer link "Turn off recaps" (`Button.ghost sm`) opens Preferences › Reader at the recap setting.
- Shown once per open; reopening the same issue the same day does not show it again (the gap is now zero).

### 3.4 Setting

Preferences › Reader: "Recap after a break" with Off / 7 / 14 / 21 / 30 / 60 days (`SuggestBox`), default 21, backed by
`AppSettings.RecapGapDays`. Indexed in `PreferenceIndex` so search finds it.

### 3.5 Tests (A)

Resolver: gap below/at/above threshold; setting 0 = never; series order vs reading-list order; finished-by-event vs finished-by-95 %; no
summaries and no resume row = null; opened issue's summary never included; character ranking and cap. View model: the gap is read before
`OpenedTime` is written; first page turn closes; Esc closes and is claimed; the panel never takes focus. Headless render in default,
daylight and matrix themes.

## 4. Slice B — "I've got 30 minutes"

### 4.1 Entry points

- **Home › Up Next header:** a `segGroup` reading "I've got" + 15 min / 30 min / 60 min (`segItem`). Clicking one opens the dialog (4.3). The
  switch is not persisted; it shows no selection after the dialog closes. Hidden when the Up Next section is hidden or empty.
- **Quick Open (Ctrl+P):** a query-aware built-in entry, "I've got N minutes", offered when the query parses as a duration: `30`, `30 min`,
  `30m`, `45 minutes`, `1h`, `1h30`, `time 45`. Range 5-240 minutes. Today only plugin commands receive the query text; this adds the same
  for one built-in entry (a duration parser like the reader palette's `TryParsePageNumber`).

### 4.2 The planner

`TimeBudgetPlanner.Plan(context, minutes, nowUtc)` returns `TimeBudgetPlan { Picks, TotalMinutes, IsRoughEstimate }`:
1. **Candidates**, in order: in-progress comic/manga issues (Continue Reading, most recent first), then `UpNextResolver.Build` with a larger
   limit (20). For an Up Next series, the next unread issues of that series may follow each other, at most 3 in a row.
2. **Time per item:** `ReadingPaceResolver` (`TimeLeftInIssue` for in-progress, `TimeFor` for unread). When the user has fewer than the 5
   qualifying sessions the pace needs, use defaults — **comics 20 s/page, manga 12 s/page** — and set `IsRoughEstimate`.
3. **Fill:** walk the candidates and add each item whose addition keeps the total at or under budget × 1.10; skip one that would not and keep
   trying later (shorter) items. Stop at the end of the candidates.
4. **Nothing fits:** `Picks` is empty and the plan carries the single shortest candidate as `ShortestAlternative`.

Books are excluded.

### 4.3 The dialog

`DialogCard`, Size Medium. Title "Your next N minutes"; subtitle "Half-read issues first, then Up Next · based on your pace" (or "…rough
estimate until you've read a few sessions" when `IsRoughEstimate`).
- One row per pick: cover, label, reason ("Finish it · 9 pages left" / "Next in series"), time ("~14 min").
- Total row: "3 issues · ~32 min".
- `FooterStart`: "Save as reading list" (`Button.secondary`) — creates a normal reading list named "Next 30 minutes · <date>".
- `Footer`: Cancel (`ghost`), "Start reading" (`primary`).
- Nothing fits: "Nothing fits in 15 minutes. The shortest is Saga #13, about 22 min." with "Read it anyway" as the primary.

### 4.4 Reading the picks

"Start reading" opens the first pick in the reader with the picks as an in-memory ordered queue, walked the way a reading-list context is
walked: the end card's "Next" goes to the next pick; after the last pick the end card behaves as normal. The queue ends when the reader is
closed. Nothing is saved. **To verify while planning:** how the reader receives a reading-list context today; if it needs a saved list id,
the fallback is a hidden temporary reading list deleted when the reader closes.

### 4.5 Tests (B)

Planner: in-progress first; Up Next order; 3-in-a-row cap; the 10 % rule including skip-and-continue; default pace and the rough flag;
nothing fits → shortest alternative; books excluded. Duration parser table. Dialog view model: Save creates a reading list with the picks in
order. Reader: Next walks the queue and stops at its end. Keyboard test for the Up Next switch in the real window.

## 5. Slice C — Natural-language search

Applies to the Library search box in **All** mode only. The other modes (Series, Writer, …) keep their current behaviour, including the
`writer:` prefix syntax.

### 5.1 Plain search catches up with CE

- The free text is split into words on whitespace and `,;`; every word must appear (case-insensitive, ordinal, against the existing
  per-item `LibrarySearchIndex` string). Words may match different fields (the deviation in §1).
- Double-quoted text is one exact phrase: `"x-men red"` matches the old way.
- Text whose trimmed start is `MATCH` or `NOT` is parsed with the CE parser already in `Paperbunkr.Engine`
  (`ComicSmartListItem.TokenizeQuery`, `ComicBookGroupMatcher.CreateMatcherFromQuery`) and its matchers mapped to SmartList rules with
  `CeMatcherMap` (the `.cbl` importer's mapping). On a parse error the text is searched as plain words and the chip row shows "Couldn't
  read this as a query; searching as text".

### 5.2 The natural-language parser

`NaturalQueryParser.Parse(text, vocabulary)` returns `ParsedQuery { Rules, FreeWords, Phrases, Spans }`, where each rule is a
`SmartListTemplateRule` (flat AND, the template catalog's shape) and each span records which characters it consumed (for click-to-revert).

Recognised phrases (version 1):

| Kind | Examples | Rule |
|---|---|---|
| Read state | unread, read, reading, finished, in progress | ReadPercentage per 5.4 |
| Content type | manga, comics, manhwa, manhua | ContentType Is |
| Publisher / imprint | marvel, vertigo | Publisher / Imprint Is |
| Creator | by hickman, written by X, drawn by X | any creator role / Writer / Penciller+Artist contains |
| Genre / tag | horror, slice of life | Genre / Tags list contains |
| Character | with batman, featuring storm | Characters list contains |
| Year | 2023, 2020-2023, 1990s, before 2000, after 2015, from 2023 | Year Is / InRange / LessThan / GreaterThan |
| Rating | rated 4+, 5 stars | Rating GreaterThan/Is |
| Added | added this week, added last month, added today | Added WithinLastDays |
| Format | tpb, one-shot, annual, hardcover | Format Is |

Rules:
- **Vocabulary** comes from the Library's in-memory suggestion index (built once per data load), not `MetadataVocabularyService` (it
  re-queries the database on every call). Publishers, imprints, creators, genres, tags, characters, formats, and series names (used only to
  detect ambiguity).
- **Longest match first** for multi-word names ("slice of life", "jonathan hickman").
- **Creators:** a surname that matches exactly one creator resolves to the full name (chip shows "Jonathan Hickman"); several matches stay a
  "contains" rule on the typed text.
- **Ambiguity:** a word that is both a character and a series name stays free text unless "with"/"featuring" precedes it. Genre/tag words
  that are also series words stay free text. When in doubt, leave it as text.
- Connector words consumed only as part of a phrase (by, from, with, featuring, rated, added) and nowhere else.
- Unrecognised words are `FreeWords` and go through 5.1.

### 5.3 Evaluation

- The parsed rules become a transient `SmartList` (`TargetKind` Issue, one AND group) evaluated with `SmartListQueryBuilder.Evaluate`
  against a snapshot loaded **once per Library data load** (`LoadSnapshot`), on the existing background pipeline worker. Free words filter
  through the search index as in 5.1. Both combine (AND) with the Lens tabs and the filter chips.
- Series cards match when any of their issues match (current behaviour).
- Live search and a saved smart list use the same rules, so they cannot disagree.
- Performance gate: parsing < 1 ms per keystroke at p95 on a 10k-issue vocabulary; the whole recompute stays inside the existing 150 ms
  debounce + background pipeline. A perf test asserts the parse budget.

### 5.4 One read-state rule

- **Unread:** no pages read (ReadPercentage 0).
- **Read:** ReadPercentage ≥ 95 (CE's rule, `HasBeenRead`).
- **Reading:** anything in between.

The Library Lens switches to this rule (today it counts `LastPageRead` null or 0 as unread). Behaviour change: an issue opened but never
turned past its first page moves from Unread to Reading. Note this in the changelog.

### 5.5 The "Understood as" row

- A row under the toolbar's search line, visible only when the query has at least one recognised phrase or a `MATCH` query/parse note.
  Wraps on narrow windows (`WrapPanel`); at 1366×768 it shares width with the toolbar.
- Label "Understood as" (`pbTextCaption`, muted). One chip per rule: `Button.filterChip.on` with a muted kind label and the value
  ("Publisher Marvel", "Creator Jonathan Hickman"). Clicking a chip removes its rule and puts its span back as literal text (it will not be
  re-parsed: the reverted word stays literal while it is in the box, and the mark is dropped when the box is cleared).
- Leftover words as muted text: + "avengers".
- Right side: "Save as Smart List" (`Button.ghost sm`) and "Search as plain text" (`Button.ghost sm`, a toggle; while on, the whole query is
  plain words and the chips hide). Neither is persisted.
- Motion: the row uses Replace on content change; no entrance animation per keystroke.

### 5.6 Save as Smart List

Creates a `SmartList` (Issue target) from the rules, plus one rule for the free words (AllProperties "contains all of" with the words),
named after the query text (editable in the usual rename flow), wrapped in a Collection with `CollectionService.SetIssueSmartList` so it
appears in the Library sidebar, then selects it. A `MATCH` query saves its mapped rules the same way.

### 5.7 Tests (C)

Parser table (each phrase kind, longest match, creator resolution, ambiguity fallbacks, connector words, mixed queries). Word split and
quoted phrases. `MATCH`/`NOT` mapping and the parse-error fallback. Read-state rule shared by Lens and chips. Save produces a list whose
`MatchCount` equals the live result count. Perf gate. Headless render of the row in three themes, wrapped and unwrapped.

## 6. Implementation notes

(Filled in while building: deviations from this spec, premises found wrong, and what was verified on screen.)
