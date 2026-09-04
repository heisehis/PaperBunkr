# File Metadata Write-Back — Implementation Plan

*Implements: docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md*

## Deviations discovered during implementation (2026-09-03)

- **No `7z u` write path — `.cbz` only via `ZipArchive`.** The ported `SevenZipEngine` `7z u` code
  needs a bundled `7z.exe` Paperbunkr doesn't ship (only `7z.dll`, for reading). Step 4
  (`SevenZipEngine.UpdateEntries`) was dropped; `MetadataFileWriteBackService` uses
  `System.IO.Compression.ZipArchive` update mode for `.cbz` (add/replace `ComicInfo.xml` +
  `paperbunkr.json`, no page re-encode, temp-copy + `File.Replace` for atomicity) and writes plain
  files for a folder comic. **`.cb7`/`.cbt` → `SkippedUnsupportedFormat`** (deferred with CBR/PDF).
- **Sixth trigger (Needs Review proposal-accept) deferred.** Wired: Issue Properties, Bulk Issue
  Properties, Detail (content-type + tag reweight), Manga Detail (content-type), Bulk Series
  (Content Type / Reading Mode), Quick Rate. `NeedsReviewViewModel.ResolveProposal` was not wired —
  accepted proposal values already flow to the file on the next edit of that issue, and the manual
  action covers it; not worth threading a callback through `MigrationOverlayViewModel`.
- **Migration `Down()` is a no-op.** A `DropColumn` on `AppSettings` triggers EF's SQLite table
  rebuild, which uses the previous migration's snapshot — and PR #40's `UnifyLibrarySortGroupFields`
  left `LibrarySortField`/`LibraryGroupField` as unmapped orphans, so the rebuild silently drops
  them and a later `Down()` step's explicit `DropColumn LibraryGroupField` then fails. Leaving the
  three columns as orphans on down-migrate matches PR #40's own precedent and keeps the migration
  round-trip tests green.
- Per-page type/rotation → sidecar is deferred (noted in `PaperbunkrSidecar`); the service carries
  `Issue.PageCount` through so a freshly-written `ComicInfo.xml` doesn't read back as 0 pages.

Surface area verified against `master` @ `a25a407` (post PR #40/#41). Key facts the plan relies on:

- `ComicInfoWriteBackService.WriteGenreTags` (App/Services) is called from exactly two places:
  `IssuePropertiesScreenViewModel.TriggerComicInfoWriteBack` (static, line ~632) and
  `BulkIssuePropertiesScreenViewModel` (line ~286). Tests: `ComicInfoWriteBackServiceTests`,
  `IssuePropertiesWriteBackTests`, `BulkIssuePropertiesWriteBackTests`.
- `EmbeddedComicInfoReader.TryRead(path) → ComicInfo?` (Data/Metadata) already reads a file's real
  embedded `ComicInfo.xml` via the engine's `IInfoStorage.LoadInfo`. `ComicInfo.ToArray()`
  serializes it (used by `CbzFixture`). This is the "load current, preserve unmodeled fields" path —
  no need for `ComicBook.Create`/`WriteInfoToFile`.
- `SevenZipEngine` (Engine) has `UpdateAll(file, IEnumerable<ComicInfo>, UpdateSettings)` private +
  `UpdateComicInfos` public — both write named temp files into the archive via one `7z u -t<fmt>`.
  `GetParameters`/`ExecuteUpdateProcess`/`WriteErrorException` handling already there. `7z` binary
  already bundled (reader).
- `CeLibraryMigrator.MapStoryFields(ComicInfo → Issue)` (Data/CeMigration, line 490) is the forward
  mapper to invert. `IssueMetadataExtensions.Effective*` (Data/Metadata) + `IssueTagExtensions.
  JoinedGenre()/JoinedTags()` (Data/Entities) give effective/flattened values. `ComicInfo` has
  **no** personal `Rating` field — only `CommunityRating` (personal `Issue.Rating` → sidecar only).
- `AppSettings` (Data/Entities): plain `public bool Foo { get; set; }` for a default-false bool, no
  `HasSentinel`/`HasDefaultValue` needed (those are enum-as-string only).
- `PreferencesScreenViewModel`: `[ObservableProperty] private bool _x;` +
  `partial void OnXChanged(bool v) => PersistBehaviorSetting(s => s.X = v);` + load in `Load()`
  (~line 520-562). Already holds `_showToast` and `_libraryScanner`.
- `LibraryContextMenuBuilder` (App/ViewModels): `ContextMenuEntry.Item("label", command, param,
  Symbol.X, isEnabled:)`. `_vm.Selection` / `_vm.SeriesSelection` (`UnionForAction`). Rendered via
  the shared `MenuFlyout` mechanism (not `ContextMenu`).
- `MainViewModel` constructs each screen VM (lines 84-120) and owns
  `LiveFolderWatch = new LiveFolderWatchService(ShowToast, …)` (line 120).
- **Migration gotcha:** `dotnet ef` from this tree migrates the shared per-user dev DB
  (`%APPDATA%\Paperbunkr\paperbunkr.db`). Add the migration with a throwaway `--connection` to a
  temp file, or roll the dev DB back after — see `feedback_worktree_shares_user_db`.

---

## Step 1: AppSettings columns + migration

**Files:** `src/Paperbunkr.Data/Entities/AppSettings.cs` (edit),
`src/Paperbunkr.Data/Migrations/<ts>_AddMetadataWriteBackSettings.cs` (+ `.Designer.cs`, snapshot) (new)
**What:** Add three `bool` properties, all default `false`, with XML-doc citing the design + CE
parity (CE's `UpdateComicFiles` / `AutoUpdateComicsFiles` / `UpdateComicBookFiles`, all default
false):
`WriteMetadataToFiles`, `WriteMetadataAutomatically`, `WriteNativeSidecar`.
Generate the migration (`AddColumn<bool>(nullable: false, defaultValue: false)` ×3, empty-ish
`Down`). No data backfill.
**Depends on:** none
**Verify:** `dotnet build` of `Paperbunkr.Data`; snapshot diff is exactly the three new
`b.Property<bool>` lines. No dedicated migration test — the repo only writes those for migrations
with custom SQL (`RemoveComicListViewMode` etc.); a plain additive `AddColumn` is covered by the
existing `EnsureCreated`/`Migrate` paths in `Paperbunkr.Data.Tests` and the Step 9 Preferences
persist test.

## Step 2: IssueToComicInfoMapper

**Files:** `src/Paperbunkr.Data/CeMigration/IssueToComicInfoMapper.cs` (new),
`src/Paperbunkr.Data.Tests/IssueToComicInfoMapperTests.cs` (new)
**What:** `public static void Apply(Issue issue, ComicInfo target)` — the inverse of
`MapStoryFields`. Overwrites every Paperbunkr-modeled ComicInfo field on `target` from the DB using
*effective* values:
- Strings via `Effective*` where one exists (`EffectiveTitle/Number/Volume/Format`, `EffectiveYear/
  Count`), raw `issue.X` otherwise. Empty/null → `string.Empty` (ComicInfo's own "unset").
- `target.Series = issue.Series?.Name ?? target.Series` (requires the `Series` nav be loaded — the
  service includes it).
- `Volume` parse: `int.TryParse(issue.EffectiveVolume(), out var v) ? v : 0`.
- `Genre = issue.JoinedGenre() ?? ""`, `Tags = issue.JoinedTags() ?? ""`.
- `CommunityRating = issue.CommunityRating ?? 0f` (personal `Issue.Rating` is **not** written here).
- `BlackAndWhite`: `issue.ColorMode == ColorMode.BlackAndWhite ? YesNo.Yes : YesNo.No` (leave
  `Unknown`→`No`? — match `CeLibraryMigrator`'s own ColorMode handling; check its migrate path and
  mirror it).
- `Manga`: invert `CeLibraryMigrator.MapMangaField` from `issue.Series?.ContentType` +
  `ReadingMode` (Manga+RTL → `YesNo`/`RightToLeft` per CE's `MangaYesNo`). If the migrator has no
  reusable inverse, add a small `MapMangaFieldReverse` next to it.
- `MainCharacterOrTeam`, `ScanInformation`, `AlternateSeries/Number`, `StoryArc`, `SeriesGroup`,
  `Web`, `LanguageISO`, `AgeRating`, `Imprint`, all credits, `Characters/Teams/Locations`,
  `Summary/Notes/Review`, `Year/Month/Day`, `Count`.
- **Not written:** `<Pages>` in this step (see Step 5 decision), `PageCount` (derived),
  `PreferredFrontCover`/`AlternateCount` (unmodeled — preserved by the service loading current
  first).
**Depends on:** none (uses existing entities/extensions)
**Verify:** `IssueToComicInfoMapperTests` — one assertion per field group; effective-value
resolution (raw null + accepted `MetadataProposal` ⇒ proposal value written); a field left null on
the Issue ⇒ `""` on the target; `Series` nav null ⇒ target's existing value untouched.

## Step 3: MetadataFileFieldSnapshot

**Files:** `src/Paperbunkr.Data/CeMigration/MetadataFileFieldSnapshot.cs` (new),
`src/Paperbunkr.Data.Tests/MetadataFileFieldSnapshotTests.cs` (new)
**What:** `public static MetadataFileFieldSnapshot Capture(Issue issue)` producing an immutable
record of every file-mapped value — the ComicInfo set (same fields as Step 2, as strings) **plus**
the sidecar set (tag category+weight tuples, `Issue.Rating`, `Review`, `BookAge`, per-page
type+rotation, `IsFinalIssue`, proposed-field values). `public static bool Differ(before, after)` —
value equality. Generalizes the `genreBefore/genreAfter` compare in
`IssuePropertiesScreenViewModel.Save` (lines 564-577).
**Depends on:** none
**Verify:** `MetadataFileFieldSnapshotTests` — identical issue ⇒ `Differ` false; a changed
ComicInfo field ⇒ true; a tag Weight-only change ⇒ true (sidecar caught) but Genre/Tags CSV
unchanged; a pure `Issue.Rating` change ⇒ true.

## Step 4: SevenZipEngine.UpdateEntries

**Files:** `src/Paperbunkr.Engine/IO/Provider/Readers/Archive/SevenZipEngine.cs` (edit),
`src/Paperbunkr.App.Tests/SevenZipEngineTests.cs` (new, or extend an existing engine test if one
exists)
**What:** `public static bool UpdateEntries(string file, int format, IReadOnlyDictionary<string,
byte[]> entriesByName)` — mirrors private `UpdateAll`: make a temp dir, write each `byte[]` to
`<tempdir>/<key>`, run one `7z u -t<zip|7z|tar> "<file>" "<tempdir>/<k1>" "<tempdir>/<k2>"` via the
existing `GetParameters`/`ExecuteUpdateProcess`, clean up, propagate `WriteErrorException`, return
bool. Reuse `MapFileFormat`/the `UpdateSettings.arg` switch for the `-t` flag.
**Depends on:** none
**Verify:** `SevenZipEngineTests` — build a real `.cbz` (`CbzFixture`), call `UpdateEntries` with
`ComicInfo.xml` + `paperbunkr.json` bytes, reopen the zip and assert both entries present with the
written content and the page entries untouched; repeat for `.cb7`/`.cbt`.

## Step 5: MetadataFileWriteBackService (replaces ComicInfoWriteBackService)

**Files:** `src/Paperbunkr.App/Services/MetadataFileWriteBackService.cs` (new),
`src/Paperbunkr.App/Services/PaperbunkrSidecar.cs` (new),
`src/Paperbunkr.App/Services/ComicInfoWriteBackService.cs` (delete),
`src/Paperbunkr.App.Tests/ComicInfoWriteBackServiceTests.cs` → rename/rewrite to
`MetadataFileWriteBackServiceTests.cs`
**What:**
- `enum MetadataWriteBackResult { Success, SkippedUnsupportedFormat, SkippedMissingFile,
  SkippedReadOnly, Failed }`; `readonly record struct MetadataWriteBackOutcome(result, string?
  fileName, string? errorMessage)`.
- `PaperbunkrSidecar` — record with `int Schema = 1` + the sidecar fields (Step 3 list);
  `ToJsonBytes()` / `TryParse(byte[])` via `System.Text.Json`. `FromIssue(Issue)` builder.
- `MetadataFileWriteBackService`: `Task<MetadataWriteBackOutcome> WriteAsync(int issueId, bool
  includeSidecar, CancellationToken)`. Own `PaperbunkrDbContext` (via `Func<>` seam for tests,
  default `PaperbunkrDb.CreateContext`); load the issue `.Include(Series).Include(Tags).
  Include(MetadataProposals).Include(Pages?/Bookmarks)`. Then:
  1. No `FilePath` / `IsPlaceholder` ⇒ `SkippedMissingFile`. `!File.Exists` ⇒ `SkippedMissingFile`.
     `new FileInfo(path).IsReadOnly` ⇒ `SkippedReadOnly`.
  2. Extension → format: `.cbz`→CBZ, `.cb7`→CB7, `.cbt`→CBT, a directory → folder-mode; anything
     else (`.cbr`/`.rar`/`.pdf`/…) ⇒ `SkippedUnsupportedFormat`.
  3. `var info = EmbeddedComicInfoReader.TryRead(path) ?? new ComicInfo();`
     `IssueToComicInfoMapper.Apply(issue, info);` `byte[] xml = info.ToArray();`
  4. `includeSidecar` ⇒ `byte[] json = PaperbunkrSidecar.FromIssue(issue).ToJsonBytes();`
  5. archive: `SevenZipEngine.UpdateEntries(path, format, entries)`; folder-mode:
     `File.WriteAllBytes(Path.Combine(dir,"ComicInfo.xml"), xml)` (+ sidecar). Wrap in try/catch →
     `Failed(ex.Message)`. Never throws.
  6. **`<Pages>` decision:** for v1, per-page data goes to the **sidecar only** — do not write
     `ComicInfo.<Pages>` (keeps the mapper simple and avoids clobbering CE-written page bookmarks
     we don't fully model). Note this in the service doc-comment; the design's "type + bookmark
     subset" is deferred.
**Depends on:** Steps 2, 4
**Verify:** `MetadataFileWriteBackServiceTests` (`CbzFixture`): full-field round-trip (write →
`EmbeddedComicInfoReader.TryRead` → assert); an unmodeled field (`AlternateCount` or a `<Pages>`
entry pre-seeded in the fixture) survives; sidecar entry written + `PaperbunkrSidecar.TryParse`
round-trips; `.cbr` ⇒ `SkippedUnsupportedFormat`; missing file ⇒ `SkippedMissingFile`; read-only ⇒
`SkippedReadOnly`; folder-mode comic writes both files; a deliberately corrupt archive ⇒ `Failed`,
no throw.

## Step 6: MetadataWriteBackQueue

**Files:** `src/Paperbunkr.App/Services/MetadataWriteBackQueue.cs` (new),
`src/Paperbunkr.App.Tests/MetadataWriteBackQueueTests.cs` (new)
**What:** Constructed with `Func<PaperbunkrDbContext>`, a `MetadataFileWriteBackService`, and
`Action<string,string> showToast` (mirrors `LiveFolderWatchService`'s ctor + test seam).
- `void Enqueue(int issueId, bool manual = false)` — coalesce by id (a `Dictionary<int,bool>`
  under a lock, OR-ing `manual`), `System.Timers.Timer` ~300 ms debounce, then a single-worker
  drain (`SemaphoreSlim(1,1)`), one `WriteAsync` at a time.
- At drain time read `AppSettings` once: skip everything if `!WriteMetadataToFiles`; skip
  non-`manual` items if `!WriteMetadataAutomatically`; `includeSidecar = WriteNativeSidecar`.
- Aggregate outcomes over the drain into one summary and `showToast("…", "Wrote N files · X
  skipped (.cbr) · Y failed")`. Single-issue automatic run that hits `SkippedUnsupportedFormat`
  still toasts the CE-style "saved to library only — Name.cbr can't be updated". `Failed` always
  toasts.
- `Task DrainNowAsync()` test hook (bypasses the timer).
- `IDisposable` (timer + semaphore), same as `LiveFolderWatchService`.
**Depends on:** Steps 1, 5
**Verify:** `MetadataWriteBackQueueTests` (temp DB + real `CbzFixture` files, short debounce via a
test ctor): debounce coalesces two `Enqueue(1)` into one write; ordering is serial; master-off ⇒
zero writes; automatic-off + non-manual ⇒ zero; automatic-off + `manual:true` ⇒ writes; summary
text for a mixed batch; sidecar written only when `WriteNativeSidecar`.

## Step 7: MainViewModel wiring

**Files:** `src/Paperbunkr.App/ViewModels/MainViewModel.cs` (edit)
**What:** Construct `MetadataWriteBackQueue` alongside `LiveFolderWatch` (line ~120), owned +
disposed by `MainViewModel`. Expose a `void EnqueueMetadataWriteBack(int id, bool manual = false)`
private method (delegates to the queue) and pass it as an `Action<int>` / `Action<int,bool>`
callback into the VMs that need it (Steps 8, 9, 10), threaded the same way `ShowToast` already is.
`ReadingScreenViewModel` does **not** need it. Also pass it into `PreferencesScreenViewModel` and
`LibraryScreenViewModel` constructors.
**Depends on:** Step 6
**Verify:** `dotnet build`; existing `MainViewModel`-touching tests still green.

## Step 8: Wire the six automatic trigger sites + retire the old path

**Files (edit):** `src/Paperbunkr.App/ViewModels/IssuePropertiesScreenViewModel.cs`,
`BulkIssuePropertiesScreenViewModel.cs`, `DetailScreenViewModel.cs`,
`MangaDetailScreenViewModel.cs`, `BulkSeriesPropertiesScreenViewModel.cs`,
`NeedsReviewViewModel.cs`
**Files (rewrite):** `src/Paperbunkr.App.Tests/IssuePropertiesWriteBackTests.cs`,
`BulkIssuePropertiesWriteBackTests.cs`
**What:** At each site, after its `SaveChanges()`, capture `MetadataFileFieldSnapshot` before/after
the edit and call the injected enqueue callback for each affected issue id **only when
`Differ` is true**:
1. `IssuePropertiesScreenViewModel.Save` — replace the `tagValuesChanged`/`TriggerComicInfoWriteBack`
   block (lines 564-577, 615-618, 623-651) with a snapshot compare over the full field set + a
   single `enqueue(issueId)`. Delete `TriggerComicInfoWriteBack`.
2. `BulkIssuePropertiesScreenViewModel` (lines 231-286) — drop the `tracksFileWriteBack` Genre/Tags
   special-case; snapshot each issue before/after; `enqueue(id)` per changed id. Delete the
   `TriggerComicInfoWriteBack` loop.
3. `DetailScreenViewModel.ReweightTag` (line 249) + the star-rating / quick-rate save (line 126) —
   `enqueue(issueId)` after `SaveChanges()` when the snapshot differs.
4. `MangaDetailScreenViewModel` — the "Apply from Provider" save (line ~145) and any inline
   metadata save: `enqueue` each affected id.
5. `BulkSeriesPropertiesScreenViewModel.Save` (line 91) — after save, `enqueue(id)` for every
   member issue of every edited series (the VM already loads them for the history snapshot).
6. `NeedsReviewViewModel.ResolveProposal` (accept path, line ~311) + `SeriesReassignmentResolver`
   accept — `enqueue` the affected issue id(s).
- Constructors of these VMs get an `Action<int>` (or `Action<int,bool>`) param, default no-op, wired
  from `MainViewModel` in Step 7. Keep the default-noop so existing unit tests construct them
  unchanged.
**Depends on:** Steps 3, 6, 7
**Verify:** rewritten `*WriteBackTests` assert the fake enqueue callback receives the right ids and
is *not* called when nothing file-mapped changed; full `Paperbunkr.App.Tests` green.

## Step 9: Preferences → Advanced "Comic File Metadata" group

**Files:** `src/Paperbunkr.App/ViewModels/PreferencesScreenViewModel.cs` (edit),
`src/Paperbunkr.App/Views/Preferences/AdvancedSection.axaml` (edit),
`src/Paperbunkr.App.Tests/PreferencesScreenViewModelTests.cs` (edit)
**What:**
- VM: three `[ObservableProperty] bool` + `OnXChanged → PersistBehaviorSetting(s => s.X = value)`;
  load in `Load()`. A `[RelayCommand] WriteAllMetadataToFiles` that (behind a confirm — reuse the
  existing two-step confirm or a dialog) enumerates every issue with a `FilePath` and calls the
  injected enqueue with `manual: true`, then a progress/summary toast.
- XAML: new `Border.groupHeader` "Comic File Metadata" above "Backup Manager". Three `CheckBox`es
  bound to the VM props; the lower two `IsEnabled="{Binding WriteMetadataToFiles}"`. Body
  `TextBlock` copy: modifies original files in place; lists non-writable formats (.cbr/.pdf). The
  "Write all library metadata to files now…" button + a `{Binding WriteAllMetadataStatus}` line.
  **Load `avalonia` router → `design-system` + `components` subskills before writing this XAML**
  (no hardcoded hex; use `PbText*`/`Pb*Brush` tokens and the shared `CheckBox`/`Button` classes;
  run `avalonia-pro-max/review-checklist` on the group before calling it done).
**Depends on:** Steps 1, 6, 7
**Verify:** `PreferencesScreenViewModelTests` — each toggle persists to `AppSettings` (temp-DB
pattern already in that file); `WriteAllMetadataToFiles` enqueues every filed issue with
`manual:true` against a fake queue. Manual on-screen check of the group (checkbox enable/disable
dependency, dark+light) — flagged, not automated (standing UIA barrier).

## Step 10: Library context-menu manual action

**Files:** `src/Paperbunkr.App/ViewModels/LibraryScreenViewModel.cs` (edit),
`src/Paperbunkr.App/ViewModels/LibraryContextMenuBuilder.cs` (edit),
`src/Paperbunkr.App.Tests/LibraryContextMenuBuilderTests.cs` (edit)
**What:**
- `LibraryScreenViewModel`: `[RelayCommand] WriteIssueMetadataToFiles(int issueId)` →
  `Selection.UnionForAction(issueId)` → enqueue each with `manual:true`.
  `[RelayCommand] WriteSeriesMetadataToFiles(int seriesId)` → expand to member issue ids → enqueue.
  A `bool CanWriteMetadataToFiles` fed from `AppSettings.WriteMetadataToFiles` (read in
  `LoadFromDatabase`).
- `LibraryContextMenuBuilder`: add `ContextMenuEntry.Item("Write metadata to files",
  _vm.WriteIssueMetadataToFilesCommand, row.Id, Symbol.Save, isEnabled: _vm.CanWriteMetadataToFiles)`
  in `BuildIssueMenu` (near "Show in Explorer"), and the series equivalent in `BuildSeriesMenu`.
**Depends on:** Steps 6, 7
**Verify:** `LibraryContextMenuBuilderTests` — the entry appears and is disabled when the setting
is off / enabled when on; command param is the row/card id.

## Step 11: Docs + full verification

**Files:** `docs/alpha-roadmap.md`, `docs/ce-feature-inventory.md` (edit)
**What:** Mark "file metadata write-back" shipped in the roadmap's "Still open" paragraph with a
summary (CE-parity settings, engine reuse, sidecar deviation, retired the Genre/Tags-only path).
Update the `ce-feature-inventory.md` §E row for CE's three write-back settings. Run the full
`Paperbunkr.App.Tests` + `Paperbunkr.Data.Tests` suites. Flag the one manual on-screen check
(Preferences group + a real drag-a-real-file end-to-end write) for the user.
**Depends on:** all
**Verify:** both full suites green (modulo the known `FileSystemWatcher`/book-scan timing flakes);
build clean, no new warnings.

---

## Test strategy summary

| Layer | Project | Fixture pattern |
|---|---|---|
| Mapper, snapshot, migration | `Paperbunkr.Data.Tests` | temp SQLite `DbContextOptions`; plain entities |
| Engine `UpdateEntries` | `Paperbunkr.App.Tests` (has `CbzFixture`) | real synthetic `.cbz`/`.cb7`/`.cbt` |
| Service, queue | `Paperbunkr.App.Tests` | `CbzFixture` + temp DB + `Func<PaperbunkrDbContext>` seam |
| Trigger VMs, Preferences, context menu | `Paperbunkr.App.Tests` | `DatabasePathOverride` temp DB; fake enqueue callback |
| End-to-end file write from a real edit | manual, on-screen | flagged — no unattended GUI automation |

No live network. No new test framework — xUnit + the existing fixtures throughout.
