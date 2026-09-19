# Reader "Save Page As" + Cover Picker — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-17-reader-save-page-and-cover-picker-design.md*

Scope note found during survey: `PdfPageReaderScreenViewModel` (Books/PDF reader) has **no**
`IContextMenuProvider` wiring at all today (unlike `ReaderScreenViewModel`, which already has
`ReaderPageContextMenuBuilder`). Building that infra from scratch for Books is a materially bigger
lift than "add two menu items" and wasn't what triggered this ask. **v1 scope: comic/manga reader
only** for Feature A; Books/PDF reader is a natural fast-follow once it has its own context-menu
provider, not blocking this work.

## Step 1: `PageExportService`
**Files:** `src/Paperbunkr.App/Services/PageExportService.cs` (new), `src/Paperbunkr.App.Tests/PageExportServiceTests.cs` (new)
**What:** `TryExport(Bitmap page, string destPath, PageExportFormat format)` where `PageExportFormat` is `{ Png, Jpeg }`. PNG via `page.Save(destPath)`; JPEG via `page.Save(destPath, new JpegBitmapEncoderOptions { Quality = 85 })` (same options shape `CoverThumbnailService` already uses).
**Depends on:** none
**Verify:** new tests - PNG/JPEG round-trip produces a valid, readably-decodable file; wrong extension doesn't leak in (service controls the encoder, not the filename).

## Step 2: Reader context-menu entries
**Files:** `src/Paperbunkr.App/ViewModels/ReaderPageContextMenuBuilder.cs` (edit), `src/Paperbunkr.App/ViewModels/ReaderScreenViewModel.cs` (edit), `src/Paperbunkr.App.Tests/ReaderPageContextMenuBuilderTests.cs` (edit or new)
**What:**
- `ReaderScreenViewModel`: two new `[RelayCommand]` methods, `SavePageAsPngAsync`/`SavePageAsJpegAsync`. Each: resolve current view's `Bitmap` (whatever `CurrentPage`/spread-composite property already backs the on-screen `Image` - confirm exact property name at implementation time), build filename `"{Series.Name} - Page {N+1}"` (1-based, matching CE), call `_filePicker.PickSaveFileAsync(title, filename, "png"|"jpg", label)`, then `PageExportService.TryExport(bitmap, path, format)`.
- `ReaderPageContextMenuBuilder.Build(target)`: the `_ => null` fallback becomes `_ => BuildForMainPage()` returning the two new entries (`ContextMenuEntry.Item("Save Page as PNG…", _vm.SavePageAsPngAsyncCommand)`, same for JPEG). Thumbnail branch (`BuildForThumbnail`) untouched.
**Depends on:** Step 1
**Verify:** `Build(someOtherObject)`/`Build(null)` now returns the 2 export entries instead of `null`; `Build(readerThumbnailSample)` still returns only Page Type/Rotate/Spread (regression guard). Manual: right-click main page in paged + continuous-scroll modes, confirm file saves and opens as a valid image.

## Step 3: Keybinding
**Files:** wherever `NavigationKeyBindings`/`DisplayKeyBindings` groups are defined (confirm exact enum/registry at implementation time - `KeyBindingService`-adjacent)
**What:** register "Save Page as PNG" as a bindable command, no forced default binding (avoid collision).
**Depends on:** Step 2
**Verify:** appears in Preferences → Keyboard Shortcuts list, remappable, round-trips through export/import.

## Step 4: Cover-source resolution helper
**Files:** `src/Paperbunkr.App/Services/CoverThumbnailService.cs` (edit) or a new sibling in `Services/Covers/`
**What:** `string? GetEffectiveCoverPath(int issueId)` = `CustomCoverPaths.GetCachePath` if `HasCustomCover`, else `CoverThumbnailPaths.GetCachePath` if it exists, else `null`.
**Depends on:** none
**Verify:** unit test covering all 3 branches (custom wins, generated fallback, neither → null).

## Step 5: `CoverPickerDialog` + `CoverPickerViewModel`
**Files:** `src/Paperbunkr.App/ViewModels/CoverPickerViewModel.cs` (new), `src/Paperbunkr.App/Views/CoverPickerDialog.axaml` + `.axaml.cs` (new - remember the new-View build gotcha: add both in the same step), `src/Paperbunkr.App.Tests/CoverPickerViewModelTests.cs` (new)
**What:** 3-tab dialog per the design doc. Constructor takes `targetIssueId`, `seriesId`, resolves Series-tab candidates (`context.Issues.Where(i => i.SeriesId == seriesId && i.Id != targetIssueId)`, filtered to `GetEffectiveCoverPath(i.Id) is not null`) and Reading-List-tab candidates (every issue sharing a `ReadingListItem`'s list with the target issue, same filter, deduped against Series-tab ids). Browse File tab reuses `IFilePickerService.PickImageFileAsync` unchanged. Selecting any candidate calls `CoverThumbnailService.TrySetCustomCover(targetIssueId, path)` and raises a result event/callback the caller uses to refresh, then closes.
**Depends on:** Step 4
**Verify:** new tests per the design doc's Testing section (exclusion, dedup, apply-and-close).

## Step 6: Wire the 3 entry points
**Files:** `src/Paperbunkr.App/ViewModels/DetailScreenViewModel.cs`, `src/Paperbunkr.App/ViewModels/MangaDetailScreenViewModel.cs`, `src/Paperbunkr.App/ViewModels/DetailTabsViewModel.cs` (all edit)
**What:** `ChangeCoverAsync`/`ChangeIssueCoverAsync` open `CoverPickerDialog` instead of calling `PickImageFileAsync` directly; on a result, same post-apply refresh each already does today (`ReloadCurrentSeries()` / single-tile swap in `DetailTabsViewModel`).
**Depends on:** Step 5
**Verify:** existing cover-change tests (if any) still pass for the Browse File path; manual click-through of all 3 entry points confirms Series/Reading List tabs populate and apply correctly.

## Step 7: Regression pass
**Files:** none
**What:** run touched App.Tests subsets (`ReaderPageContextMenuBuilderTests`, `ReaderScreenViewModelTests`, `PageExportServiceTests`, `DetailScreenViewModelTests`, `DetailTabsViewModelTests`, `MangaDetailScreenViewModelTests`, `CoverPickerViewModelTests`, `KeyBindingServiceTests`).
**Depends on:** Steps 1-6
**Verify:** all green; forced clean rebuild confirms `CoverPickerDialog.axaml`'s XAML actually wove (per this project's own new-View build gotcha).
