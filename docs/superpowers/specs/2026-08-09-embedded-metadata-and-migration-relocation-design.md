# Book Folders: Embedded ComicInfo.xml Metadata + Migration Entry-Point Relocation

*Date: 2026-08-09. Two related follow-ups bundled at the user's request. Part 1 is the fast-follow
docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md §2 explicitly deferred
("richer metadata is a clean fast-follow once this lands"), triggered by a real user report: added
a Book Folder, no metadata or thumbnails populated. Thumbnails were a genuine bug, already fixed
same session. Metadata was a deliberate v1 scope cut, not a bug - this spec closes it. Part 2 is an
IA cleanup surfaced while investigating a separate "migration didn't populate the library" report
from the same session (also already root-caused and fixed - a Library-screen staleness bug,
unrelated to this spec).*

## 1. Embedded ComicInfo.xml metadata reading

**Correcting the original spec's own scoping claim.** §2 of the 2026-08-07 spec said this needed
"a per-archive-type entry reader wired to [`ComicInfoProvider`], which doesn't exist yet outside
`PageImageDecoder`'s page-only access path." That's not accurate: `ComicProvider` - the base class
every archive reader (`ArchiveComicProvider` → cbz/cbr/7z) already inherits from - implements
`IInfoStorage` directly, with a working `LoadInfo()` that reads `ComicInfo.xml` from the archive.
`ComicBook.RefreshInfoFromFile` already uses exactly this via `imageProvider as IInfoStorage`.
Confirmed with a throwaway spike this session (not committed): built a real cbz with a real
embedded `ComicInfo.xml` (via `ComicInfo.ToArray()`, the same serializer CE itself uses), opened it
through `Providers.Readers.CreateSourceProvider`, cast to `IInfoStorage`, called `LoadInfo` -
correctly returned Series/Number/Writer. The mechanism already exists; it's just never been called
from the App layer. This is a wiring job, not new archive-format plumbing.

**Precedence: embedded metadata wins over filename parsing when present.** Matches CE's own
behavior - embedded `ComicInfo.xml` is authoritative when it exists; filename parsing
(`ComicNameInfo.FromFilePath`, already in place) is the fallback for files without it. Per-field,
not all-or-nothing: if the embedded `Series` is blank but `Number` is set, the filename-parsed
series name is kept and the embedded number is used - an embedded field only overrides when it
actually has a value.

**Field scope: full field set**, matching what CE migration already maps (Title, Writer,
Penciller, Inker, Colorist, Letterer, CoverArtist, Editor, Translator, Publisher, Imprint, Genre,
Summary, Notes, Review, Characters, Teams, Locations, Tags, AgeRating, LanguageISO, Format, Web,
PageCount, AlternateSeries/Number, StoryArc, SeriesGroup, Year/Month/Day, Count) - not a smaller v1
subset. A book arriving via a folder scan should carry the same richness as one arriving via
migration.

### 1.1 Shared field mapping

`CeLibraryMigrator.MapIssue(ComicBook book)` (`Paperbunkr.Data.CeMigration`) already maps this
exact field set, but takes a `ComicBook`, not a `ComicInfo` - and `ComicBook`'s runtime fields
(`FilePath`, `AddedTime`, `ReleasedTime`, `OpenedTime`, `LastPageRead`, `FileIsMissing`,
`CustomThumbnailKey`) are declared on `ComicBook` itself, not the base `ComicInfo` class, so
`MapIssue` can't simply be widened to accept `ComicInfo`.

Split it: new `public static void MapStoryFields(ComicInfo info, Issue issue)` on
`CeLibraryMigrator`, containing everything `MapIssue` currently maps that genuinely lives on
`ComicInfo` (i.e. everything except the seven `ComicBook`-only runtime fields above).
`MapIssue(ComicBook book)` becomes a thin wrapper: call `MapStoryFields(book, issue)`, then set the
`ComicBook`-only fields itself. Behavior for existing Migration callers is unchanged - this is an
extraction, not a rewrite. `LibraryFolderScanner` becomes the second caller of `MapStoryFields`.

### 1.2 Scan pipeline changes

In `LibraryFolderScanner.ScanAll`'s existing per-file loop, after
`ComicNameInfo.FromFilePath(file)`:

- Open the file via `Providers.Readers.CreateSourceProvider(file)` + `Open(async: false)` (same
  call `PageImageDecoder.TryOpen` already makes - this is a separate, short-lived open just for
  metadata, not the page decoder itself, disposed immediately after reading).
- Cast to `IInfoStorage`; if non-null, call `LoadInfo(InfoLoadingMethod.Complete)`.
- If that returns a non-null `ComicInfo`: resolve `seriesName`/`Number`/`Volume`/`Year` using the
  embedded-wins-when-present rule above, call `MapStoryFields` to fill in the rest, then set
  `FilePath`/`AddedTime` as today. "Has a value" per field, matching `ComicInfo`'s own unset
  sentinels (and the same check `LibraryFolderScanner` already applies to the filename-parsed
  values today): `Series`/`Number` via `!string.IsNullOrWhiteSpace(...)`, `Volume`/`Year` via
  `> 0` (unset is `-1`).
- If the cast fails, `LoadInfo` returns null (no `ComicInfo.xml` in the archive), or the format
  doesn't support it (e.g. PDF): identical to today's filename-only path. No behavior change for
  files without embedded metadata.

Series find-or-create is unchanged: same case-insensitive exact-name match against the in-memory
`seriesByName` dictionary, just now keyed on the (possibly embedded-sourced) name instead of only
the filename-parsed one. No fuzzy conflict resolution added - that's real, separate scope
(Migration's `SeriesNameMatcher`) that nothing today requires; revisit only if exact-match causes
real duplicate-series complaints in practice.

### 1.3 Error handling

Reuses the scan loop's existing per-file `try/catch` ("one bad file doesn't stop the batch") - a
corrupt or malformed `ComicInfo.xml`, or any exception during the metadata-read attempt, just
degrades that one issue to filename-only. No new error-handling surface.

## 2. Migration entry-point relocation

Today, CE migration is a standalone rail-nav icon (`MainWindow.axaml`, `OpenMigrationOverlayCommand`)
that opens a full-window modal overlay (`MigrationOverlay.axaml`, unchanged Locate → Preview →
Conflicts → Commit → Results flow). Book Folders scanning - the *other* way to add comics to the
library - lives inside Preferences → Libraries. Moving migration's entry point alongside it puts
every "get comics into my library" action in one place.

**What moves**: the rail-nav icon and its badge are removed from `MainWindow.axaml`. A new
"Migrate from ComicRack CE" section is added to Preferences → Libraries, styled like the existing
Book Folders `groupBox`, with a button bound to the same `OpenMigrationOverlayCommand`.

**What doesn't move**: the overlay itself (`MigrationOverlay.axaml`,
`MigrationOverlayViewModel`, `IsMigrationOverlayOpen`, `CloseMigrationOverlayCommand`) is
unchanged - it stays a full-window modal rendered at `MainWindow.axaml` root, since it needs to
render over whichever screen is active, same as today.

**Needs Review badge**: today a small accent dot on the rail icon, bound to
`Migration.NeedsReview.HasPendingItems`. Moves to sit next to the new button in Preferences →
Libraries only - not duplicated onto the Preferences rail icon itself. Simpler, and matches Book
Folders' own convention of surfacing its state only inside Preferences rather than on the rail.

## Testing

- Extend `CbzFixture` with an optional embedded-`ComicInfo.xml` parameter (verified working via
  this session's spike - real `ComicInfo.ToArray()` bytes as a real zip entry).
- `LibraryFolderScannerTests`: embedded metadata wins over a deliberately-misleading filename
  (Series/Number/Volume/Year and a few extra fields like Writer/Publisher all come from the
  embedded data); malformed `ComicInfo.xml` falls back to filename-only without failing the scan;
  existing filename-only tests stay unchanged (no regression when no embedded data exists).
- `CeLibraryMigratorTests`: unchanged assertions should keep passing after the `MapIssue`/
  `MapStoryFields` split, proving the extraction didn't change Migration's observable behavior.
- `PreferencesScreenViewModelTests`: new "Migrate from ComicRack CE" button in the Libraries tab
  triggers `OpenMigrationOverlayCommand`.
- `MainViewModelTests`: rail nav no longer has a migration icon/command tied to it directly (the
  overlay-open/close commands and `IsMigrationOverlayOpen` themselves are unchanged and still
  tested as today).
- Manual verification: same no-GUI-automation approach as prior specs - build + run real tests,
  then ask the user to scan a folder containing comics with real embedded `ComicInfo.xml` and
  confirm the Detail screen actually shows the real metadata, and confirm the Preferences →
  Libraries migration button opens the same overlay as before.
