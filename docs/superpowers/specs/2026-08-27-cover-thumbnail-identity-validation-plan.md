# Cover Thumbnail Identity Validation — Implementation Plan
*Implements: docs/superpowers/specs/2026-08-27-cover-thumbnail-identity-validation-design.md*

## Step 1: `CoverFingerprint` helper
**Files:** `src/Paperbunkr.App/Services/CoverFingerprint.cs` (new),
`src/Paperbunkr.App.Tests/CoverFingerprintTests.cs` (new)
**What:** `public static class CoverFingerprint { static string Stem(int id, string? filePath, long? fileSizeBytes); }`.
Returns `"{id}-{hash:x8}"` where hash = FNV-1a over `normalizedPath + "|" + (size?.ToString() ?? "")`.
Normalized path = `Path.GetFullPath` → `ToLowerInvariant()` → `\`→`/`. Null/empty path → `"{id}-nofile"`
(no hash). Reuse the FNV-1a constants already in `SeriesCardSample.StableHash`.
**Depends on:** none
**Verify:** new `CoverFingerprintTests` — determinism, path-case/separator normalization, size
sensitivity, null-size (path-only) and null-path (`-nofile`) branches.

## Step 2: Comic path helper + service + cache re-key
**Files:** `CoverThumbnailPaths.cs`, `CoverThumbnailService.cs`, `CoverImageCache.cs`,
`Views/CoverImageConverter.cs` (edit)
**What:**
- `CoverThumbnailPaths`: add `GetCachePath(string stem)`, `EnumerateForId(int id)`
  (`{id}-*.jpg` glob), `AllThumbnailFiles()`. `DeleteCachedThumbnail(int id)` deletes every
  `{id}-*.jpg`. Keep `ThumbnailDirectory` test hook. Drop/replace the old `GetCachePath(int)`.
- `CoverThumbnailService`: private `StemFor(int issueId)` loads the issue via `_contextFactory`
  and returns `CoverFingerprint.Stem(id, FilePath, FileSize)`.
  `TryGenerateThumbnail(int issueId, string filePath, long? fileSize = null)` — compute stem,
  dest = `GetCachePath(stem)`; if exists return true; else delete `EnumerateForId(issueId)`
  siblings, then decode+save. `TrySetCustomCover(int issueId, string src)` and
  `ResetCover(int issueId, string? filePath)` keep their signatures, resolve the stem via
  `StemFor`. `ResetCover` passes the looked-up `FileSize` into `TryGenerateThumbnail`.
- `CoverImageCache`: `_cache` keyed `string` (stem). `Get(string stem)` core;
  `Get(int id, string? filePath, long? fileSize)` convenience → `Get(CoverFingerprint.Stem(...))`.
  `Invalidate(int id)` removes in-memory entries whose key starts `"{id}-"` and calls
  `DeleteCachedThumbnail(int id)`. `InvalidateMemoryOnly(string stem)`.
- `CoverImageConverter.Convert`: `value is string stem ? CoverImageCache.Get(stem) : null`.
**Depends on:** Step 1
**Verify:** build; `CoverThumbnailServiceTests` / `CoverImageCacheTests` updated in Step 6.

## Step 3: Comic call sites
**Files:** `Models/SeriesCardSample.cs`, `Models/IssueListRow.cs`, `Models/IssueCardSample.cs`,
`Models/ReadingListSpotlightSample.cs`, `Models/SpotlightIssueSample.cs`,
`ViewModels/SmartScreenViewModel.cs`, `ViewModels/DetailTabsViewModel.cs`,
`ViewModels/DetailScreenViewModel.cs`, `ViewModels/MangaDetailScreenViewModel.cs`,
`ViewModels/IssuePropertiesScreenViewModel.cs`, `ViewModels/ReaderScreenViewModel.cs`,
`Plugins/PaperbunkrApplication.cs`, `Views/LibraryScreen.axaml` (edit)
**What:**
- `SeriesCardSample`: add `string CoverKey` (computed in `FromSeries` from the resolved cover
  issue's `Id`/`FilePath`/`FileSize`); keep `CoverIssueId` for its non-cover consumers.
- `IssueListRow`: add `string CoverKey => CoverFingerprint.Stem(Id, FilePath, FileSize)`.
- `LibraryScreen.axaml`: the 8 `CoverImageConverter` bindings switch from `CoverIssueId` / `Id`
  to `CoverKey`.
- Eager `CoverImageCache.Get(entity.Id)` sites → `Get(entity.Id, entity.FilePath, entity.FileSize)`.
  `DetailScreenViewModel`/`MangaDetailScreenViewModel` series-cover lines use `card.CoverKey`.
- `PaperbunkrApplication.GetComicThumbnail` → `CoverThumbnailPaths.GetCachePath(CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize))`.
**Depends on:** Step 2

## Step 4: Books mirror (path-only fingerprint — no `Book.FileSize` column)
**Files:** `BookCoverThumbnailPaths.cs`, `BookCoverThumbnailService.cs`, `BookCoverImageCache.cs`,
`Models/BookCardSample.cs`, `ViewModels/BooksScreenViewModel.cs`, `Views/CoverImageConverter.cs`
(`BookCoverImageConverter`) (edit)
**What:** same shape as Steps 2–3 but `CoverFingerprint.Stem(id, path, null)` everywhere
(books have no persisted size; path alone still fixes id-reuse). `TryGenerateThumbnail`
keeps its `BookFormat format` param. Document the comics-have-size / books-path-only asymmetry
in `BookCoverThumbnailPaths`' doc comment.
**Depends on:** Step 1

## Step 5: Self-healing sweep + triggers
**Files:** `CoverThumbnailService.cs`, `BookCoverThumbnailService.cs`,
`ViewModels/MainViewModel.cs`, `ViewModels/LibraryScreenViewModel.cs` (edit)
**What:**
- `GenerateAllAsync` (both): candidate = entity whose *stem* file is absent. After the
  regeneration loop, **orphan GC**: build `HashSet<string>` of valid stems from the same query,
  enumerate `*.jpg`, delete any file whose stem-name isn't in the set.
- `MainViewModel` ctor: fire-and-forget `CoverThumbnailService().GenerateAllAsync` +
  `BookCoverThumbnailService().GenerateAllAsync` (no-op `Progress`), swallow exceptions.
- `LibraryScreenViewModel` (on load / `LoadFromDatabase` completion): fire-and-forget
  `GenerateAllAsync`, guarded by a `static int` `Interlocked.CompareExchange` running-flag so
  rapid navigation doesn't stack passes.
**Depends on:** Steps 2, 4

## Step 6: Tests
**Files:** `CoverThumbnailServiceTests.cs`, `CoverImageCacheTests.cs`,
`BookCoverThumbnailServiceTests.cs`, `BookCoverImageCacheTests.cs`,
`PreferencesScreenViewModelTests.cs` (edit)
**What:** update existing assertions to the stem filename (via `CoverFingerprint.Stem` or a
`GetCachePath(stem)` helper). New cases: fingerprinted filename written; stale sibling swept on
regenerate; **id-reuse** — id 411 re-pointed at a different file no longer serves the old cover
and `GenerateAllAsync` regenerates + GCs the orphan; `Get(stem)` null on stem mismatch;
orphan-GC removes a file for a now-absent id, keeps a valid one. Mirror service+GC tests for books.
**Depends on:** Steps 2–5
**Verify:** `dotnet test src/Paperbunkr.App.Tests` green; then launch the app once (rebuilt
cover cache regenerates in the background, covers match titles).
