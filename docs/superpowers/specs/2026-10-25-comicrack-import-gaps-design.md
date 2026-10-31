# ComicRack Import Gaps Design

## Scope

Complete the four remaining ComicRack import gaps tracked under item 4 in `docs/paperbunkr-todo.md`:

1. Import the four CE series-date smart-list matchers and use CE's date-only comparison semantics for every Smart List date field.
2. Import saved ComicRack smart lists from `ComicDb.xml` as part of the existing library migration.
3. Allow a single batch selection of multiple Library Organizer `.lop` files and report each file's outcomes together.
4. Read pre-2.0 Library Organizer profile roots with the original plugin's documented version conversion, preserving behavior where representable and reporting limits.

The numeric `Number` range behavior, static ComicRack reading lists, and any unsupported rule that could change organizing behavior are outside scope.

## Smart List date behavior

All Smart List date operators compare calendar dates, consistent with ComicRack CE's `ComicBookDateMatcher.IgnoreTime = true`: `IsAfter`, `IsBefore`, `WithinLastDays`, and `DateInRange` ignore time components. This applies to existing issue/book date fields and the four new series date fields, including the shared evaluator used by novel Smart Lists. `WithinLastDays` uses the current calendar date as its boundary. An unset date does not match.

Add these issue-targeted fields, retaining append-only enum numbering:

- Series: Last Added
- Series: Last Opened
- Series: Last Published
- Series: Last Released

Each reads the maximum corresponding date across the full library group keyed by CE's `(ShadowSeries, ShadowVolume)` identity, represented by the issue's series name and effective volume. This is a library-wide statistic, not a statistic over issues already filtered by sibling matchers. An empty group value behaves like CE's `DateTime.MinValue` and remains non-matching under the unset-date rule.

## ComicDb.xml smart-list migration

Keep removing `<ComicLists>` before deserializing `ComicDatabase`; unknown plugin matcher types must not prevent migration of books. Separately parse the original XML subtree with the tolerant ComicRack smart-list reader so the existing matcher mapping, warning, and unsupported-condition policy is reused.

Recursively visit ComicRack folders and import only saved `ComicSmartListItem` rules. Ignore ordinary saved reading lists and static ID lists. Prefix each smart list's name with its folder path joined by ` > ` to preserve context in Paperbunkr's flat Smart Lists catalog.

Preview reports smart lists that can be imported, wholly rejected lists, matcher warnings, and name collisions. Existing names are preserved; incoming collisions are suffixed (`Name (2)`, etc.) and called out in preview. Migration commit saves books, series, conflicts, and imported smart lists together through its existing single save operation. A repeated migration may create another suffixed copy; no overwrite or implicit deduplication is introduced.

## Library Organizer `.lop` import

The file picker accepts multiple `.lop` files in one operation. Read each file independently; a file-level error or rejected profile does not prevent other valid profiles from being imported. Produce one combined result with file-specific errors, rejected-profile reasons, notes, and imported counts. Preserve the existing name-suffix collision behavior.

For pre-2.0 `<Settings>/<Setting>` documents, apply the original plugin's version conversion: `Test` mode becomes `Simulate`; legacy EmptyData metadata keys and token names are renamed using the plugin's conversion table; then the profile is parsed through the current model. Import representable values. Profiles that contain rules or settings whose conversion would alter file movement are rejected or clearly reported rather than silently approximated. Retain the upstream warning that 1.6-and-earlier profile support was incomplete.

## UX and failure handling

The ComicRack migration keeps its Preview → Conflicts → Commit flow and displays smart-list counts, collision names, and import warnings alongside existing migration counts. The `.lop` picker displays a single combined outcome. Cancellation before an operation starts writes nothing. Each importer continues past independently invalid profiles/files according to the rules above.

No new global import framework is introduced. Keep the existing `CeSmartListReader`, migration path, `LopProfileReader`, `LopProfileImporter`, profile manager view model, and file-picker abstraction as the integration seams.

## Verification

- Data tests cover all four series dates, full-library grouping, and date-only boundaries for every date operator.
- Migration tests cover nested folders, smart-list-only import, unsupported plugin matchers not blocking books, collision suffixing, and preview/commit persistence.
- `.lop` tests cover multi-file aggregate outcomes, continuing after file errors, pre-2.0 conversions, preserved representable values, and reported/rejected limits.
- Run the focused Data/App tests and build `src/Paperbunkr.App/Paperbunkr.App.csproj` in Debug in the main checkout.
- Review migration UI against the Avalonia checklist, including semantic theme resources, keyboard/tab access, and compact layout.

## Source checks

CE date grouping and date matchers were checked in `_reference/ComicRackCE/ComicRack.Engine/ComicBookSeriesStatistics.cs`, `SmartListSeriesLastAddedTimeMatcher.cs`, `SmartListSeriesLastOpenedTimeMatcher.cs`, `SmartListSeriesLastPublishedTimeMatcher.cs`, `SmartListSeriesLastReleasedTimeMatcher.cs`, and `ComicBookDateMatcher.cs`. The Library Organizer version conversion was checked against the original plugin's `losettings.py`.
