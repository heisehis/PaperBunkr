# Detail layouts v2: the Library's list-layout powers on comic and manga detail screens

Date: 2026-10-19. Follows `2026-10-18-detail-screen-list-layouts-design.md` (v1: fixed field sets, one default per detail type,
menu in the Library Workspace popup). Decisions settled in a grilling pass (rounds 1-2, every recommendation accepted).

## Goal

A detail layout can do what a Library list layout does: any of the Library's issue fields, three caption lines, tile text
elements, cover fit, sort, group, draggable column headers, starter layouts, and a per-series override.

CE has no series detail screen, so there is nothing to match directly; the parity anchor is CE's list layout
(`DisplayListConfig`: columns, `ThumbnailConfig.CaptionIds`, `ComicTextElements`, sort and group), already ported for the Library.

## Decisions

| # | Decision |
|---|---|
| Q1 | Comic List columns, Poster captions and Card lines use `IssueListFieldCatalog` (the Library's 64 fields). Manga chapter columns get the same catalog plus the chapter-only keys. |
| Q2 | Poster: up to 3 caption lines (any catalog field) + Hide captions. Read status and edition stay cover overlays with their own switches. |
| Q3 | Card: the Library's ordered `TileTextElement` list (Title is the headline; the rest join into the second line), plus the read-status switch. |
| Q4 | Poster and Card take `LibraryGridCoverFit` (Poster / Panorama / Tiles aspect handling of the cover). |
| Q5 | Sort by any sortable catalog field, either direction. Comic default: Number ascending. Manga default: Number ascending. |
| Q6 | Group. Comic: Run (default, today's behaviour), None, or any catalog group field. Manga: Volume (default), None, or any catalog group field. |
| Q7 | Comic List and manga Chapters get a header row: drag a column's edge to resize, drag a header to reorder. List Options keeps Move up/down as the keyboard path. |
| Q8 | One default per detail type, plus an opt-in per-series override ("Use only for this series") and "Set on all series". No automatic per-series memory. |
| Q9 | Starter layouts: Comic "Cover wall", "Checklist table", "Reading cards"; Manga "Compact chapters", "Full chapter table". Seeded once. |
| Q10 | Book series detail is out of scope. |
| Q11 | Per-series control lives in the Library's series right-click menu: **Detail layout ▸** named layouts / **Follow the default**. "Set on all series" lives in the Workspace popup's Detail screen layouts section. No layout buttons on detail screens. |
| Q12 | Specials share the Issues tab's sort and stay ungrouped. |
| Q13 | Saved v1 layouts upgrade silently; nothing is reset and the built-in default looks as it does today. |

## Data

`DetailListLayoutState` keeps its record and JSON home (`ListLayoutAssignments` / `ListLayouts` rows under
`WorkspaceScreen.ComicDetail` / `MangaDetail`; no migration). Column fields become string keys so one column list can hold catalog
fields and detail-only keys, and so v1 JSON (`"Field":"FullTitle"`) still deserializes:

- `ComicColumns`, `ChapterColumns`: `DetailListColumn(string Field, bool Visible, double Width)`. A key is an `IssueListSortField`
  name with a `Display` projection, or a detail-only key: comic `ReadStatus`; manga `NewStatus`, `ReadStatus`, `Bookmark`,
  `Missing`, `Progress`.
- New: `CaptionFields` (≤3 `IssueListSortField`), `HideCaptions`, `ShowReadOverlay`, `ShowEditionOverlay`, `CardElements`
  (`TileTextElement` list), `ShowCardStatus`, `CoverFit`, `SortField` + `SortDirection`, `GroupBy` (string: `run` / `none` /
  `IssueListGroupField` name), `ChapterSortBy` + `ChapterSortDirection`, `ChapterGroupBy` (`volume` / `none` / group field name).
- v1 fields (`PosterFields`, `CardFields`, `ChapterSortField`) are read only to upgrade, then dropped on the next save.

Upgrade aliases: comic `FullTitle→Title`, `ArcTitle→StoryArc`, `CoverDate→Released`; manga `Title→Title`, `Date→Released`.
Poster v1 `Number`/`ArcTitle` become caption lines in that order; `ReadStatus`/`Edition` become the overlay switches. Card v1
`FullTitle→Title`, `NumberAndProgress→Number`, `ReadStatus→ShowCardStatus`. Chapter v1 `Date` sort becomes `Released`.

## Rendering

Each detail tile/row carries an `IssueListRow` built with `IssueListRow.FromIssue` (the Library's own row), so caption, column and
tile text come from `ListLayoutText.Field` / `TileSecondLine`, and sorting/grouping from `IssueListFieldCatalog`'s comparers and
group keys. Run and Volume grouping keep their existing headers. Header cells are a per-screen collection; a resize commits on
release, a reorder on drop, both through the same path as List Options, then `ListLayouts.TrackChange()`.

## Per-series override

The detail host's `LayoutSelectionKey` is `series:{id}` when that series has its own assignment, else the default key, so
changes made on screen (sort pills, header drags) land where the series' layout lives. `ListLayoutsViewModel` gains:
set-as-default (the Workspace popup rows; re-applies only when the shown series follows the default), set-for-series,
follow-default and set-on-all-series (clears every `series:*` row, then sets the default).

## Testing

JSON upgrade and normalization; catalog-backed cell text, sort and group on both detail view models; per-series resolution and the
four new layout commands; starter seeding runs once; theme tokens only (`Pb*`), keyboard path for every drag.
