# Preferences Screen — Libraries Tab (Virtual Tags + Book Folders)

*Date: 2026-08-07. Third tab on the shell established by
docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md §5. Scoped after reading CE's
actual `PreferencesDialog`/`Settings.cs` Libraries-tab source directly, same method as the
Behavior-tab spec. Sharing/Server settings (`LookForShared`, `ComicLibraryServerConfig`, etc.) are
explicitly excluded — docs/ce-feature-inventory.md §F already tracks network library sharing as
its own large initiative needing its own design spec, not Preferences-tab scope.*

## 1. Virtual Tags

CE's Virtual Tags (`ComicRack.Engine/Metadata/VirtualTags/`, already ported verbatim but unwired
in `Paperbunkr.Engine`) are user-defined metadata fields computed from a caption-format template
string (e.g. `{Series} Vol.{Volume}`) evaluated per-issue, editable via a list + insert-field-picker
in Preferences.

**Not reusing `ComicBook.GetFullTitle`/`ExtendedStringFormater`**: CE's template evaluator is
built on `ComicBook`'s reflection + "shadow value" property system (`GetPropertyValue<string>(...,
ComicValueType.Shadow)`), a mechanism designed for CE's whole in-memory book model, not a good fit
for a small feature operating on Paperbunkr's `Issue`/`Series` EF entities. Same rationale as
Smart Lists' "in-memory query architecture" rebuild - port the *format* where it matters (CBL/CSV
serializers), rebuild *compute* logic natively against the current model. This spec builds a
small, purpose-built template evaluator instead.

### 1.1 Data model

New `VirtualTagDefinition` entity (`Paperbunkr.Data.Entities`):

- `Id`, `Name` (string), `CaptionFormat` (string, e.g. `"{Series} #{Number}"`), `IsEnabled` (bool),
  `SortOrder` (int) - no `IsDefault`/`ID`-as-fixed-slot-number from CE's design (that was a
  WinForms `VirtualTag01..20` fixed-property-count workaround; a plain EF table has no such limit).

### 1.2 Template mini-language

`VirtualTagTemplateEvaluator.Evaluate(string template, Issue issue, Series series)`:
substitutes `{FieldName}` tokens (case-insensitive) against a fixed, small field set pulled from
`Issue`/`Series` - `{Series}`, `{Number}`, `{Volume}`, `{Year}`, `{Title}`, `{Publisher}`,
`{Writer}`, `{Penciller}` - unrecognized tokens pass through unchanged (visible-and-obvious rather
than silently dropped, easier to debug in the editor's live preview). No conditional/nested syntax
- CE's own format strings are flat token substitution too, this isn't a regression.

### 1.3 Preferences UI

New "Virtual Tags" group in the Libraries tab: list of defined tags (name + enabled state,
click to select), an editor panel (Name, Caption Format text box, Enabled checkbox, an "Insert
field" dropdown of the supported tokens, live preview computed against the first issue found in
the library - falls back to a static example row if the library is empty), Add/Delete buttons.
Changes apply immediately (same click-to-apply convention as Appearance/Behavior), no separate
Save step.

**Deliberately out of scope for this spec**: wiring computed Virtual Tag values into Smart Lists'
query engine as a filterable field, or displaying them anywhere in Library/Detail screens yet -
the evaluator + editor is real, working infrastructure; consuming the computed value in the rest
of the UI is its own follow-up once there's a concrete display slot to put it in (matches the
skin system spec's icon-manifest precedent: real capability, no forced consumer yet).

## 2. Book Folders (scan + import)

CE's "Book Folders" panel (`PreferencesDialog` `groupComicFolders`) manages a folder list, each
with an optional live-watch flag, plus an on-demand "Scan" button. Paperbunkr currently has *no*
way to add comics to the library other than CE-library migration - this is genuinely new ingestion
capability, not a settings toggle.

**Reused as-is** (already ported, unwired, in `Paperbunkr.Engine`): `ComicNameInfo.FromFilePath`
for filename-based Series/Number/Volume/Year/Format parsing - the same parser CE itself uses.

**v1 scope decision: filename parsing only, no embedded `ComicInfo.xml` reading.** CE's scanner
also reads embedded `ComicInfo.xml` for richer metadata, but that requires archive-format-specific
entry extraction (`ComicInfoProvider` is ported but needs a per-archive-type entry reader wired to
it, which doesn't exist yet outside `PageImageDecoder`'s page-only access path) - real scope, not
free. Shipping filename-only first proves the folder→scan→library pipeline end to end; richer
metadata is a clean fast-follow once this lands, same "ship the mechanism, enrich later" precedent
as the skin system's icon manifest and `windows_11` reference skin.

**v1 scope decision: on-demand scan only, no live `FileSystemWatcher` auto-import.** CE's
per-folder "Watch" checkbox wires a `FileSystemWatcher` for real-time auto-import - a genuinely
separate concern (background file-event handling, debouncing, thread-safety against the UI) from
the scan-and-import logic itself. Ships as its own follow-up once on-demand scanning is proven.

### 2.1 Data model

New `WatchedFolder` entity (`Paperbunkr.Data.Entities`): `Id`, `Path` (string, unique). No `Watch`
bool in v1 (see above) - every listed folder is scanned when "Scan" is pressed, nothing more.

### 2.2 Scan pipeline

New `LibraryFolderScanner` (`Paperbunkr.App.Services`):

- Walks each `WatchedFolder.Path` recursively for files with a supported comic extension (reuse
  the same extension set `PageImageDecoder`/`EngineConfiguration` already recognize - cbz/cbr/zip/rar/7z, no new format support).
- Skips any file path already present as an `Issue.FilePath` in the database (idempotent re-scan,
  same "presence-based, re-running only fills gaps" principle as `CoverThumbnailService.GenerateAllAsync`).
- For each new file: `ComicNameInfo.FromFilePath(path)` → find-or-create the `Series` by
  case-insensitive name match (reusing the exact matching convention `CeLibraryMigrator` already
  established for series identity), then add a new `Issue` with `Number`/`Volume`/`Year` from the
  parsed name and `FilePath` set.
- Reports progress via the same `IProgress<(int Done, int Total)>` shape `CoverThumbnailService`
  already uses, for UI consistency.

### 2.3 Preferences UI

New "Book Folders" group in the Libraries tab: list of folders (Browse-to-add via
`IFilePickerService`-equivalent folder picker, Remove, Open-in-Explorer), a "Scan Now" button
showing progress and a result summary (`"Added 12 issues across 3 series"`), disabled while a scan
is already running.

## Testing

- `VirtualTagTemplateEvaluatorTests`: token substitution for every supported field, unrecognized
  tokens pass through unchanged, missing/null `Issue`/`Series` values render as empty string not
  `"null"`.
- `VirtualTagDefinitionTests` (`Paperbunkr.Data.Tests`): CRUD + `SortOrder` persistence.
- `LibraryFolderScannerTests`: scanning a temp folder with real files (via `CbzFixture`-named
  fixtures, same "generate via the real code path" precedent) creates the right Series/Issues;
  re-running is idempotent (no duplicates); an existing series (case-insensitive name match) gets
  a new Issue added to it rather than a duplicate Series; unsupported file extensions are ignored.
- `PreferencesScreenViewModelTests`: Virtual Tag add/edit/delete persists; folder add/remove
  persists; Scan Now triggers the scanner and refreshes the folder list's implicit state.
- Manual verification: same no-GUI-automation approach as prior specs - build + run real tests,
  then ask the user to point "Scan Now" at a real folder of comics and confirm the library
  actually gains the right series/issues.
