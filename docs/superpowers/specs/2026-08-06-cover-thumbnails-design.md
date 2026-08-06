# Cover Thumbnails + Library List View

*Date: 2026-08-06. Follows the Migration UX polish pass, once the real 371-series/2072-issue CE
library was migrated in and it became clear the Library/Detail/Reader screens still show
deterministic color-gradient placeholders (`SeriesCardSample.CoverBrushFor`) instead of real cover
art. ComicRack CE always had real thumbnails - the engine machinery for it
(`Paperbunkr.Engine/IO/Cache`: `ThumbnailManager`, `ThumbnailDiskCache`, `ImagePool`,
`ThumbnailKey`, `ICustomThumbnail`) was faithfully ported from the decompile, it has just never
been wired into Paperbunkr's Avalonia UI. This spec builds fresh, minimal Avalonia-native code on
top of the already-proven Reader Canvas decode pipeline (`PageImageDecoder`) rather than reusing
that `System.Drawing.Bitmap`-based CE machinery, matching the precedent the Reader Canvas work
already set.*

## 1. Cover Thumbnail Service

New `CoverThumbnailService` in `Paperbunkr.App.Services`:

- Reuses `PageImageDecoder.TryOpen(issue.FilePath)` → `GetPage(0)` to decode an issue's first page
  - the same proven path the Reader Canvas already uses, no new archive-reading code.
- Resizes the decoded page down to a thumbnail (longest edge ~400px, aspect preserved) and saves it
  as JPEG to `%AppData%\Paperbunkr\thumbnails\{issueId}.jpg`.
- **One thumbnail per Issue, not per Series.** A Series' "cover" is whichever issue its
  `CoverIssueId` points to, falling back to its first issue by number if unset - Library cards look
  up that issue's cached file rather than a separate series-level image being stored. No duplicate
  decode/storage.
- Cache is presence-based: if `{issueId}.jpg` already exists, skip it. No timestamp/invalidation
  logic for v1 - re-running "Generate Covers" after adding new comics only fills in the gaps rather
  than redoing the whole library.
- Per-issue decode failures (corrupt archive, unsupported format, missing file) are caught and
  skipped - one bad file doesn't stop the batch, and isn't retried automatically (no cached file
  means it's naturally retried on the next "Generate Covers" run).

## 2. The "Generate Covers" action

- `CoverThumbnailService.GenerateAllAsync(IProgress<(int done, int total)>)` enumerates every
  `Issue` with a non-null `FilePath` that doesn't already have a cached thumbnail, decodes+saves
  each on a background `Task.Run` (matching the pattern `MigrationViewModel.Commit()` already uses
  correctly - never blocking the UI thread), reporting progress as it goes.
- **Trigger 1 - Migration Results stage**: a "Generate Covers" button next to "View Needs
  Review"/"Migrate Again" in `MigrationOverlay.axaml`'s Results panel, since that's the moment
  hundreds of new covers are needed at once. Shows a progress bar reusing the existing
  `IsBusy`/`ProgressBar` visual pattern already used in the Commit stage.
- **Trigger 2 - Library toolbar**: a standalone "Generate Covers" button on
  `LibraryScreenViewModel` for later re-runs (new comics added outside a migration). Same service
  call, same progress UI.

## 3. Wiring real covers into Library/Detail cards

- New static `CoverImageCache` (in-memory `Dictionary<int issueId, Bitmap>`) loads a cached
  thumbnail file into an Avalonia `Bitmap` once and reuses it, so scrolling/re-rendering the grid
  doesn't re-decode JPEGs from disk repeatedly. Kept resident for the app's lifetime - no eviction
  needed at this scale (a few thousand ~400px thumbnails).
- `SeriesCardSample.FromSeries` and the `IssueCardSample` construction in
  `DetailTabsViewModel.LoadSeries` both gain a nullable `Bitmap? CoverImage`: looks up the cache
  (loading from disk on first miss), `null` if no thumbnail has been generated yet for that issue.
- `CoverBrush` (the existing gradient) stays exactly as-is and becomes the fallback - XAML shows the
  `Image` when `CoverImage` is non-null, the gradient `Border` otherwise. Un-generated covers keep
  looking like they do today; covers replace themselves with real art incrementally as "Generate
  Covers" processes the library.

## 4. Reader page rail (real per-page thumbnails)

- `ReaderScreenViewModel` already holds an open `_decoder` for the current issue and currently
  fills `Thumbnails` with the `CoverBrush` gradient. `PageImageDecoder`'s existing cache only keeps
  3 pages resident (previous/current/next) by design, so pulling every page through
  `GetPage(pageIndex)` to build a full rail would fight that eviction policy.
- Add `GetThumbnail(pageIndex)` to `IPageImageDecoder`/`PageImageDecoder`: decodes and immediately
  downsizes a page without going through the 3-page full-res cache. A separate, small in-memory
  thumbnail cache (all pages of the *currently open issue only* - cheap at thumbnail size) lives
  alongside the existing full-res one.
- Falls back to the `CoverBrush` gradient per-thumbnail if decode fails for a given page, matching
  the Reader Canvas's existing "one bad page doesn't break the rest" behavior.

## 5. Library Grid/List view toggle

- `LibraryScreenViewModel` gains a `LibraryViewMode` (`Grid`/`List`) observable property with
  `IsGridView`/`IsListView` computed flags - the same "mode enum + computed `Is*` flags +
  `IsVisible` panel switching" pattern already used throughout this app (Migration overlay stages,
  `MainViewModel.CurrentScreen`), not a new pattern.
- Two small toggle buttons (grid-icon / list-icon) replace/sit alongside the current decorative
  "Display ▾" button in `LibraryScreen.axaml`.
- **List mode** is a new row template bound to the same `Covers` collection: thumbnail (reusing
  `CoverImage`/`CoverBrush` from §3) + series name + `ContentType · N issues` + unread badge. A
  second `ItemsControl` toggled via `IsVisible`, same as the grid one.
- Scope boundary: only the Grid↔List switch. The existing "Grid density" slider and sort-checkbox
  rows in that dropdown stay exactly as decorative as they are today - unrelated to this feature.
- Not persisted across restarts for v1 (resets to Grid each launch) - no user-settings/preferences
  store exists yet in the Data layer, and adding one is a separate, larger piece of work than this
  feature warrants.

## Testing

- `CoverThumbnailServiceTests` (new, `Paperbunkr.App.Tests` - needs the `Avalonia.Headless`
  bootstrap already used there for `Bitmap` construction, per `TestAppBuilder.cs`): generates a
  thumbnail from the existing `CbzFixture` test archive, asserts the output file exists and is a
  valid decodable image; asserts a second `GenerateAllAsync` run skips an issue whose thumbnail
  already exists (presence-based caching); asserts a corrupt/missing-file issue is skipped without
  throwing and without stopping the rest of the batch.
- `CoverImageCacheTests`: asserts repeated lookups for the same issue id return the same `Bitmap`
  instance (no redundant disk reads); asserts a miss (no cached file) returns `null` rather than
  throwing.
- Manual verification against the real migrated library (371 series/2072 issues) is still required
  for the actual visual/perf check, per the project's standing note that GUI screen-automation isn't
  reliable for this native desktop app - build+test verification plus asking the user to drive the
  real UI, not simulated clicking.
