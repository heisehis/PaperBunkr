# Icon action mapping

Per docs/superpowers/specs/2026-08-24-design-language-foundation-design.md's Iconography section:
one icon per action, audited against all 17 files that reference `Assets/Icons/*.png`, documented
here so future additions follow the same rule instead of drifting back into inconsistency.

## Converted to vector (`Styles/Icons.axaml`, `PbIcon*`) - this phase

These 7 are the icons actually used by the 5 FloatingPanel-migrated overlays
(`ReadingListPropertiesOverlay`, `IssuePropertiesScreen`, `BulkIssuePropertiesScreen`,
`QuickRateOverlay`, `MigrationOverlay`). Audited against every other occurrence of the same PNG
across all 17 consuming files - no conflicts found for any of these 7 (each already meant exactly
one action everywhere it appeared, before this phase touched anything).

| Action | Vector resource | Raster source (still used elsewhere until its own screen is touched) |
|---|---|---|
| Rate (star toggle) | `PbIconStar` | `Star.png` |
| Copy | `PbIconCopy` | `Copy.png` |
| Cancel / dismiss | `PbIconCloseCircle` | `Close_Circle.png` |
| Save | `PbIconSave` | `Save.png` |
| Success / confirmed | `PbIconCircleCheck` | `Circle_Check.png` |
| Open folder | `PbIconFolderOpen` | `Folder_Open.png` |
| Warning | `PbIconCircleWarning` | `Circle_Warning.png` |

## Converted to vector - Phase 2 (nav rail)

Per docs/superpowers/specs/2026-08-24-navigation-shell-motion-system-design.md - the rail is the
first thing Phase 2 touches, so per the same incremental-rollout principle, its own icons convert
now. Audited against every other occurrence of the same PNG across all consuming files.

| Action | Vector resource | Raster source |
|---|---|---|
| Home (nav) | `PbIconHome` | `Home.png` |
| Library (nav) | `PbIconBook` | `Book.png` |
| Books (nav) | `PbIconLayers` | `Layers.png` |
| Smart Lists (nav) | `PbIconFilter` | `Filter.png` |
| Reading Lists (nav) | `PbIconBookmark` | `Bookmark.png` |
| Undo / Redo | `PbIconUndo` | `Undo.png` (Redo reuses it, mirrored - see `RenderTransform="scaleX(-1)"` already used on the raster version) |
| Preferences (nav) | `PbIconSettings` | `Settings.png` |
| Pin/unpin nav rail | `PbIconPin` | *(new - no raster precedent)* |

Story Events already used `PbIconStar` (converted in Phase 1) - no change needed.

## Converted to vector - Phase 3 (Home screen)

Per docs/superpowers/specs/2026-08-24-home-screen-design.md - `Refresh.png` was this screen's only
consumer (grepped across every `.axaml` file, no conflicts found).

| Action | Vector resource | Raster source |
|---|---|---|
| Refresh (Home screen) | `PbIconRefresh` | `Refresh.png` |

## New vector icons - Phase 4b (Library toolbar)

Per docs/superpowers/specs/2026-08-27-library-browsing-4b-toolbar-rework-design.md §3 - the
two-zone toolbar's "View & Sort" button and its tabbed popup. No raster predecessor; these are
new glyphs for actions that didn't have a dedicated icon before (the old toolbar used
`Sort_Ascending.png`/`Layers.png`/`Window.png` on separate Filter/Sort/Group/Display pills, all
collapsed into one control here). `PbIconFilter`/`PbIconLayers`/`PbIconSearch`/`PbIconPlus`/
`PbIconChevronLeft`/`PbIconChevronRight` (already in the set) cover the rest of the toolbar.

| Action | Vector resource | Raster source |
|---|---|---|
| View &amp; Sort (toolbar button) | `PbIconViewSort` | *(none - new)* |
| Sort direction / `Sorted:` chip | `PbIconSortAsc` | *(none - new)* |
| View tab (display-mode) glyph | `PbIconGrid` | *(none - new)* |

## Converted to vector - Phase 6 (Reader chrome)

Per docs/superpowers/specs/2026-08-25-reader-chrome-design.md - `Skip_Back.png`/`Skip_Forward.png`
were each single-consumer (`ReaderScreen.axaml`'s page-turn buttons only, grepped, no conflicts).

| Action | Vector resource | Raster source |
|---|---|---|
| Previous page | `PbIconSkipBack` | `Skip_Back.png` |
| Next page | `PbIconSkipForward` | `Skip_Forward.png` |

## Converted to vector - Phase 6 icon pass (Reader chrome, follow-up)

Per docs/superpowers/specs/2026-08-27-reader-chrome-icon-pass-design.md - completes the reader
chrome's icon language. The Reader Chrome phase deliberately converted only the two page-turn
icons above and left every other reader control on a text glyph or emoji; this pass gives every
button in `ReaderScreen.axaml` a `Path.pbIcon` vector.

**New concepts (no raster precedent, same as `PbIconPin` in Phase 2):**

| Action | Vector resource |
|---|---|
| Overflow / open drawer | `PbIconMoreVertical` |
| Toggle fullscreen | `PbIconFullscreen` |
| Previous chapter | `PbIconChevronsLeft` |
| Next chapter | `PbIconChevronsRight` |
| Previous bookmark | `PbIconChevronLeft` |
| Next bookmark | `PbIconChevronRight` |
| Zoom out | `PbIconMinus` |
| Zoom in | `PbIconPlus` |
| Close drawer / dismiss inline row | `PbIconClose` (plain X; distinct from `PbIconCloseCircle`, the overlay-dismiss glyph) |
| Rotate clockwise | `PbIconRotateCw` |
| Rotate counter-clockwise | `PbIconRotateCcw` |
| Fit mode (leads the fit-mode pill) | `PbIconFit` |
| Reader reading-direction indicator | `PbIconArrowRight` / `PbIconArrowLeft` / `PbIconArrowDown` |

The three `Arrow*` glyphs are one action - the reading-mode pill's leading icon - selected at
runtime by `ReadingModeIconConverter` (`Views/ReadingModeIconConverter.cs`): the six `ReadingMode`
values collapse to three direction glyphs (LTR/HorizontalContinuous -> right, RTL/
HorizontalContinuousRightToLeft -> left, VerticalContinuous/Webtoon -> down).

`PbIconRotateCw` has **three** consumers, all meaning "clockwise rotation" - the drawer's rotate-CW
button, the drawer's auto-rotate toggle row, and the thumbnail-rail rotation indicator. Recorded
here so a future audit reads that as intentional, not drift.

**Vector versions of an existing raster** (the `.png` stays on disk - still used by other screens
that haven't been through their own phase yet):

| Action | Vector resource | Raster source (still used elsewhere) |
|---|---|---|
| Zoom level (magnifier, leads the zoom-preset pill) | `PbIconSearch` | `Search_Magnifying_Glass.png` |
| Rename bookmark | `PbIconEdit` | `Edit_Pencil.png` |
| Commit inline rename (plain check; distinct from `PbIconCircleCheck`) | `PbIconCheck` | `Check.png` |
| Delete bookmark | `PbIconTrash` | `Trash_Empty.png` |
| Double-page spread | `PbIconBookOpen` | `Book_Open.png` |
| Auto-scroll | `PbIconPlay` | `Play.png` |

## Converted to vector - Phase 7 (Preferences rework)

Per docs/superpowers/specs/2026-08-28-preferences-rework-design.md - the Preferences screen was the
last surface still on the raster/OpacityMask pattern. Its groups were re-homed into 8 section
UserControls and every icon in them converted. `Loading` (Sync Metadata) folds into the existing
`PbIconRefresh` - a two-arrow circular glyph reads as "sync". Audited: `Folder_Add`, `Folder_Search`,
`Cloud_Upload`, `Archive` were each single-consumer (Preferences only) before this phase.

| Action | Vector resource | Raster source |
|---|---|---|
| Add a watched/book folder | `PbIconFolderAdd` | `Folder_Add.png` |
| Scan a folder now | `PbIconFolderSearch` | `Folder_Search.png` |
| Migrate from ComicRack CE | `PbIconCloudUpload` | `Cloud_Upload.png` |
| Back up the database now | `PbIconArchive` | `Archive.png` |
| Sync embedded metadata | `PbIconRefresh` | `Loading.png` (shared with Home refresh) |

## Converted to vector - Reading Lists + Story Events screen restyle

Per docs/superpowers/specs/2026-08-28-preferences-rework-plan.md Steps 14-15. `ReadingScreen.axaml`
and `EventsScreen.axaml` moved onto the design language alongside the Preferences rework. Icons
already vector (`PbIconEdit`, `PbIconCopy`, `PbIconRefresh`, `PbIconSearch`, `PbIconPlus`,
`PbIconCheck`, `PbIconTrash`, `PbIconClose`, `PbIconPlay`, `PbIconChevronRight`, `PbIconArrowDown`)
were reused. New geometries:

| Action | Vector resource | Raster source |
|---|---|---|
| Import a list (.CBL / .CSV) | `PbIconFileUp` | `File_Upload.png` |
| Export a list (.CBL / text) | `PbIconFileDown` | `File_Download.png` |
| Info / status banner | `PbIconInfo` | `Info.png` |

`Triangle_Warning` (reading-list "Missing" row) now uses `PbIconCircleWarning`; the "Read" affordance's
ad-hoc `<Path>` triangle is now `PbIconPlay`.

## Still raster (`Assets/Icons/*.png` + `Border.icon` OpacityMask pattern)

Unconverted for now, per the design doc's incremental-rollout principle - conversion happens as
each icon's consuming screen is touched in its own phase, not all at once. Listed here only as an
inventory checkpoint, not yet assigned canonical single-action mappings:

`Add_Plus`, `Bell_Notification`, `Globe`,
`Puzzle`, `Remove_Minus_Circle`,
`Sort_Ascending`, `Triangle_Warning`, `Window`.

(`Book_Open`, `Check`, `Edit_Pencil`, `Play`, `Search_Magnifying_Glass`, and `Trash_Empty` now
have vector equivalents from the Phase 6 icon pass but their PNGs are still consumed by screens
not yet reworked - see that section above.)

When one of these is converted, audit it the same way as the ones above (grep every consuming file,
confirm single-action use or resolve the conflict) before adding it to the table.
