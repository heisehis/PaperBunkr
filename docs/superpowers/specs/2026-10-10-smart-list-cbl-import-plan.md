# CE smart-list `.cbl` import — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md*

Facts read from the code on 2026-10-10 that adjust the spec (the spec is updated to match):

- `SmartListCondition.Field` is stored as a **string** in the database, but `OrganizerProfile.ExcludeRuleJson` serializes `PluginCondition` with default `System.Text.Json`, which writes the enum as a **number**. New members are therefore appended to `SmartListField` and `SmartListOperator`, never inserted. No migration is needed (string column, `MaxLength(32)`; every new name is shorter).
- `SmartListCatalog` deliberately keeps `Number` as **Text**. The real files use `ComicBookNumberMatcher` with the in-range operator ("number 0–1" = one-shot), which a text field cannot express. So a new numeric field `NumberValue` is added and `ComicBookNumberMatcher` maps to it.
- The condition editor builds its field list from `SmartListCatalog.Definitions`, so a new entry there is enough for the UI to offer it. Fields that need library-wide context (the four series fields) have a definition but no selector; `SmartListQueryBuilder.EvaluateCondition` special-cases them the way it does `Duplicate`.
- CE's `GetCustomValue` returns null for a missing key and the matcher compares `value ?? ""`, confirmed in `ComicBook.cs:1810` / `ValuesStore.cs:104` / `ComicBookStringMatcher.cs:70`. Our `EvaluateCustomValue` returns false for a missing value. The fix is in scope.
- Alerts: `IActivityService.RaiseAlert(new ActivityAlert { Severity, Title, Detail, DedupeKey })` (see `ContinuityPageViewModel.cs:240`). The Data layer cannot call it, so the importer returns warnings and the app callers raise one alert per batch.
- Callers of `CblReadingListIO.Import`: `DragImportService`, `NewReadingListViewModel.ImportFileAsync`, `ReadingListPageViewModel.ImportCbl` (Manage), and `ReadingListFolderIO`/`AnnotationExportService` (unrelated or folder-level; checked in step 5).
- Tests: xUnit in `Paperbunkr.Data.Tests`, temp-file SQLite per test class (see `SmartListQueryBuilderTests`), `TestData\` copied via `<None Include=… CopyToOutputDirectory>`. `SmartListQueryBuilder` is `internal`; `Paperbunkr.Data.Tests` has `InternalsVisibleTo`.

Run only the touched test classes while working (project rule): `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~<Class>"`. Never two `dotnet` runs at once.

## Step 1: New fields and series statistics
**Files:** `src/Paperbunkr.Data/Entities/SmartListField.cs` (edit: append `NumberValue`, `Published`, `SeriesLastNumber`, `SeriesMaxYear`, `SeriesMaxCount`, `SeriesMaxGapSize` after `NovelPublished`), `src/Paperbunkr.Data/SmartLists/SmartListCatalog.cs` (edit: five `Definitions` rows; `NumberSelectors[NumberValue]`; `DateSelectors[Published]`), `src/Paperbunkr.Data/SmartLists/SeriesStatistics.cs` (new), `src/Paperbunkr.Data/SmartLists/SmartListQueryBuilder.cs` (edit), `src/Paperbunkr.Data.Tests/SmartListSeriesFieldsTests.cs` (new)
**What:**
- `NumberValue` = `float.TryParse(EffectiveNumber(), invariant)` else -1 (CE `ComicBookNumberMatcher`, "-1 when unparseable").
- `Published` = CE `ComicBook.Published`: null when `EffectiveYear()` is not positive, else `new DateTime(year, clamp(Month,1,12), clamp(Day,1,DaysInMonth))`. `DateSelectors` returns `DateTime?`, so null replaces `DateTime.MinValue` (an unset date never matches, the existing contract).
- `SeriesStatistics` (internal, pure): `Compute(IReadOnlyList<Issue>)` → `Dictionary<(string Series, string Volume), Stats { LastNumber, MaxYear, MaxCount, MaxGapSize }>`, key `(Series?.Name ?? "", EffectiveVolume() ?? "")`, CE `ComicBookSeriesStatistics` formulas: last number = max numeric number else -1; max year = max `EffectiveYear() ?? -1`; max count = max `Count ?? -1`; gaps = sorted positive numbers (including `a-b` integer ranges, skipping the first of the range as CE's `GetRangeNumber` does) where the step is above 1, gap size = the step, `MaxGapSize` = largest step as int, 0 when none.
- `EvalContext` gains `SeriesStats`; `LoadSnapshot` fills it only when a condition uses one of the four fields; `EvaluateCondition` special-cases them, feeding the number to `SmartListLeafEvaluator.EvaluateNumber`.
**Depends on:** none
**Verify:** new tests: per-field evaluation, gap cases (step of 1 = no gap, a jump of 3 = gap 3, ranges, non-numeric numbers ignored, none numeric = -1), grouping by volume, series with no year, `Published` (year ≤ 0, month 0, day 31 in a 30-day month), `NumberValue` ("5", "5.5", "1a", null). Then `SmartListQueryBuilderTests`, `SmartListCatalog*` classes.

## Step 2: Missing custom value compares as empty (CE parity)
**Files:** `src/Paperbunkr.Data/SmartLists/SmartListQueryBuilder.cs` (edit `EvaluateCustomValue`), `src/Paperbunkr.Data.Tests/SmartListCustomValueTests.cs` (new, or added to an existing custom-value test class if one exists)
**What:** `string value = match?.Value ?? string.Empty; return EvaluateText(value, c);`. This changes existing lists that use "Is ''" / "Is not X" on a custom value; call it out in the todo entry.
**Depends on:** none
**Verify:** missing value + Is "" matches; missing + Is "x" does not; missing + IsNot "x" matches (via `Not`/`IsNot`); existing custom-value tests still pass.

## Step 3: `CeSmartListReader` and `CeMatcherMap`
**Files:** `src/Paperbunkr.Data/SmartLists/CeImport/CeMatcherMap.cs`, `CeSmartListReader.cs`, `CeSmartListDraft.cs` (all new), `src/Paperbunkr.Data.Tests/CeSmartLists/CeSmartListReaderTests.cs` (new), `src/Paperbunkr.Data.Tests/TestData/CeSmartLists/*.cbl` (new, ~14 trimmed copies of real files), `Paperbunkr.Data.Tests.csproj` (edit: `<None Include="TestData\CeSmartLists\*.cbl" CopyToOutputDirectory="PreserveNewest" />`)
**What:** exactly the spec's mapping table, with the Step-1 field for `ComicBookNumberMatcher` (`NumberValue`), `SmartListSeriesMaxNumbertMatcher` (`SeriesLastNumber`), `SmartListSeriesMaxYearMatcher`, `SmartListSeriesMaxCountMatcher`, `SmartListSeriesMaxGapSizeMatcher`, `ComicBookPublishedMatcher` (`Published`).
- Operator tables: string 0–7 → Is, Contains, ContainsAny, ContainsAll, StartsWith, EndsWith, ListContains, RegularExpression; numeric 0–3 → Is, GreaterThan, LessThan, InRange; date (`ComicBookDateMatcher`: read its operator list in `_reference` first) → the Date operators; Duplicate 0/1 → Is/IsNot.
- Leaf: `Not`, `IgnoreCase` (default true), `MatchValue` → `Value`, `MatchValue2` → `Value2` for InRange only. Custom values: name = `MatchValue`, value = `MatchValue2`.
- Group: `MatcherMode` → `Mode`; `Not="true"` on a group negated by De Morgan (flip mode, flip each child's `Not`, recurse). The root `<Matchers>` is an implicit AND group. Empty group matches everything.
- Plugin: consult `PluginMatcherRegistry` (Step 4); a miss, an unmapped type, or an operator index out of range drops the condition and adds a warning `{ConditionType, Detail, Effect = Widened|Narrowed}` where Effect depends on whether the parent group is AND (dropped = widened) or OR (narrowed), after any De Morgan flip.
- A file whose every condition dropped yields `Draft.IsEmpty = true`.
- XML is read with `XDocument` and `xsi:type` by namespace, so `ComicBookPluginMatcher` (not in our engine) never reaches `XmlSerializer`.
**Depends on:** Step 1 (field names), Step 4 (registry type; stub it first if needed)
**Verify:** `CeSmartListReaderTests`: one test per mapping row and operator table entry; nested group; `Not` group (property test: evaluate the converted tree against generated issues, compare with the complement of the un-negated tree); custom value with `MatchValue2`; junk `MatchValue2` ignored; dropped condition warning wording; all-dropped file.

## Step 4: `PluginMatcherRegistry`
**Files:** `src/Paperbunkr.Data/SmartLists/CeImport/PluginMatcherRegistry.cs` (new), tests in `CeSmartListReaderTests`
**What:** static registry `PluginKey → Func<string value, string value2, SmartListCondition?>`, empty today; `Register(key, factory)` is the hook pieces C and E use. A key with a factory returning null is still a miss.
**Depends on:** none
**Verify:** registered key produces a condition in the reader; unregistered key warns.

## Step 5: Detect and import
**Files:** `src/Paperbunkr.Data/ReadingLists/CblFile.cs` (new: `Detect`), `src/Paperbunkr.Data/SmartLists/CeImport/SmartListFileImporter.cs` (new), `src/Paperbunkr.Data/ReadingLists/CblImport.cs` (new: `ImportAny`, `CblImportResult`), `src/Paperbunkr.Data.Tests/CeSmartLists/CblImportTests.cs` (new)
**What:**
- `CblFile.Detect(path)` → `ReadingList | SmartList | Invalid` by reading the XML: `<Matchers>` with children and no `<Book>` → SmartList; anything with `<Book>` → ReadingList (books win); no root `ReadingList` or unreadable → Invalid; no books and no matchers → Invalid.
- `SmartListFileImporter.Import(context, path)`: draft → `SmartList { TargetKind = Issue, IsSystem = false, SortOrder = context.SmartLists.Count(), RootGroup }`; name collisions get " (2)", " (3)"; a draft with `IsEmpty` is not saved and returns a result flagged `Refused`.
- `CblImport.ImportAny(context, path)` routes by `Detect` to `CblReadingListIO.Import` or the smart-list importer and returns `CblImportResult { Kind, ReadingList?, SmartList?, Warnings, Refused }`; `Invalid` throws `InvalidDataException` as the reading-list path does today.
**Depends on:** Steps 3–4
**Verify:** `CblImportTests` against a real temp-file DB: reading-list file → reading list as before; smart-list file → a persisted list whose `SmartListQueryBuilder.Build` result matches expectations on a seeded library; a book-and-matcher file → reading list; name collision; refused all-dropped file; Invalid. `ReadingListTests` still pass.

## Step 6: Route the three callers and raise one alert
**Files:** `src/Paperbunkr.App/Services/DragImportService.cs` (edit: `DragImportResult` gains `SmartListsImported` and `ImportWarnings`, defaulted so existing `new DragImportResult(...)` call sites and tests compile; the `.cbl` branch calls `CblImport.ImportAny`), `src/Paperbunkr.App/ViewModels/NewReadingListViewModel.cs` (edit `ImportFileAsync`), `src/Paperbunkr.App/ViewModels/ReadingListPageViewModel.Manage.cs` (edit `ImportCbl`), `src/Paperbunkr.App/ViewModels/ReadingListPageViewModel.Drawer.cs` and `LibraryScreenViewModel.cs` (edit the drop-summary text near the existing `ReadingListsImported` lines), `src/Paperbunkr.App/Services/CeImportAlerts.cs` (new: builds the `ActivityAlert` from warnings, `DedupeKey` per batch), `src/Paperbunkr.App.Tests/DragImportServiceTests.cs` and a new `CeImportRoutingTests.cs`
**What:**
- Drag import: a smart-list file counts toward `SmartListsImported`, warnings collected across the batch, one alert raised by the consumer.
- `NewReadingListViewModel` / `ImportCbl`: if the picked file is a smart list, create it, say so in the status message ("Imported smart list 'X'"), raise the alert if there were warnings, and do not call `_onCreated`/`_openList` (no reading list was created).
- The summary text gains "{n} smart list(s) imported" beside the reading-list part.
**Depends on:** Step 5
**Verify:** `DragImportServiceTests` (existing cases unchanged; new case for a smart-list drop); `CeImportRoutingTests` view-model tests with a fake file picker and `IActivityService`; `ReadingListTests`.

## Step 7: Import command on the Smart screen
**Files:** `src/Paperbunkr.App/ViewModels/SmartScreenViewModel.cs` (edit: `ImportCommand`, uses the existing `_filePicker`/context pattern; check the field name when editing), the Smart screen sidebar header XAML (edit: one button), `src/Paperbunkr.App.Tests/SmartScreenImportTests.cs` (new)
**What:** pick a `.cbl`, `CblImport.ImportAny`; a smart list is added and selected (`LoadSmartList`); a reading-list file is refused with a status message pointing to Reading Lists (it would otherwise silently create one from the Smart screen).
**Depends on:** Step 5
**Verify:** view-model test with a fake picker. The XAML button is not viewed on screen.

## Step 8: Corpus smoke test
**Files:** `src/Paperbunkr.Data.Tests/CeSmartLists/CeSmartListCorpusTests.cs` (new)
**What:** if `PAPERBUNKR_CE_SMARTLIST_CORPUS` names a folder, read every `.cbl` in it with `CeSmartListReader`, assert no exception, and write the warning histogram to the test output; otherwise the test returns immediately (documented skip). Run once locally against the extracted scratchpad copies of the 154 files and record the histogram in the todo entry.
**Depends on:** Step 3
**Verify:** run it with the variable set.

## Step 9: Roadmap and docs
**Files:** `docs/paperbunkr-todo.md` (edit: a dated entry with what was built, what was run, what was not seen on screen, the custom-value behavior change, the date-matchers follow-up), `docs/ce-feature-inventory.md` (edit the smart-list line if it mentions import), `wiki/Smart-Lists.md` or the nearest smart-list wiki page (edit: one paragraph on importing a ComicRack smart list and what is dropped)
**Depends on:** all
**Verify:** read back.

## Final check
`dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj` (build only; CLAUDE.md's XAML-weave caution: if the Smart screen XAML edit is the only XAML change this is a plain edit, no new `x:Class`), then `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~SmartList|FullyQualifiedName~CeSmartLists|FullyQualifiedName~ReadingList"` and `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~DragImportServiceTests|FullyQualifiedName~CeImportRoutingTests|FullyQualifiedName~SmartScreenImportTests"`.
