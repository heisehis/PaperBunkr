# Cover Thumbnail Identity Validation — design

2026-08-27

## Problem

The cover-thumbnail cache is keyed **only** by the auto-increment primary key:

- Comics: `%AppData%\Paperbunkr\thumbnails\{issueId}.jpg` (`CoverThumbnailPaths.GetCachePath`)
- Books: `%AppData%\Paperbunkr\book-thumbnails\{bookId}.jpg` (`BookCoverThumbnailPaths.GetCachePath`)

Two code paths trust that filename with no check that the JPEG belongs to the entity
currently holding that id:

- `CoverThumbnailService.TryGenerateThumbnail` / `BookCoverThumbnailService.TryGenerateThumbnail`
  — "if `{id}.jpg` exists, skip, we're done".
- `CoverImageCache.Get` / `BookCoverImageCache.Get` — "if `{id}.jpg` exists, decode and serve it".

Any library rebuild that reassigns primary keys — re-import, re-running the ComicRack CE
migration, editing `LibraryFolderScanner` / import logic and re-scanning — leaves the cache
folder full of JPEGs mapped to the wrong comics. `CoverThumbnailPaths.DeleteCachedThumbnail`'s
doc comment already records the failure mode; the 2026-08-19 fix only patched per-issue
*deletion* call sites, so a full rebuild (which does not go through them) still produces
mismatches. This has now recurred three times.

The covers are not randomly shuffled — each wrong cover is whatever entity *previously* held
that primary key.

## Goal

A mechanism that automatically detects "this cached thumbnail does not belong to this comic"
and self-heals, regardless of which rebuild path caused the drift.

## Fingerprint

A short FNV-1a hash of **normalized full path + `"|"` + file size in bytes**.

- **Comics**: size comes from `Issue.FileSize` (`long?`, already persisted — "CE:
  `ComicBook.FileSize`").
- **Books**: `Book` has no size column and this branch is migration-sensitive (a stale EF
  snapshot was recently found and regenerated on it), so no schema change. The book card
  builder and the book generator `stat` the file (`FileInfo.Length`) instead — Books libraries
  are dozens of files, not thousands, so the cost is negligible.
- **Path normalization**: `Path.GetFullPath`, then `ToLowerInvariant()`, then `\` → `/`.
- **Tolerant inputs**:
  - Null / unknown size → hash the path only.
  - Null / empty path (a fileless placeholder issue — a deliberate CE deviation the current
    code already supports for user-set custom covers) → stem is `"{id}-nofile"`, giving a
    fileless issue's custom cover a stable home.

New shared helper in `Paperbunkr.App.Services`:

```csharp
public static class CoverFingerprint
{
    /// Returns "{id}-{hash:x8}", or "{id}-nofile" when filePath is null/empty.
    public static string Stem(int id, string? filePath, long? fileSizeBytes);
}
```

Used by **both** the comic and book paths so the two near-identical cache subsystems cannot
drift apart.

## Cache files become self-identifying

Filename changes from `{id}.jpg` to `{id}-{fphash}.jpg` (Books:
`book-thumbnails/{id}-{fphash}.jpg`). The `{id}-` prefix stays so per-entity operations
(deletion sweep, custom cover, reset) remain entity-scoped; the `-{fphash}` suffix is the
identity check.

### `CoverThumbnailPaths` / `BookCoverThumbnailPaths`

- `GetCachePath(string stem)` → `{ThumbnailDirectory}/{stem}.jpg`.
- `EnumerateForId(int id)` → all `{id}-*.jpg` in the directory (for the sibling sweep).
- `DeleteCachedThumbnail(int id)` — now globs and deletes every `{id}-*.jpg`. Still swallows
  `IOException`. Call sites (deletion, merge) only hold the id, which is why the glob is needed.
- The `ThumbnailDirectory` test-redirect hook is unchanged.

### `CoverThumbnailService` / `BookCoverThumbnailService`

- `TryGenerateThumbnail(int id, string filePath, long? fileSize, …)` — computes the stem,
  `dest = GetCachePath(stem)`. If `dest` exists → return true. Otherwise **delete every stale
  `{id}-*.jpg` sibling first**, then decode + save `dest`. The sibling-sweep clears an orphan
  the moment its entity is regenerated.
- `TrySetCustomCover` / `ResetCover` take the same `(id, filePath, fileSize)` identity args so
  they write to the *current* stem. Call sites (`DetailScreenViewModel`, `DetailTabsViewModel`,
  `MangaDetailScreenViewModel`, `PaperbunkrApplication`) already hold the `Issue`.
- `CoverImageCache.InvalidateMemoryOnly` / `Invalidate` take the stem (or id, for `Invalidate`
  which also deletes files).

### `CoverImageCache` / `BookCoverImageCache`

- `Get(string stem)` (was `Get(int)`) — the bounded in-memory LRU is re-keyed on the stem
  string; on-disk path is `GetCachePath(stem)`. A miss returns `null` (the card's
  `CoverBrush` fallback shows) and is still not cached — the existing self-healing rationale is
  unchanged.
- `Invalidate(int id)` — drops any in-memory entries for that id (LRU scan by `"{id}-"` prefix)
  and calls `DeleteCachedThumbnail(int id)`.

### `CoverImageConverter` / `BookCoverImageConverter`

`Convert` takes a `string` stem instead of an `int`:

```csharp
value is string stem ? CoverImageCache.Get(stem) : null;
```

## Card models carry the key

- **`SeriesCardSample.CoverKey`** (`string?`) — computed in `FromSeries` from the resolved
  cover issue's `Id` / `FilePath` / `FileSize` (all already loaded). The `CoverIssueId`
  binding in `LibraryScreen.axaml` (Panorama, grouped, poster-grid, cover-only templates)
  becomes a `CoverKey` binding. `CoverIssueId` stays as a property for its non-cover consumers
  (Continue Reading, `DetailScreenViewModel`, `MangaDetailScreenViewModel`).
- **`IssueListRow.CoverKey`** (`string?`) — computed property from its existing `Id` /
  `FilePath` / `FileSize`. The `Id`-through-`CoverImageConverter` bindings in
  `LibraryScreen.axaml` (compact / details / list rows) become `CoverKey` bindings.
- **`IssueCardSample` / `BookCardSample`** and the eager `CoverImageCache.Get(entity.Id)` call
  sites — `SmartScreenViewModel`, `DetailTabsViewModel` (×2), `DetailScreenViewModel` (issue
  focus), `IssuePropertiesScreenViewModel`, `ReaderScreenViewModel` (chapter transition),
  `ReadingListSpotlightSample`, `SpotlightIssueSample` — switch to
  `Get(CoverFingerprint.Stem(entity.Id, entity.FilePath, entity.FileSize))`. Each already has
  the entity in scope. `BookCardSample.FromBook` stats the file for size.

## Self-healing sweep

`GenerateAllAsync` (both services) becomes fingerprint-aware:

1. **Regeneration pass** — candidate = every issue / book whose *fingerprinted* destination
   file is absent → `TryGenerateThumbnail`. A rebuild that re-imported the same files at the
   same paths produces identical stems, so nothing regenerates. Reused-id and moved-file cases
   produce a new stem whose file is absent → regenerated, and the stale sibling is swept.
2. **Orphan GC** (new, runs after the regeneration pass) — build the `HashSet<string>` of
   currently-valid stems from the same query; one `Directory.EnumerateFiles(dir, "*.jpg")`;
   delete any file whose stem is not in the set. Covers deleted series / deleted issues /
   pre-upgrade `{id}.jpg` files.

Presence-based and cheap when everything already matches: one query, N `File.Exists`, one
directory listing.

### Triggers

Per the "auto on library load + startup" decision:

- **Startup** — `MainViewModel` constructor fires `new CoverThumbnailService().GenerateAllAsync`
  and `new BookCoverThumbnailService().GenerateAllAsync` as background fire-and-forget tasks
  (with a `Progress` sink that does nothing). Today nothing runs on startup — only scans,
  migration, and the Preferences "Generate Covers" button do.
- **Library screen load** — `LibraryScreenViewModel` kicks a background `GenerateAllAsync`,
  guarded by a `static` "already running" flag (a `SemaphoreSlim(1, 1)` tried with a zero
  timeout, or a plain `Interlocked` bool) so rapid rail navigation does not stack passes.
- **Existing hooks unchanged** — `LiveFolderWatchService`, `MigrationViewModel`,
  `PreferencesScreenViewModel`, `BooksScreenViewModel`.

## One-time effect on upgrade

Existing `{id}.jpg` files (no `-{fphash}`) match no valid stem → ignored on read, then deleted
by the first orphan-GC pass. The first startup after this change regenerates the whole cover
cache once — a few minutes of background work for a 2000+ comic library, non-blocking.
Deliberate: migrating the old files by re-deriving their identity is not worth the complexity.

## Testing

New `CoverFingerprintTests`:

- Deterministic for identical inputs across calls.
- Path normalization — `C:\X\a.cbz`, `c:/x/a.cbz`, `C:\X\A.CBZ` all produce the same stem.
- Size sensitivity — same path, different size → different stem.
- Null size → stable path-only stem; null/empty path → `"{id}-nofile"`.

Extend `CoverThumbnailServiceTests`:

- Generating writes `{id}-{fp}.jpg`, not `{id}.jpg`.
- Regenerating after the issue's path changes deletes the stale `{id}-{oldfp}.jpg` sibling.
- **Id-reuse scenario** — cache holds `411-{fpA}.jpg`; an issue with id 411 now points at a
  different file (`fpB`); `CoverImageCache.Get("411-{fpB}")` returns null (not the `fpA`
  image), and `GenerateAllAsync` regenerates `411-{fpB}.jpg` and GCs `411-{fpA}.jpg`.

Extend `CoverImageCacheTests`:

- `Get(stem)` returns null when only a mismatched-stem file exists for that id.

New orphan-GC test (both services):

- A `*.jpg` for an id with no matching current entity is removed; a valid-stem file is kept.

Books: mirror the service and GC tests in the `BookCoverThumbnailService` / `BookCoverImageCache`
test files.

## Out of scope

- "Same path, same size, file content silently changed in place" — not the reported failure.
  A third fingerprint component (mtime, or a content hash) can be added later if it ever
  becomes a real problem.
- `ArcCoverImageCache` (reading-list arc covers) — keyed by `ReadingList.Id`, a different
  lifecycle that library rebuilds do not touch. Not implicated.
- No change to `Issue` / `Book` schema.
