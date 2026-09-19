# Reader "Save Page As" + Cover Picker (Series/Reading List) — Design

Date: 2026-09-17. Status: design, pending user review (grilling rounds 1-2, all answered in chat).
Not yet approved for `writing-plans`.

## Why

Two independent asks in the same message:
1. Save/export the currently-displayed reader page to an image file.
2. When changing a cover (issue's own, or the series' representative cover), offer covers already
   owned — from the same series, or the same reading list(s) — as a source, not just a file-picker.

## CE-parity check (standing rule)

**Feature A has a real CE precedent**, confirmed directly:
`_reference/ComicRackCE/ComicRack/MainForm.cs:2167` (`ExportImage`) /
`MainForm.cs:2308` (`ExportCurrentImage`) — "Save Page as" `SaveFileDialog` with a multi-format
filter (jpg/bmp/png/gif/tif), remembered last-used filter index, filename
`"{Comic Caption} - Page {N}"`.

**Feature B has no CE precedent** — deliberate Paperbunkr-only enhancement, same category as
Duplicate Files review and the live-watch missing-files alert.

## Approaches considered

**Feature A format support:**
1. *(rejected)* Full CE parity (jpg/bmp/gif/tif/png) via a single `SaveFileDialog`-style multi-filter
   dialog — `IFilePickerService.PickSaveFileAsync` ([IFilePickerService.cs:28](../../../src/Paperbunkr.App/Services/IFilePickerService.cs))
   only accepts one fixed extension per call; supporting this would mean extending that shared
   interface, affecting every other caller (keyboard-shortcut export, CBL export, text export) for a
   format nobody uses on a comic page (bmp/gif/tif).
2. **(chosen)** Two menu items ("Save Page as PNG...", "Save Page as JPEG..."), each a single
   `PickSaveFileAsync` call with a fixed extension. No interface change, no format-memory setting -
   the menu item clicked *is* the format. YAGNI over CE parity here, deliberately.

**Feature B picker shape:**
1. *(rejected)* Extend "Set Cover…"'s single click straight into a gallery-only flyout (drop the
   file-browse entry point's current prominence).
2. **(chosen)** Small dialog, 3 tabs (Browse File / From Series / From Reading List). Preserves
   today's only option (file browse) as-is; the two new sources are just more tabs in the same
   shape.

## Feature A — Save Page As

**Trigger**: reader context-menu item, both the comic/manga reader (`ReaderScreenViewModel`) and
the Books/PDF reader (`PdfPageReaderScreenViewModel`) — the latter already has a region-*crop*
capture (`BookAnnotationCaptureService`) but nothing that exports the whole page. Both readers use
`ContextMenuHost.Provider` + a per-reader `IContextMenuProvider` builder
([ReaderPageContextMenuBuilder.cs](../../../src/Paperbunkr.App/ViewModels/ReaderPageContextMenuBuilder.cs)
for comics) - confirmed its `Build(target)` switch's `_ => null` fallback is exactly the "right-click
the main page, not a thumbnail" case, so the new entries replace that fallback rather than adding a
new wiring point.

**What gets exported**: matches CE - "whatever the current view renders" (a spread as shown, in
double-page mode; the single current page otherwise). Continuous-scroll reuses whichever page index
that mode already tracks for reading-progress persistence - no new "current page" concept.

**Shared implementation**: new `PageExportService` (`Paperbunkr.App.Services`) -
`bool TryExport(Bitmap page, string destPath, PageExportFormat format)`, encoding via Avalonia's own
`Bitmap.Save(stream, encoderOptions)` (same primitive `CoverThumbnailService` already uses for JPEG;
PNG is `Bitmap.Save(stream)` with no options). Both readers already hold (or can obtain) their
current rendered `Bitmap` for on-screen display - no re-decode needed, just save what's already in
memory.

**Filename**: `"{Series/Book title} - Page {N}"` (comic) / `"{Book title} - Page {N}"` (books) -
CE's convention, sanitized through whatever filename-sanitizing helper this codebase already uses
for exports (confirm at plan time - `FileUtility`-equivalent).

**Keybinding**: add to the existing remappable-keybinding system (`KeyBindingService`,
`NavigationKeyBindings`/`ZoomFitKeyBindings`/`DisplayKeyBindings` groups per
[[project_paperbunkr_keyboard_shortcuts_freeze_bug]]) - a new "Save Page as PNG" bindable command,
no default binding forced on top of existing shortcuts (avoid collision risk) unless a genuinely free
key exists.

## Feature B — Cover Picker (Series / Reading List sources)

**Entry points** (both already funnel through one method today):
- `DetailScreenViewModel.ChangeCoverAsync(issueId)` / `ChangeSeriesCoverAsync()` ([DetailScreenViewModel.cs:539,563](../../../src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs))
- `MangaDetailScreenViewModel`'s own equivalent pair
- `DetailTabsViewModel.ChangeIssueCoverAsync` (per-tile, Issues tab) - wired via
  `DetailIssueContextMenuBuilder`'s `"Set Cover…"` entry ([DetailIssueContextMenuBuilder.cs:41](../../../src/Paperbunkr.App/ViewModels/DetailIssueContextMenuBuilder.cs))

All three currently do: `FilePickerService().PickImageFileAsync(...)` →
`CoverThumbnailService.TrySetCustomCover(issueId, path)`. The new dialog replaces the direct
file-picker call at each site; "Browse File" tab does exactly what happens today.

**New `CoverPickerDialog`** (view + `CoverPickerViewModel`), 3 tabs:
- **Browse File** - today's `PickImageFileAsync` flow, unchanged, just relocated into the dialog.
- **From Series** - every other `Issue` in the same `Series` (excluding the target issue itself),
  rendered via the existing `AsyncCoverImage.SourceId` binding already used elsewhere (no new
  image-loading path). Click applies immediately (matches today's "pick file → apply" directness -
  no separate confirm step).
- **From Reading List** - every other `Issue` across *every* reading list the target issue belongs
  to, one combined gallery, deduplicated against the Series tab's set (an issue in both doesn't
  double-render).
- Empty state per tab ("No other issues in this series have a cover yet" / "...in a reading list...")
  when the candidate set is empty - tab still shown, not hidden, for a consistent 3-tab shape.

**Source resolution** (new small helper, `CoverThumbnailService` or a sibling): effective cover path
for issue X = `CustomCoverPaths.GetCachePath(X)` if it exists, else `CoverThumbnailPaths.GetCachePath(X)`
if it exists, else no cover (excluded from the gallery). Selecting a candidate calls the *same*
`TrySetCustomCover(targetIssueId, thatPath)` already used today - a real copy (re-encoded into the
target's own custom-cover slot), not a link; if the source issue's own cover later changes, the
copy already made is unaffected, matching `TrySetCustomCover`'s existing semantics exactly.

## Non-goals (v1)

- No support for Books (EPUB/PDF) cover picker from series/reading-list sources - scoped to comic
  `Issue`/`Series` per the actual ask ("the series and the reading list" are comic concepts here).
- No live-updating copied cover if the source issue's cover changes later (a copy, not a reference -
  consistent with existing `TrySetCustomCover` behavior, not a new limitation).
- No multi-select / bulk cover-from-series across many issues at once - one target issue per dialog
  open, matching today's one-at-a-time file-picker flow.

## Testing

- `PageExportServiceTests`: PNG/JPEG round-trip from a real decoded `Bitmap`, correct file extension,
  spread-vs-single-page input both accepted (it's just "a Bitmap in, a file out" - the reader supplies
  whichever it already renders).
- `ReaderPageContextMenuBuilderTests`: new entries present on the "not a thumbnail" fallback path,
  absent/unchanged on the thumbnail path (regression guard for the existing Page Type/Rotate/Spread
  submenus).
- `CoverPickerViewModelTests`: Series-tab candidate set excludes the target issue and any
  cover-less sibling; Reading-List-tab dedupes against Series-tab entries; selecting a candidate
  calls `TrySetCustomCover` with the resolved source path and closes the dialog.
- Existing `ChangeCoverAsync`/`ChangeIssueCoverAsync` test coverage (if any) stays green - Browse
  File tab must be byte-for-byte the same behavior as today.
- Manual: right-click the main reader page (not a thumbnail) in both paged and continuous-scroll
  comic modes, confirm "Save Page as PNG/JPEG" export the visible page/spread; open the cover picker
  from all three entry points, confirm Series/Reading List tabs populate and applying a pick updates
  the tile without a full reload (matching `DetailTabsViewModel`'s existing single-tile-swap
  optimization).
