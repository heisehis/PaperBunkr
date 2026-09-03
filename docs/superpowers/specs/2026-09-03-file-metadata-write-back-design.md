# File Metadata Write-Back — Design

*Last open item in the "Library browsing extras" backlog (`docs/alpha-roadmap.md`), deliberately
sequenced last: it mutates the user's original comic files in place, so it's a real risk surface
that CE itself gates behind explicit opt-in settings.*

## CE-parity check (standing rule)

Checked `_reference/ComicRackCE` first.

- **The write itself** — `ComicBook.WriteInfoToFile(IComicUpdateSettings settings)`
  (`ComicRack.Engine/ComicBook.cs:2400`): bails if the file is missing or read-only, opens the
  file's own `ImageProvider`, casts to `IInfoStorage`, calls `infoStorage.StoreInfo(info)` where
  `info` is `GetInfo()` (a plain `ComicInfo`) normally, or `this` (the full `ComicBook`, which also
  serializes a `ComicBook.xml`) when `settings.UpdateComicBookFiles` is on. For an archive this
  routes `ArchiveComicProvider.OnStoreInfo` → `IComicAccessor.WriteInfo` →
  `SevenZipEngine.UpdateComicInfos`, which runs `7z u -t<zip|7z|tar> <file> <tmp>/ComicInfo.xml` —
  a single-entry archive update, pages never touched. 7-Zip writes to its own temp and swaps, so a
  crash mid-write leaves the original intact. RAR/PDF/DjVu providers return `false` from
  `WriteInfo`/`OnStoreInfo` (no free writer). **Paperbunkr already has this whole path ported and
  working** (`src/Paperbunkr.Engine`), just with zero callers — `7z` is already bundled (reader
  spec, `project_paperbunkr_reader_canvas`).
- **The trigger** — `QueueManager.AddBookToFileUpdate(cb, alwaysWrite)`
  (`ComicRack.Engine/QueueManager.cs:538`): called after essentially any metadata edit. Guarded by
  `cb.IsLinked && Settings.UpdateComicFiles && (Settings.AutoUpdateComicsFiles || alwaysWrite)` and
  "is anything actually dirty". Debounced with a 100 ms per-book timer into a serial
  `WriteComicBookInfoFileQueue`, so a bulk edit produces one write per file, not one per field.
- **The explicit action** — `MainForm.UpdateComics()` (whole library) and
  `ComicBrowserControl.UpdateFiles()` (selected, non-fileless) both call `AddBookToFileUpdate(cb,
  alwaysWrite: true)` — bypasses the `AutoUpdateComicsFiles` gate but still requires the
  `UpdateComicFiles` master.
- **The settings** — three checkboxes in `PreferencesDialog`, all defaulting to `false`
  (`Settings.cs:202-206`), under a "Comics" group:
  1. *"Allow writing of Book info into files"* (`UpdateComicFiles`) — master.
  2. *"Book files are updated automatically"* (`AutoUpdateComicsFiles`) — auto vs. manual-only.
  3. *"Allow writing of Library info into files"* (`UpdateComicBookFiles`) — also embed a
     `ComicBook.xml` with CE-proprietary catalog fields (rating, custom fields, per-page data, read
     state). The lower two disable in the UI when the master is off
     (`PreferencesDialog.cs:197-199`).

This ports as **behavior, not code** for the App layer (CE is WinForms + a global `Program.Settings`
singleton + a `ComicBook` that *is* the in-memory metadata; Paperbunkr's DB row is the source of
truth and the engine's `ComicBook`/`ComicInfo` types are used only transiently). The engine-layer
write path is reused as-is.

**Deliberate deviation on setting #3:** Paperbunkr does **not** write CE's `ComicBook.xml`. Its DB
is the source of truth for rating/read-state/custom fields and `ComicBook.xml` is a CE-proprietary
shape. Instead, setting #3 becomes *"also write a Paperbunkr sidecar"* — a small versioned
`paperbunkr.json` archive entry carrying only the fields with no `ComicInfo.xml` home (confirmed
with the user). Everything else mirrors CE.

## Scope

- **Write the full `ComicInfo.xml` field set** every metadata editor can touch — not just Genre/Tags
  (today's `ComicInfoWriteBackService` scope). Series/Number/Count/Volume/Title/Year/Month/Day,
  Summary/Notes/Review, all credits (Writer/Penciller/Inker/Colorist/Letterer/CoverArtist/Editor/
  Translator), Characters/Teams/Locations/MainCharacterOrTeam, Genre + Tags (flat CSV),
  Publisher/Imprint, AgeRating, LanguageISO, Format, Web, ScanInformation, Manga, BlackAndWhite,
  CommunityRating + personal rating, `<Pages>` (type + bookmark subset).
- **Optional `paperbunkr.json` sidecar** (setting #3) for fields with no ComicInfo standard home:
  tag categories + weights (ComicInfo gets the flat CSV; the sidecar keeps the structure),
  Quick-Rate / personal rating, Review, BookAge, per-page rotation overrides (`IssuePage`),
  IsFinalIssue / series-complete, proposed-field values. **Not** read state / opened / added dates
  (DB-owned). Written as an archive entry, same `7z u` update as `ComicInfo.xml` — one archive
  update writes both.
- **Formats:** CBZ / CB7 / CBT / folder-of-images are writable; CBR/RAR, PDF, DjVu, EPUB, and
  fileless entries are a visible skip, never a silent failure.
- **Two modes:** automatic (debounced background write after any qualifying save) and a manual
  *"Write metadata to files"* action (selection + whole-library). Manual works whenever the master
  is on, regardless of the automatic toggle.
- **Explicitly out of scope:** CE's `ComicBook.xml`; writing on read-state / reading-position
  changes; any write to a format without a free writer; migrating existing files on upgrade (only
  writes on an actual edit or an explicit action).

## Settings

Three new `AppSettings` columns, all default `false` (CE parity — nothing touches a user's files
until they opt in):

| Field | Meaning |
|---|---|
| `WriteMetadataToFiles` | Master. Off ⇒ no file is ever written, the rest is inert. |
| `WriteMetadataAutomatically` | On ⇒ every qualifying save enqueues a background write. Off ⇒ only the manual action writes. |
| `WriteNativeSidecar` | On ⇒ also write `paperbunkr.json` into the archive. Off ⇒ standard `ComicInfo.xml` only. |

One EF migration adding the three columns.

**Preferences → Advanced**, new group **"Comic File Metadata"** placed above "Backup Manager"
(`AdvancedSection.axaml`). Three checkboxes; the lower two bind `IsEnabled` to the master (CE's
exact dependency). Body copy states plainly that this **modifies the original comic files in
place** and lists the non-writable formats. A **"Write all library metadata to files now…"** button
lives in the same group (Section: Manual action). `PreferencesScreenViewModel` gets the three
`[ObservableProperty]` fields + persist-on-change, identical to every other setting there. Load the
`avalonia` router → `design-system` / `components` subskills before writing this XAML (hardcoded-hex
and shared-style check — see CLAUDE.md).

## Components

### `IssueToComicInfoMapper` (`Paperbunkr.Data/CeMigration`)

Lives next to `CeLibraryMigrator` as its literal inverse (`Paperbunkr.Data` already references
`Paperbunkr.Engine` and owns the `Effective*` extensions in `Metadata/IssueMetadataExtensions.cs`).
`Apply(Issue issue, ComicInfo target)` overlays every Paperbunkr-modeled ComicInfo field onto
`target`, using **effective** values (`issue.EffectiveNumber()` etc. — accepted `MetadataProposal`s
included). Whole-field overwrite from DB truth, not a diff. Fields Paperbunkr doesn't model are left
as loaded. One place, unit-tested field-by-field, so "which fields round-trip" is a single
reviewable list. `Issue.Tags` → the flat `Genre`/`Tags` CSV via the existing `JoinedGenre()` /
`JoinedTags()` helpers.

### `MetadataFileWriteBackService` (`Paperbunkr.App/Services`) — replaces `ComicInfoWriteBackService`

`Task<MetadataWriteBackOutcome> WriteAsync(int issueId, bool includeSidecar)`. Loads the issue fresh
from its own `PaperbunkrDbContext` (callers pass only an id):

1. Resolve `FilePath`; classify format. Fileless / missing / read-only / unsupported → the matching
   skip outcome, no file touched.
2. `ComicBook.Create(path, RefreshInfoOptions.None)` — loads the file's **current** embedded
   `ComicInfo.xml` so unmodeled elements survive.
3. `IssueToComicInfoMapper.Apply(issue, book)`.
4. If `includeSidecar`: build `paperbunkr.json` bytes — `{ "schema": 1, ... }` over the sidecar
   field list. New `PaperbunkrSidecar` record + `System.Text.Json` (de)serializer, versioned.
5. One archive update: new `SevenZipEngine.UpdateEntries(string file, int format,
   IReadOnlyDictionary<string, byte[]> entriesByName)` generalizing the existing `UpdateAll` — write
   each `byte[]` to a temp file named by key, one `7z u`. Folder-of-images: `File.WriteAllBytes`
   the entries directly into the folder.
6. Return `Success` / `SkippedUnsupportedFormat` / `SkippedMissingFile` / `SkippedReadOnly` /
   `Failed(message)`. **Never throws** — catches internally, all failures are outcomes (the
   `WriteErrorException` from `ExecuteUpdateProcess` becomes `Failed`).

`ComicInfoWriteBackService` + `WriteGenreTags` + `IssuePropertiesScreenViewModel.TriggerComicInfoWriteBack`
are deleted; the `ComicExporter`/`PackedStorageProvider` pipeline stays (still used by real
"Export/Convert comics") — it just loses this one caller.

### `MetadataWriteBackQueue` (`Paperbunkr.App/Services`)

Owned by `MainViewModel` like `LiveFolderWatchService`, constructed with `ShowToast` +
`PaperbunkrDb.CreateContext`.

- `Enqueue(int issueId, bool manual = false)` — coalesces by issue id (re-enqueue resets the
  debounce), ~300 ms debounce, then a background worker processes **one file at a time** (7-Zip is
  a per-call process; concurrent archive writes invite corruption).
- Reads `AppSettings` itself at flush time: `manual` items require only `WriteMetadataToFiles`;
  automatic items also require `WriteMetadataAutomatically`. `includeSidecar` = `WriteNativeSidecar`.
- Outcomes aggregate into a short batch window and flush **one** toast:
  `"Wrote 12 files · 2 skipped (.cbr) · 1 failed"`. A single-issue automatic write that skips for a
  non-writable format still surfaces the CE-style *"saved to library only — Name.cbr can't be
  updated"* toast (generalizes the tags editor's current per-non-CBZ notice); `Failed` always
  notifies.
- Fire-and-forget from every trigger — the DB `SaveChanges()` is already committed and
  authoritative, the file write is best-effort.

### `MetadataFileFieldSnapshot` (`Paperbunkr.Data/CeMigration`, beside the mapper)

Generalizes the tags editor's existing `genreBefore/genreAfter` compare
(`IssuePropertiesScreenViewModel.cs:564-577`). `Capture(Issue)` → a value snapshot of every
file-mapped field (ComicInfo set + sidecar set); `Differ(before, after)` → bool. Trigger sites call
`Enqueue` only when this returns true (so a Category/Weight-only reweight enqueues only when
`WriteNativeSidecar` is on and only the sidecar changed; a pure star-rating change enqueues nothing
unless the sidecar is on). A `manual` run skips the check entirely.

## Trigger wiring

Each site calls `queue.Enqueue(id)` **after** its `SaveChanges()`, guarded by
`MetadataFileFieldSnapshot.Differ`:

1. `IssuePropertiesScreenViewModel.Save` — one id.
2. `BulkIssuePropertiesScreenViewModel` apply — every edited id.
3. Detail-screen inline edits — Quick-Rate, star rating, tag reweight/recategorize — one id.
4. Bulk Series / series-metadata edits — every member issue id.
5. "Apply from provider" (AniList / MangaBaka / MangaDex) — the target id(s).
6. Metadata-proposal acceptance in Needs Review — the affected id(s).

`MainViewModel` passes the queue (or a thin `Action<int>` enqueue delegate, matching how `ShowToast`
is threaded) into each ViewModel that needs it. ViewModels already lacking a toast/callback seam get
one the same way `ReadingScreenViewModel` just got `showToast` for drag-and-drop import.

## Manual "Write metadata to files" action

Available whenever `WriteMetadataToFiles` is on (CE's `alwaysWrite: true`):

- **Library grid context menu** → *"Write metadata to files"* on the current selection (a series
  selection expands to its member issues). Routed through the shared `MenuFlyout` mechanism
  (`project_paperbunkr_context_menu_rebuild` — `ContextMenu` popups don't render in this Avalonia
  build). Enqueues all ids with `manual: true`, progress toast, then the summary toast. CE's
  `ComicBrowserControl.UpdateFiles()`.
- **Preferences → Advanced**, "Comic File Metadata" group → *"Write all library metadata to files
  now…"* → confirm dialog stating the file count → progress toast → summary. CE's
  `MainForm.UpdateComics()`.
- Both route through `MetadataWriteBackQueue` with `manual: true`.

## Edge cases

- **Concurrent automatic + manual run, or two edits to the same file** — the queue's per-id
  coalescing + single-worker serialization means one write per file at a time; no new lock needed
  beyond the queue itself.
- **File renamed / moved between the edit and the flush** — `WriteAsync` re-resolves `FilePath`
  from the DB at flush time and re-checks existence; a since-vanished file is a `SkippedMissingFile`.
- **`7z u` partial failure (exit 1)** — `ExecuteUpdateProcess` already distinguishes a real
  `WriteErrorException` from a benign exit 1; the service maps the former to `Failed`, the latter to
  `Success` (matches the engine's existing contract).
- **Live folder-watch reacting to our own write** — our `7z u` bumps the archive's mtime, which
  `LiveFolderWatchService` sees as a change. It only acts on `Created`/`Deleted`/`Renamed`, not
  `Changed`, so an in-place update is ignored — no rescan loop. (Confirmed against
  `LiveFolderWatchService.cs` `NotifyFilters`.)
- **Sidecar on a file that has one from a prior write** — `7z u` replaces the entry; the versioned
  `schema` field lets a future reader migrate an older sidecar.
- **Master toggled off while items are queued** — the queue re-checks settings at flush time, so
  queued items become no-ops.

## Testing

- **`MetadataFileWriteBackServiceTests`** (`CbzFixture`, real synthetic archives): full-field
  round-trip (write → re-read embedded via `EmbeddedComicInfoReader` → assert), an unmodeled
  ComicInfo element preserved across a write, sidecar written + parsed back, CBR → `SkippedUnsupportedFormat`,
  missing file → `SkippedMissingFile`, read-only → `SkippedReadOnly`, folder-of-images comic, a
  forced `7z` failure → `Failed` (not a throw).
- **`IssueToComicInfoMapperTests`**: every mapped field; effective-value (accepted proposal)
  resolution; null/empty handling; whole-field-overwrite semantics.
- **`MetadataWriteBackQueueTests`**: debounce coalescing, one-at-a-time ordering, aggregated
  summary text, settings re-checked at flush (master off ⇒ no-op; automatic off + non-manual ⇒
  no-op; automatic off + manual ⇒ writes), the field-change guard suppressing a no-op enqueue.
- **`SevenZipEngineTests`** (or extend existing): `UpdateEntries` adds/replaces multiple entries in
  one call for zip/7z/tar.
- **ViewModel wiring**: each trigger site enqueues the right ids against a fake queue; master-off
  and automatic-off paths; manual action bypasses the field-change guard.
- **Real OS-level nothing** — every path here is headless and archive-level; no GUI automation gap
  this time. The Preferences checkboxes are the only UI and are covered by
  `PreferencesScreenViewModelTests` (persist-on-change) + a FlaUI toggle test written but unrunnable
  in this environment (standing UIA barrier).
