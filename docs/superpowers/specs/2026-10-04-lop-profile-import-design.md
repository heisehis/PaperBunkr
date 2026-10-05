# Library Organizer `.lop` profile import (piece B)

Date: 2026-10-04. Part of [the CE preset audit](2026-10-04-ce-preset-audit-overview-design.md).

## Goal

Read the profile files the Library Organizer plugin (2.1.x) saves and turn them into `OrganizerProfile` rows, so a user migrating from ComicRack keeps their organizing rules.
Our profile already holds the plugin's templates, `EmptyData`, `FailedFields`, `ExcludeFolders`, month names and rules. What is missing is the reader and the handling of the settings we do not model.

Success: the ten supplied profiles import; a profile that would organize the wrong books never imports.

## Non-goals

- Exporting `.lop`.
- Pre-2.0 `Settings/Setting` files (the plugin itself converts them in `update()`); reported as unsupported.
- A per-profile illegal-character table. Our `Sanitizer` default equals the plugin's and every supplied profile uses it. A profile whose table differs is reported, not applied.
- `Prefix` / `Postfix` / `Seperator` / `TextBox` (the plugin's per-token decoration), `MoveFileless`/`FilelessFormat`, `MoveFailed`/`FailedFolder`: reported when set; none of the supplied profiles set them.

## Source facts (from `losettings.py`, `locommon.py` 2.1.x and the ten files)

- A file's root is `<Profile Name="…" Version="2.1">` (one profile) or `<Profiles LastUsed="…">` holding several. Every setting is a child element: text, `true`/`false`, `<Item>` lists, or `<Item Name="…" Value="…"/>` dictionaries (`Months` keys are integers).
- `Mode` is `Move | Copy | Simulate`, the same as our `OrganizerMode`. `CopyMode` (add copies to the library) is true in the file unless `Mode` is Copy and the user turned it off; our Copy never adds to the library (documented deviation).
- Exclude rules: `<ExcludeRules Operator="Any|All" ExcludeMode="Do not|Only">` holding `ExcludeRule(Field, Operator, Value)` and nested `ExcludeGroup(Operator)`. `check_metadata_rules` counts rule results, ignoring groups that report none; no rules at all means "move everything" in either mode.
  - `Do not`: a book that qualifies is **skipped**. This is our model.
  - `Only`: a book that qualifies is **moved**, all others skipped. Five of the ten supplied profiles use it.
- Rule operators: `is`, `is not`, `contains`, `does not contain`, `greater than`, `less than`. Comparisons are case-sensitive; greater/less try an integer compare and otherwise compare strings. Fields are display names mapped by `name_to_field`; `Manga`, `SeriesComplete`, `BlackAndWhite` compare Yes/No values; `StartYear`/`StartMonth` read the earliest book of the series in the library.
- The supplied profiles: all use the default illegal-character table, no prefix/postfix/separator/text-box, no failed-folder or fileless settings; `Events.lop` is `Copy` with `CopyMode=false`; the others are `Move`; templates use `Custom(...)` and `tags( )(issue)` tokens and `volume0`/`number3`/`altNumber3`.
  `Events.lop` appears in both archives, identical.

## Design

### Components

- **`LopProfileReader`** (pure, `Paperbunkr.Data/Organizing/Import/`): `Stream` → `LopReadResult { Profiles: [ImportedProfile { OrganizerProfile, Notes }], Rejected: [{ Name, Reason }], FileError? }`. No database, no UI.
- **`ExcludeRuleConverter`**: the plugin's rule tree → a `PluginConditionGroup` serialized into `ExcludeRuleJson` (the same serializer the profile editor uses; the plan finds it), including the inversion below.
- **`LopProfileImporter`** (database): saves accepted profiles through `OrganizerProfileStore.Save`.
- UI: an "Import profiles…" action in the organizer's profile list (multi-select files) and `.lop` accepted by the drag-and-drop importer (a new bucket beside `.cbl`/`.csv`). After an import, the first imported profile opens in the profile editor with its notes shown; a summary lists the notes for the rest.

### Field mapping

| `.lop` | `OrganizerProfile` | Notes |
|---|---|---|
| `Name` | `Name` | a name already in use gets " (2)", " (3)" |
| `FolderTemplate`, `FileTemplate` | same | copied verbatim; our template engine is plugin-parity (`TemplatePluginParityTests`). Both are run through the existing save-time template validation; a failure is a note, and the profile is still imported so the user can fix it |
| `BaseFolder` | `BaseFolder` | kept as written (see Safety) |
| `Mode` | `Mode` | direct |
| `UseFolder`, `UseFileName`, `RemoveEmptyFolder`, `EmptyFolder`, `EmptyData`, `FailEmptyValues`, `FailedFields`, `ExcludeFolders` | same | direct |
| `Months` | `MonthNames` | stored only when it differs from the default table |
| `ExcludeRules` | `ExcludeRuleJson` | see below |
| `IllegalCharacters` | — | note when different from the default table |
| `Prefix`, `Postfix`, `Seperator`, `TextBox`, `MoveFileless`, `MoveFailed`, `ExcludedEmptyFolder` | — | note when set (non-empty / true) |
| `AutoSpaceFields`, `ReplaceMultipleSpaces` | — | note when false (we always apply them; the plan confirms by reading the evaluator) |
| `CopyMode` | — | note when true and `Mode` is Copy (our Copy never adds to the library) |
| `DontAskWhenMultiOne`, `CopyReadPercentage`, `Version` | — | UI-only or bookkeeping; ignored without a note |

### Exclude rules

Our model: the rules select the books to **skip**. Conversion:

1. Rule operator → `SmartListOperator`/`Not`: `is` → Is, `is not` → Is + Not, `contains` → Contains, `does not contain` → Contains + Not, `greater than`/`less than` → GreaterThan/LessThan. Case-sensitive in the plugin, so `IgnoreCase = false`.
2. Field name → `SmartListField` through a table built from `name_to_field` (the plan reads the 100-odd entries; this spec does not guess them). Yes/No fields become Toggle conditions (`Yes` → true, `No` → false).
3. `ExcludeRules` `Operator` → the root group's `Mode` (`Any` → Or, `All` → And); each `ExcludeGroup` becomes a child group.
4. Groups with no rules are dropped (the plugin ignores them). No rules at all → `ExcludeRuleJson` is null.
5. `ExcludeMode = Only` means "keep only books that qualify", so the books to skip are those that do **not** qualify: wrap the whole tree in NOT. `PluginConditionGroup` has no group-level Not, so apply De Morgan recursively (flip each group's mode, flip each condition's `Not`). `Do not` needs no inversion.
6. **A rule that cannot be translated rejects the whole profile**: an unknown field name, `StartYear`/`StartMonth` (no series-start equivalent), `Yes/No` value `Unknown`, or greater/less on a text field. Dropping a rule would change which files move, and a moving organizer is the one place a silent approximation costs real files. The rejection names the rule.

### Safety

- `UseForScheduledRun` is always false on import.
- `BaseFolder` is kept, but a `Move` profile imports with a note "check the base folder before running" and the editor opens with that field highlighted; nothing runs from the import. The supplied profiles point at `D:\Books\ComicBooks\`.
- The reader never touches the filesystem.

## Error handling

An unreadable or non-XML file is a `FileError` and skipped. A profile node that throws while converting is rejected with its reason and the others in the file still import. Each file's result is independent, so one bad file in a multi-select does not stop the rest, and the summary lists every rejection.

## Testing

- Reader: each supplied file as a fixture (the identical `Events.lop` once), asserting templates, mode, base folder, and note lists.
- Exclude conversion against a **reference port of `check_metadata_rules`** written in the test: over a generated set of books and generated rule trees (any/all, nested, both `ExcludeMode`s), the books our converted rule excludes must equal the books the reference port would skip. This is the check that protects the Only inversion.
- Untranslatable rules reject the profile; a profile with no rules imports with null rule JSON in either mode.
- Names: collision suffixes; a `Profiles` file with several profiles; `Events.lop` imported twice.
- Safety: `UseForScheduledRun` false; the importer performs no file I/O.
- UI: view-model test for the import action and the notes summary. Not rendered or viewed on screen.

## Not verified when this spec was written

The rule serializer used by the profile editor, the `name_to_field` table, whether the evaluator applies `AutoSpaceFields`/`ReplaceMultipleSpaces` the plugin's way, and where the profile list lives in the UI are unread; the plan's first task reads them. Nothing has been seen on screen.
