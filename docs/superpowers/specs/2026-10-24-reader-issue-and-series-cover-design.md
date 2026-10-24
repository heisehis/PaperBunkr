# Reader Issue Covers and Independent Series Artwork — Design

Date: 2026-10-24. Status: design, pending user review.

## Why

Two related cover behaviors need to be corrected and made explicit:

1. A reader page should be usable as the issue's cover image.
2. Changing a series cover currently writes the image to the selected cover issue. When that is
   issue #1, the series action also changes issue #1's own cover. Series artwork needs its own
   persisted choice.

The existing reader already stores `IssuePage.PageType = Cover`, but that tag does not influence
cover-thumbnail generation: `CoverThumbnailService` always decodes archive page 0. The series
screen's cover picker targets `_coverIssueId` and calls `CoverThumbnailService.TrySetCustomCover`,
which explains the issue-level side effect.

## CE behavior

Checked against `_reference/ComicRackCE`:

- CE tags pages as `FrontCover` and resolves the comic thumbnail from its cover-page metadata.
- CE allows several pages to be `FrontCover` and has a separate preferred-front-cover choice.
- The page-management context menu changes page type. No reader command to set the displayed page
  as cover was found.
- No separate series-cover setter or property was found; CE's cover image resolves through an
  individual issue.

Paperbunkr will follow the multiple-cover/preferred-cover model for issue pages and add a deliberate
series-artwork override of its own.

## Approaches considered

1. **(Chosen) Persist a preferred issue cover page and a separate series cover path.** Keep all
   `PageType.Cover` tags, store which one supplies the issue thumbnail, and store custom series art
   independently. This makes the two choices explicit and handles multiple cover pages.
2. Use the existing `PageType.Cover` tag as a single active cover and keep series art in a separate
   cache. This avoids a schema migration, but cannot represent multiple tagged covers and a preferred
   one separately.
3. Keep issue covers as custom image overrides and change only which issue represents the series.
   This does not let the user choose a reader page as a cover and leaves series artwork coupled to
   issue artwork.

## Data model

Add two nullable fields:

- `Issue.PreferredCoverPageNumber`: the zero-based archive page number used to generate the issue's
  cover thumbnail. It must refer to an existing page tagged `PageType.Cover`. Null means no explicit
  preference.
- `Series.CustomCoverPath`: the app-managed copy of custom series artwork. Null means the series
  uses its issue-based fallback. The image is copied into a durable per-series cover location under
  the app data directory; the database stores a relative path/key resolved beneath that directory,
  so relocating the configured app data root does not strand the image.

The fields are independent. Setting or clearing the series cover must never write an issue's custom
cover, `PreferredCoverPageNumber`, `IssuePage` rows, or `Series.CoverIssueId`.

For an issue without an explicit preferred page, the effective source page is the first page tagged
`Cover` in archive order; if none is tagged, use page 0. If a preferred page is changed away from
`PageType.Cover` or no longer exists in the archive, clear the preference and use that fallback.
Existing multiple `Cover` tags are retained. Choosing a cover from the reader adds/retains the
selected page's `Cover` tag and records it as preferred.

For a series without `CustomCoverPath`, preserve the current fallback: use its valid `CoverIssueId`,
or the first issue by number when no valid selection exists. A series custom cover takes precedence
over that fallback.

## Reader behavior

Add **Set as issue cover** to the main-page context menu for the currently displayed page. The page
thumbnail's existing **Page Type → Cover** action also makes that page the preferred issue cover.
Both call one issue-cover operation that:

1. Validates the page number and decodes the source page from the issue archive.
2. Writes the scaled thumbnail using the existing cover-thumbnail pipeline.
3. Tags that page as `Cover` and updates `PreferredCoverPageNumber`.
4. Removes any manually picked issue-cover override so it cannot continue masking the chosen page.
5. Invalidates decoded and grid cover caches and refreshes the reader and visible library/detail
   cover samples.

If the archive page cannot be decoded or the thumbnail cannot be written, keep the existing preferred
page, page tags, and custom issue cover. A successful reader action replaces a manual issue-cover
override, as expected from explicitly choosing an archive page.

Other page-type changes retain their existing behavior. If the selected preferred page is changed
from `Cover` to another type, clear the explicit preference and regenerate from the fallback page.

## Series artwork behavior

The Western and Manga Detail **Change Cover** pickers target the series, not `_coverIssueId`. Preserve
the existing sources (file, series issues, reading lists, and the external-provider source where
available); copy the chosen image into the series' own custom-cover location and persist its path.
Selecting a candidate from an issue copies that issue's effective cover into series artwork and does
not change the source issue.

The external-metadata **Apply Cover** action also writes a series custom cover. It must not call
`TrySetCustomCover` for the selected issue. A reset action clears the series path and deletes the
series custom image, returning the series to the `CoverIssueId`/first-issue fallback.

Issue-level pickers, including per-issue Detail actions, continue to write issue custom covers. They
do not change series artwork or its selected issue.

## Cover resolution and invalidation

Add a series-cover resolver alongside the issue cover resolver. It checks the series custom path
first and treats a missing or unreadable custom file as unset, then resolves the existing effective
cover issue. Series cards, library tiles, Detail
heroes, and cover-picker candidates must all use the same precedence rule. Existing asynchronous and
grid cover loading should remain off the UI thread; a namespaced cache key or equivalent typed source
identity must keep series cover entries from colliding with issue IDs.

Changing or resetting series artwork invalidates the series image and grid-cache entries and refreshes
all visible representations. Changing an issue's preferred page invalidates the issue's generated
thumbnail and aspect ratio, removes its custom override only after a replacement thumbnail is ready,
and refreshes issue and series representations that use that issue.

## Migration and compatibility

Add an EF migration for the nullable issue and series fields. Existing rows need no forced cover
selection: null issue preferences resolve from existing `Cover` tags and then page 0; null series
paths preserve current issue-based covers. Do not migrate existing issue custom-cover files into
series cover paths because the app cannot determine whether each file was intentionally chosen for
the issue, the series, or both.

Remote series remain read-only and continue to use their mirrored issue cover. The new custom series
path applies to locally managed series only. No book/PDF cover behavior is included.

## Verification

- Verify an issue with no cover tags uses archive page 0; one or more `Cover` tags use the first tag
  when preference is null; and an explicit preferred page wins when several are tagged.
- Verify setting a reader page tags and prefers it, refreshes its thumbnail, and removes a prior
  manual issue override without changing other `Cover` tags.
- Verify an undecodable page leaves the previous issue cover and metadata untouched.
- Verify changing a series cover through file, issue candidate, and external metadata updates the
  series artwork while leaving every issue cover and `CoverIssueId` unchanged.
- Verify reset restores the selected-issue fallback, then the first-issue fallback when no valid
  `CoverIssueId` exists.
- Verify migration defaults preserve existing issue/series cover display and series/issue IDs cannot
  collide in the asynchronous/grid cover caches.
- Manually inspect the reader page menu and Western/Manga Detail cover pickers, and confirm all
  screens refresh after set/reset.
