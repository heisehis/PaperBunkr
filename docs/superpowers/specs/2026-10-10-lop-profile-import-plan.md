# Library Organizer `.lop` profile import — Implementation Plan
*Implements: docs/superpowers/specs/2026-10-04-lop-profile-import-design.md*

Facts read from the code on 2026-10-10 that adjust the spec (the spec is updated to match):

- The organizer UI is `ProfileManagerViewModel` / `ProfileManagerView.axaml` in `Paperbunkr.App/Scraper/` (Preferences → Organize). It builds the profile list from `OrganizerProfileStore.GetAll()`, saves through `_store.Save`, and checks templates with `TemplateValidator.Validate(template, allowEmpty)` (folder template allows empty, file template does not). The "New profile" button sits under the profile list (`ProfileManagerView.axaml:118`).
- `IFilePickerService` has **no multi-select**: `PickOpenFileAsync(title, extension, label)` returns one path. So the Import button picks one file (which may hold several profiles); several files arrive through drag-and-drop. The spec's "multi-select" is dropped.
- `ExcludeRuleJson` is `JsonSerializer.Serialize(PluginConditionGroup)` with default options (`ProfileManagerViewModel.cs:256`), read back with `JsonSerializer.Deserialize<PluginConditionGroup>`. `SmartListField`/`SmartListOperator` serialize as numbers.
- `PluginCondition(Field, Op, Value, Value2, CustomValueName, VirtualTagId, SearchMode, Not, IgnoreCase)`; `PluginConditionGroup(Mode, Conditions, ChildGroups)` has no group-level `Not`.
- `OrganizerMode` is `Move | Copy | Simulate`, the plugin's `Mode` strings. `OrganizerProfile` has every field the spec maps (`UseFolder`, `UseFileName`, `EmptyFolder`, `EmptyData`, `FailEmptyValues`, `FailedFields`, `ExcludeFolders`, `RemoveEmptyFolders`, `MonthNames`, `BaseFolder`, `UseForScheduledRun`).
- `Sanitizer`'s illegal-character table equals the plugin default exactly (`Sanitizer.cs:18-26`). The naming evaluator has no space-collapse switch to compare against, so `AutoSpaceFields` / `ReplaceMultipleSpaces` = false is simply reported.
- The plugin's `name_to_field` table is read (50 entries). Plugin rule semantics (`locommon.py:292-363`): text fields compare case-sensitively; `is` on a numeric field compares a number with a string and is never true in the plugin; `contains`/`does not contain` on a numeric field would raise; only `greater than`/`less than` work on numbers (int compare). That decides which rules translate (Step 1).
- Drag import's consumers show counts in `LibraryScreenViewModel.cs:2676` and `ReadingListPageViewModel.Drawer.cs:228`.
- Tests: `Paperbunkr.Data.Tests` (xUnit, temp SQLite) for the reader/converter/importer; `Paperbunkr.App.Tests/OrganizerUiTests.cs` already constructs `ProfileManagerViewModel` (lines 353, 516, 621, 644) — its new optional constructor parameters must default.

Run only touched classes: `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~Lop"`, `src/Paperbunkr.App.Tests --filter "FullyQualifiedName~OrganizerUiTests"`. One `dotnet` at a time.

## Step 1: `ExcludeRuleConverter`
**Files:** `src/Paperbunkr.Data/Organizing/Import/ExcludeRuleConverter.cs` (new), `src/Paperbunkr.Data/Organizing/Import/LopFieldMap.cs` (new), `src/Paperbunkr.Data.Tests/Organizing/ExcludeRuleConverterTests.cs` (new)
**What:** input is a small DTO tree (`LopRule { Field, Operator, Value }`, `LopGroup { Operator, Rules }`, plus root `Operator` and `ExcludeMode`) so the converter has no XML in it. Output `ExcludeConversion { PluginConditionGroup? Group, string? RejectedBecause }`.
- Field map (plugin display name → `SmartListField`, kind):
  text → `Format`←"Format", `Publisher`, `Imprint`, `Genre`, `Tags`, `Teams`, `Characters`, `Locations`, `Notes`, `Review`, `Writer`, `Penciller`, `Inker`, `Colorist`, `Letterer`, `Editor`, `CoverArtist`, `Web`, `AgeRating`, `StoryArc`, `SeriesGroup`, `AlternateSeries`, `AlternateNumber`, `ScanInformation`, `LanguageISO`←"Language", `MainCharacterOrTeam`←"Main Character Or Team", `Title`, `SeriesName`←"Series", `File`←"File Name", `FullPath`←"File Path", `FileFormat`, `Number`←"Number";
  numeric → `Count`, `Year`, `Month`, `Day`, `Volume`, `Rating`, `AlternateCount`, `ReadPercentage`;
  toggle → `SeriesComplete`, `BlackAndWhite` (values `Yes`/`No`; `Unknown` rejects).
  Anything else (`Counter`, `End Year`, `End Month`, `Start Year`, `Start Month`, `First Letter`, `Released Date`, `Added Date`, `Manga`) rejects.
- Operators: text fields accept `is`, `is not`, `contains`, `does not contain` (`IgnoreCase = false`: the plugin is case-sensitive); `greater than`/`less than` on a text field reject. Numeric fields accept only `greater than`/`less than` (`is`, `is not`, `contains`, `does not contain` reject with "the plugin never matches this rule"). Toggle fields accept `is`/`is not`.
- Tree: root `Operator` Any/All → Or/And; groups recurse; empty groups drop; no rules → `Group = null`.
- `ExcludeMode = Only`: invert by De Morgan over the whole tree (flip each group's mode, flip each condition's `Not`).
- Any untranslatable rule: `RejectedBecause` names the field/operator and no group is returned.
**Depends on:** none
**Verify:** `ExcludeRuleConverterTests`: each table row, every rejection, empty/null cases, group flattening, `Only` inversion on single rule, nested group, mixed. **Equivalence test:** a test-local port of `check_metadata_rules` / `ExcludeGroup.book_should_be_moved` (count/total, None-skip, Any/All, Do not/Only), then over generated books and generated rule trees assert that the books the converted group matches (evaluated through `SmartListQueryBuilder` over a SQLite-seeded library) are exactly the books the reference port says to **skip**.

## Step 2: `LopProfileReader`
**Files:** `src/Paperbunkr.Data/Organizing/Import/LopProfileReader.cs`, `LopReadResult.cs` (new), `src/Paperbunkr.Data.Tests/Organizing/LopProfileReaderTests.cs` (new), `src/Paperbunkr.Data.Tests/TestData/Lop/*.lop` (new: the 10 supplied files, `Events.lop` once; `.lop` extension needs its own `<None Include>`), `Paperbunkr.Data.Tests.csproj` (edit)
**What:** `Read(Stream)` → `LopReadResult { Profiles: [ImportedProfile { OrganizerProfile Profile, IReadOnlyList<string> Notes }], Rejected: [{ Name, Reason }], string? FileError }`.
- Root `Profile` (one) or `Profiles` (several); legacy `Settings`/`Setting` → `FileError` "pre-2.0 profile files aren't supported".
- Mapping per the spec table; `Months` stored only when different from `FieldResolvers.DefaultMonthNames`; `UseForScheduledRun = false`; `Mode` parsed case-insensitively, unknown → profile rejected.
- Notes (strings): non-default `IllegalCharacters`; non-empty `Prefix`/`Postfix`/`Seperator`/`TextBox`/`ExcludedEmptyFolder`; `MoveFileless`/`MoveFailed` true; `AutoSpaceFields`/`ReplaceMultipleSpaces` false; `CopyMode` true with `Mode` Copy; a Move profile → "Check the base folder before running: <path>"; template validation problems via `TemplateValidator.Validate` (folder `allowEmpty: true`, file `allowEmpty: false`) prefixed "Folder template:"/"File template:".
- `ExcludeRules` → DTO tree → `ExcludeRuleConverter`; a rejection rejects the profile; success stores `JsonSerializer.Serialize(group)` in `ExcludeRuleJson` (null when no group).
**Depends on:** Step 1
**Verify:** `LopProfileReaderTests`: all 10 supplied files read (counts, templates, mode, base folder, notes); the five `Only` profiles carry an inverted rule that, evaluated, selects the intended books; a hand-made file with an untranslatable rule is rejected; `Profiles` root with two profiles; pre-2.0 root; non-XML.

## Step 3: `LopProfileImporter`
**Files:** `src/Paperbunkr.Data/Organizing/Import/LopProfileImporter.cs` (new), `src/Paperbunkr.Data.Tests/Organizing/LopProfileImporterTests.cs` (new)
**What:** `Import(OrganizerProfileStore store, Stream)` / `Import(store, path)`: reads, makes names unique against `store.GetAll()` (" (2)", " (3)", also within the file), saves each accepted profile, returns `LopImportSummary { Imported: [(OrganizerProfile Saved, Notes)], Rejected, FileError }`. Performs no filesystem access beyond opening the given file.
**Depends on:** Step 2
**Verify:** against a temp-file SQLite store: names collide and get suffixes; `Events.lop` twice → second becomes "Events (2)"; rejected profiles are not saved; saved profiles have `UseForScheduledRun` false.

## Step 4: Import button in the profile manager
**Files:** `src/Paperbunkr.App/Scraper/ProfileManagerViewModel.cs` (edit: optional `IFilePickerService? filePicker` constructor parameter; `ImportProfilesCommand`; `ImportNotes` collection and `ImportStatusMessage`), `src/Paperbunkr.App/Scraper/ProfileManagerView.axaml` (edit: an "Import…" button beside "New profile" and a notes list under the profile list), `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit the `new Scraper.ProfileManagerViewModel(...)` call at line 168 to pass `_filePicker`), `src/Paperbunkr.App.Tests/LopImportUiTests.cs` (new)
**What:** `ImportProfilesCommand` picks a `.lop`, calls `LopProfileImporter`, `Refresh()`, selects the first imported profile, and fills `ImportStatusMessage` ("Imported 3 profiles; 1 skipped") and `ImportNotes` ("<profile>: <note>" lines, then "<profile> was not imported: <reason>" lines). Cleared on the next import or when another profile is selected is NOT required; they simply persist until replaced. The base-folder note is the reminder; no field highlight (dropped from the spec as unwarranted for a note already shown).
**Depends on:** Step 3
**Verify:** `LopImportUiTests` with a fake file picker (the existing `OrganizerUiTests` fakes show the pattern) and the existing `OrganizerUiTests` class still passing (new constructor parameter defaults to null). The XAML is not viewed on screen.

## Step 5: Drag and drop `.lop`
**Files:** `src/Paperbunkr.App/Services/DragImportService.cs` (edit: a `.lop` bucket; `DragImportResult.ProfilesImported` and `ImportNotes`, defaulted), `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` and `ReadingListPageViewModel.Drawer.cs` (edit the summary text), `src/Paperbunkr.App.Tests/DragImportServiceTests.cs` (edit: new case)
**What:** a dropped `.lop` is imported with `LopProfileImporter` against `new OrganizerProfileStore(_contextFactory)`; counted, notes returned; one alert is raised by the consumer when there are notes or rejections (reuse the step-6 helper from the smart-list plan if present, otherwise add `LopImportAlerts` next to it). A bad file is counted and skipped like a bad reading-list file.
**Depends on:** Step 3 (and the smart-list plan's `DragImportResult` edit if it landed first; merge by hand)
**Verify:** `DragImportServiceTests` new case plus the existing ones.

## Step 6: Roadmap and docs
**Files:** `docs/paperbunkr-todo.md` (edit: dated entry; what was built, what ran, what was not seen on screen, the decisions: no illegal-character table, reject-on-untranslatable, single-file picker), `wiki/Scraping-and-Organizing.md` (edit: one paragraph on importing Library Organizer profiles and what is reported)
**Depends on:** all
**Verify:** read back.

## Final check
`dotnet build src/Paperbunkr.App/Paperbunkr.App.csproj`, then `dotnet test src/Paperbunkr.Data.Tests --filter "FullyQualifiedName~Lop|FullyQualifiedName~ExcludeRuleConverter|FullyQualifiedName~LibraryOrganizerService"`, `dotnet test src/Paperbunkr.App.Tests --filter "FullyQualifiedName~OrganizerUiTests|FullyQualifiedName~LopImportUiTests|FullyQualifiedName~DragImportServiceTests"`. Editing `ProfileManagerView.axaml` adds no new `x:Class`, so the weave caution in CLAUDE.md does not apply; check the build output is 0 errors and that the existing view still loads in the headless tests.
