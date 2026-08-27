# Cover Art Override — Design

*Follow-on from the manga detail screen work (docs/superpowers/specs/2026-08-23-manga-detail-
screen-design.md) — user feedback after seeing the shipped screen asked for "a global function to
change the thumbnail/cover art."*

## CE-parity check (standing rule)

CE has real precedent: `ComicBook.CustomThumbnailKey` + `MainForm.SetCustomBookThumbnail`
(`_reference/ComicRackCE/ComicRack/MainForm.cs:4289`). But CE's version **refuses to set a custom
thumbnail on any linked book** (`if (cb.IsLinked) return false;`) — it only ever applies to
unlinked/placeholder entries with no real file. Paperbunkr's own `Issue.CustomThumbnailKey` is a
straight port of that field, migrated in but never read or written anywhere in the App layer.

Confirmed with the user: this feature deliberately **deviates from CE** — it overrides the
displayed cover for any series/issue, linked file or not (e.g. useful when a manga's actual first
page is a blank/title page rather than art). `Issue.CustomThumbnailKey` itself is not used; the
override reuses the existing on-disk thumbnail cache instead (see below), so no schema change was
needed.

## Mechanism

No new schema. The existing per-issue JPEG cache (`CoverThumbnailPaths.GetCachePath(issueId)`,
already the single source every screen reads via `CoverImageCache.Get`) is overwritten directly:

- `CoverThumbnailService.TrySetCustomCover(int issueId, string sourceImagePath)` — loads the
  user-picked image, runs it through the same scale-to-400px-longest-edge + JPEG-quality-85
  pipeline `TryGenerateThumbnail` already uses for real comic pages, and **always overwrites**
  (unlike `TryGenerateThumbnail`'s presence-check, which is what makes this an override rather than
  a fill-the-gap generation). Calls `CoverImageCache.InvalidateMemoryOnly` (a new method) rather
  than `CoverImageCache.Invalidate` — the latter deletes the on-disk file, which would delete the
  one just written.
- `CoverThumbnailService.ResetCover(int issueId, string? filePath)` — deletes the cached file via
  the real `Invalidate` (file removal is exactly what's wanted here) and, when the issue has a
  linked file, regenerates immediately from the real decoded page rather than leaving the cover
  blank until something else happens to call `TryGenerateThumbnail` again.
- `FilePickerService.PickImageFileAsync(string title)` — new method (not on `IFilePickerService`,
  to avoid forcing three existing test fakes to implement a method they don't need) offering a
  multi-extension image filter (jpg/jpeg/png/webp/bmp) that `PickOpenFileAsync`'s single-pattern
  signature can't express.

Both service classes are constructed fresh per call (`new CoverThumbnailService()`,
`new FilePickerService()`) at each ViewModel call site, matching this app's established "no DI
container, construct stateless providers fresh" precedent (already used for tracker/metadata
providers).

## Entry points

Three, all funneling through the same `TrySetCustomCover`/`ResetCover` pair:

- **Header "Change Cover" action** on both `DetailScreenViewModel` (Western) and
  `MangaDetailScreenViewModel` — targets the series' designated cover issue
  (`Series.CoverIssueId`, tracked as `_coverIssueId` from `SeriesCardSample.FromSeries`).
- **Issues-tab tile context menu** (`DetailTabsViewModel`, Western screen) — "Set Cover…"/"Reset
  Cover" targeting that specific issue. Refreshes just that one `IssueCardSample` (its `CoverImage`
  is init-only, so the tile is swapped in place) rather than a full reload, so the rest of the
  current multi-selection survives.
- **Chapter-row context menu** (`MangaDetailScreenViewModel`'s Chapters tab) — same pair, targeting
  that chapter. Reloads the whole series display on success (`ReloadCurrentSeries`), since this
  screen has no per-row in-place refresh mechanism the way the Issues tab does.

## Testing

`CoverThumbnailServiceTests` covers: writing a decodable JPEG from a picked image regardless of any
linked file, overwriting an existing cached thumbnail (write timestamp advances), returning false
for an unreadable path, `ResetCover` regenerating from a linked file vs. leaving the cover blank
when there's no file to regenerate from. On-screen verification of the three UI entry points is
pending as of this write-up.
