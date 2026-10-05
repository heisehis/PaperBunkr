# CE smart-list `.cbl` import (piece A)

Date: 2026-10-04. Part of [the CE preset audit](2026-10-04-ce-preset-audit-overview-design.md).

## Goal

A ComicRack smart list is a `.cbl` file whose `<Books>` is empty and whose `<Matchers>` holds a matcher tree. Today `CblReadingListIO.Import`
reads only `<Books>`, so every such file imports as an empty reading list, and `CeLibraryMigrator` deliberately strips smart lists from `ComicDb.xml`.
Make a smart-list `.cbl` import as a real Smart List that matches the same books CE would.

Success: the 154 supplied files import; every matcher that has a CE-parity equivalent evaluates the way CE does; anything else is reported, never silently dropped.

## Non-goals

- The four **date** series-level matchers (last added / opened / released / published time). Same pattern, no file uses them; a follow-up.
- Running plugin matchers. A `ComicBookPluginMatcher` maps to a native condition only when a later piece registers one (C, E).
- Exporting smart lists as `.cbl`.
- Smart-list folders (smart lists have none; imports are flat, named as the file names them).
- Migrating a whole `ComicDb.xml`'s smart lists. A follow-up that can reuse the reader.

## Source facts

- File shape (from the 154 files): `ReadingList > Name, Books (empty), Matchers`. `Matchers` is an implicit AND group. A matcher is `<ComicBookMatcher xsi:type="…">`
  with attributes `Not="true"`, `MatchOperator="n"` (0 when absent), `IgnoreCase="false"` (true when absent) and children `MatchValue` / `MatchValue2`. `ComicBookGroupMatcher`
  adds `MatcherMode="Or"` (default And), `Collapsed` (ignored) and a nested `<Matchers>`.
- String operators (`ComicBookStringMatcher`): 0 is, 1 contains, 2 contains any of, 3 contains all of, 4 starts with, 5 ends with, 6 list contains, 7 regex.
  Numeric (`ComicBookNumericMatcher`): 0 is, 1 greater, 2 smaller, 3 in range (inclusive, uses `MatchValue2`). An unparseable numeric value becomes -1.
- `MatchValue2` is **junk** on every single-argument matcher (a Notes matcher carries `MatchValue2="yes"`). Only `ComicBookCustomValuesMatcher` and range operators read it.
- `ComicBookCustomValuesMatcher` uses column 1: the custom-value **name** is `MatchValue`, the value compared is `MatchValue2`.
- Types in the 154 files and their counts: Publisher 246, Format 212, FullPath 130, Title 115, CustomValues 109, Series 98, Group 87, Tags 48, File 17, Plugin 11, Notes 9, Count 9,
  MainCharacterOrTeam 5, FileFormat 5, PageCount 3, SeriesMaxNumber 3, Summary 2, ScanInformation 2, Language 2, SeriesMaxYear 2, Number 2, Duplicate 2, Published 1, Directory 1.
  Plugin keys: `SameXDifferentY` 9, `findProposedValues` 2. Custom-value names: `comicvine_issue`, `DataManager_processed`, `Original Format`, `Format Ok`, `Universe`, `Not Universe`, `Leading Numbers`, `Volume Number`.

## Design

### Components

- **`CeSmartListReader`** (pure, `Paperbunkr.Data/SmartLists/CeImport/`): `XDocument` → `CeSmartListDraft { Name, SmartList (unsaved entity graph), Warnings }`. No database, no UI. Everything below is its job.
- **`CeMatcherMap`**: the single table from `xsi:type` to `(SmartListField, operator-set)` and from operator index to `SmartListOperator`. Adding a type is one row.
- **`PluginMatcherRegistry`**: `PluginKey` → a factory returning a `SmartListCondition` (or null). Empty in this piece. C registers `findProposedValues`; E registers `SameXDifferentY`. A key not in the registry yields a warning.
- **`SmartListFileImporter`** (database): saves a draft as a `SmartList` (`TargetKind = Issue`, not `IsSystem`), appended after the last `SortOrder`. A name already in use gets " (2)", " (3)".
- **`CblFile.Detect(path)`** → `ReadingList | SmartList | Invalid`. Smart list means `<Matchers>` has children and `<Books>` has none. A file with both is a reading list (books win; nothing is lost).
- **`CblImportResult { ReadingList?, SmartList?, Warnings }`**: what the three existing callers (`DragImportService`, `NewReadingListViewModel`, `ReadingListPageViewModel.Manage`) receive. Each routes by `Detect`.

### Mapping

| CE type | Our field | Notes |
|---|---|---|
| Publisher, Format, Title, Series, Tags, Notes, Summary, MainCharacterOrTeam | `Publisher`, `Format`, `Title`, `SeriesName`, `Tags`, `Notes`, `Summary`, `MainCharacterOrTeam` | string operators |
| File, FullPath, Directory, FileFormat, ScanInformation | `File`, `FullPath`, `Directory`, `FileFormat`, `ScanInformation` | string |
| Language | `LanguageISO` | string |
| Count, PageCount, Number | `Count`, `PageCount`, `Number` | numeric; `Number` already parses to float, -1 when unparseable |
| CustomValues | `CustomValue` | `CustomValueName` = `MatchValue`, `Value` = `MatchValue2` |
| Duplicate | `Duplicate` | operator 0 "on" → `Is`, 1 "off" → `IsNot` |
| Published | `Published` (new, below) | |
| SeriesMaxNumber, SeriesMaxYear | `SeriesLastNumber`, `SeriesMaxYear` (new) | |
| `SmartListSeriesMaxCountMatcher`, `SmartListSeriesMaxGapSizeMatcher` | `SeriesMaxCount`, `SeriesMaxGapSize` (new) | no file uses them; CE parity |
| Group | nested `SmartListConditionGroup` | `MatcherMode` → `Mode`; `Not` on a group is applied by negating each child (De Morgan: flip the mode, flip each `Not`) because our group has no `Not` |
| Plugin | `PluginMatcherRegistry` | by `PluginKey`; `MatchOperator` (an index into whatever plugins were installed) is ignored |

Per-leaf rules: `Not` → `Not`; `IgnoreCase` → `IgnoreCase`; `MatchValue` → `Value`; `MatchValue2` → `Value2` only for in-range, otherwise discarded.
The root `<Matchers>` becomes the root AND group. A group with no matchers matches everything (CE and our builder agree). Operator indices outside the table, or a type not in `CeMatcherMap`, add a warning and drop that condition.

A condition dropped from an **AND** group widens the list; dropped from an **OR** group it narrows it. The warning says which, so the user knows which way the list is off.

### New fields (appended to the end of `SmartListField`, so an enum stored by number keeps every existing value)

- `SeriesLastNumber`, `SeriesMaxYear`, `SeriesMaxCount`, `SeriesMaxGapSize` — Number type, group "Series", labels "Series: Last Number / Last Year / Highest Count / Biggest Gap" (CE's descriptions).
  Computed from `ComicBookSeriesStatistics` semantics over the **whole library**, not the narrowed set: group key `(Series?.Name ?? "", EffectiveVolume())`; last number = max of the numeric issue number, -1 when none is numeric;
  max year = max `EffectiveYear`; max count = max `Count`; max gap size = largest difference between consecutive numbers above 1, built from every positive number including range numbers, as a difference rather than a missing-issue count
  (`GetGaps` yields `RangeF(num, i - num)` and `MaxGapSize` takes `(int)g.Length`). Each value is computed once per snapshot into a dictionary on `EvalContext` (as `DuplicateIds` is), only when a condition references one of the fields.
- `Published` — Date type, built from `EffectiveYear`/`Month`/`Day` the way CE's `ComicBook.Published` is: `DateTime.MinValue` when the year is not positive, month and day clamped. Distinct from `Released` (`ReleasedTime`).

These flow to plugins through `PluginCondition.Field` and to the sharing server through the same builder, so both need the new members covered by their versioning/DTO tests.

### An existing-engine fix this piece includes

CE's `ComicBookCustomValuesMatcher` reads `GetCustomValue(name)`, and a missing value compares as the empty string. `SmartListQueryBuilder.EvaluateCustomValue` returns false when the custom value does not exist, so `comicvine_issue is ""`
(the file's "not scraped yet" idiom) matches nothing instead of every unscraped book. Confirm against `ComicBook.GetCustomValue` in the plan's first step; if confirmed, fix it with a test. It changes existing lists, so it is called out here, not folded in quietly.

### Warnings

One Activity Center alert per import batch, using the same alert path other imports use (the plan identifies it), listing for each list: the file name, the dropped condition's type, and "widened" or "narrowed". The importer returns the data; the caller raises the alert.

## Error handling

A file that is not XML, has no `ReadingList` root, or is unreadable becomes `Invalid` and is skipped and counted, exactly as a bad reading-list file is today. A file whose conditions are only partly dropped still imports, with the warning above. A file that has matchers where **every** condition dropped is not imported, and the alert says why: an empty AND list would match the whole library, which is worse than no list. A file that genuinely has no matchers and no books is `Invalid`.

## Testing

- Reader: one test per row of the mapping table and per operator index, using trimmed copies of real files as fixtures (about twelve, covering each type, a nested group, `Not` on a group, a plugin matcher, a custom-value matcher with `MatchValue2`).
- Group negation: property test that a negated group evaluates as the complement of the group over generated issues.
- Series fields: gap cases (single number, ranges, a jump of 1 versus more, non-numeric numbers), grouping by volume, case of series with no numeric numbers.
- `Published`: year ≤ 0, month 0, day 31 in a 30-day month.
- Corpus smoke test: if `PAPERBUNKR_CE_SMARTLIST_CORPUS` points at a folder, import every file and assert no exception, printing a histogram of warnings. Skipped when unset, so CI does not need the files.
- Routing: each of the three callers sends a smart-list file to the smart-list importer and a reading list to the existing path.

## Not verified when this spec was written

How the Activity Center alert is raised, where the three callers surface a created smart list, and whether `SmartListField` is stored by name or by number are unread; the plan's first task reads them. None of the behavior has been seen on screen.
