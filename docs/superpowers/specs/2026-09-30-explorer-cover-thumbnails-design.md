# File Explorer cover thumbnails

Date: 2026-09-30. Status: design approved in session (grilling rounds 1-4); spec awaiting user review.

## Goal

Comic and book files show their cover as the Windows File Explorer thumbnail (also in file-open
dialogs), instead of a generic icon - for files anywhere on disk, whether or not they're in the library
and whether or not Paperbunkr is running. Modelled on CDisplayEx's Configure → Other → Thumbnails
checklist.

**Not a CE-parity feature.** ComicRack CE never shipped a thumbnail handler (no `IThumbnailProvider`,
`IExtractImage` or `e357fccd` anywhere in `_reference/ComicRackCE`). This is a deliberate addition.

## Decisions

| # | Question | Decision |
|---|----------|----------|
| 1 | What it covers | Comic and book files that contain an image, anywhere on disk. Never reads the library DB. |
| 2 | File types | `.cbz .cbr .cb7 .cbt .pdf .djvu .epub .mobi` (+ `.azw .azw3`, Q11). No plain `.zip/.rar/.7z/.tar`, no `.webp` (Windows already does it), no `.fb2`/`.cbw`. *(Implementation note: Paperbunkr doesn't ship DjVuLibre's `ddjvu.exe`, so `.djvu` is only offered when `Resources\ddjvu.exe` exists.)* |
| 3 | Default | Off. |
| 4/18/23 | Where | Preferences → Advanced, as a **"Thumbnail" column on the existing file-type rows** (layout B). Group caption renamed FILE ASSOCIATION → **FILE TYPES**, with "Open with" / "Thumbnail" column headers. Anchor `advanced.fileAssociation` unchanged. |
| 5 | Which image | Exactly the library's cover rule: page 0 of the provider = first image after Paperbunkr's image-extension filter and natural sort (`CoverThumbnailService.cs:96` → `PageDecodeCore.DecodeSinglePage(path, 0)`; filter `ComicProvider.cs:11-30,106-119`; sort `ExtendedStringComparer`). ComicInfo `FrontCover` is **not** used - the library doesn't use it either. |
| 6/22 | Another program owns a type | Row shows a second line "Thumbnail: \<program\>". Turning ours on takes over and remembers the previous handler; turning it off restores it. |
| 7 | Uninstall | Removes our registration and restores each previous handler. |
| 8 | CDisplayEx Magnifier size | Out of scope. |
| 9 | "Has an image" | Archives, EPUB, MOBI: cover image or no thumbnail (Windows default icon). PDF, DjVu: rendered page 1 always. |
| 10 | Book covers | EPUB: declared cover image (OPF), else first image in reading order. MOBI/AZW: EXTH cover record (`MobiHeaderReader.CoverRecordIndex`), else first image record. |
| 11 | Kindle | `.mobi .azw .azw3` as one "Kindle / MOBI" row, matching associations. |
| 12 | Grouping | Follows the existing rows (comic engine formats, then book formats); PDF appears once (comic side), as in associations. |
| 13/21 | Initial state | Every Thumbnail toggle starts off; turned on per row. The installer checkbox turns on every thumbnail type not owned by another program. No "turn all on" button. |
| 14 | Slow files | 3-second budget per request; on timeout, no thumbnail. |
| 15 | Protected/damaged | No thumbnail, never any UI. |
| 16 | Image codecs | WIC only. *(Changed in implementation, 2026-09-30: the planned SkiaSharp WebP fallback was dropped - SkiaSharp 3.119 is marked trimmable but not AOT-compatible, and libwebp isn't shipped. WebP/AVIF/HEIF/JXL covers show when Windows has that codec; current Windows normally has WebP.)* |
| 17 | Installer | One unticked task "Show comic and book covers as thumbnails in File Explorer". |
| 19 | Non-thumbnail rows | ZIP/RAR/RAR5/7z/TAR Archive, eComic (WebComic), FB2: Thumbnail column shows a faint "—". |
| 20 | `.cbr` on two rows | eComic (RAR) and eComic (RAR5) both show the toggle, bound to the same per-extension state. |

## Approach

Our own handler, compiled with .NET Native AOT (chosen over bundling SumatraPDF's GPL `PdfPreview.dll`,
whose cover rule differs and whose CLSID would collide with an installed SumatraPDF; and over
ArcThumb + our own PDF/DjVu, which means two handlers).

Research basis (2026-09-30):
- Thumbnail handlers using `IInitializeWithStream` run out-of-process by default in the
  "Thumbnail Cache Out-of-Proc Server" `dllhost.exe` - not in explorer.exe
  (learn.microsoft.com/windows/win32/shell/thumbnail-providers).
- Microsoft's "no managed in-proc extensions" guidance is about one CLR per process; a Native AOT DLL
  carries its own runtime. Shipping precedents: sql-bi/SQLBI-Whiteboard ThumbnailHandler (MIT),
  SharpAstro/tianwen (`docs/plans/explorer-thumbnails.md`), cnbluefire/ShellExtensions.
- No built-in COM under AOT → `[GeneratedComInterface]` + `StrategyBasedComWrappers`. AOT DLLs cannot
  unload → `DllCanUnloadNow` always returns `S_FALSE`.
- SharpCompress 0.48.0 is `IsAotCompatible`. PDFium is a C API → `[LibraryImport]` (PDFiumSharpV2 is
  netstandard2.0, not used here). Since .NET 10 an AOT DLL's `AppContext.BaseDirectory` is its own
  folder, so `pdfium.dll` next to it resolves.
- The handler **cannot reference `Paperbunkr.Engine`**: its default 7z path is `[ComImport]` COM
  (unsupported under AOT) and it pulls SqlClient/MySqlConnector.

## Design

### 1. `src/Paperbunkr.ShellThumbnails` (new, net10.0-windows, `PublishAot`, `NativeLib=Shared`)

- `ThumbnailProvider` - COM class implementing `IInitializeWithStream` + `IThumbnailProvider`;
  exported `DllGetClassObject` / `DllCanUnloadNow` via `[UnmanagedCallersOnly]`. `Initialize` only stores
  the `IStream` (reading it there would hydrate cloud placeholders). `ThreadingModel=Apartment`.
- `GetThumbnail(cx)` - wraps the `IStream` as a read-only seekable `Stream`, dispatches by extension to a
  cover finder, scales, returns a 32bpp `HBITMAP` (`WTSAT_ARGB`). Runs under a 3 s deadline; any
  exception, timeout, encryption or "no image" → `E_FAIL` (Windows shows its default icon).
- Cover finders (each returns encoded image bytes or a decoded bitmap):
  - `ArchiveCoverFinder` - `.cbz .cbr .cb7 .cbt` via SharpCompress. Uses the **same** image-extension
    whitelist, `__MACOSX`/`.DS_Store` exclusion and `ExtendedStringComparer` natural sort as the engine,
    compiled in as linked source files (no Engine reference), so the result equals library page 0.
  - `EpubCoverFinder` - OPF cover (`meta name="cover"` / `properties="cover-image"`), else first image
    in spine order.
  - `MobiCoverFinder` - linked `MobiHeaderReader` / `PalmDbReader` sources; cover record, else first
    image record. Covers `.mobi .azw .azw3`.
  - `PdfCoverRenderer` - PDFium page 0 via `[LibraryImport]` into the installed `pdfium.dll`.
  - `DjvuCoverRenderer` - runs the installed `Resources\ddjvu.exe` for page 1 to a temp file, killed at
    the deadline; temp file deleted.
- `ThumbnailScaler` - WIC decode → scale to fit `cx` → BGRA `HBITMAP`. WebP falls back to SkiaSharp.
- Memory: after a request that decoded a large image, trigger a GC (the known surrogate-bloat issue).
- Trimmed so the DLL stays a few MB; only OS imports + `pdfium.dll` at runtime.

### 2. App side (`Paperbunkr.App`)

- `ThumbnailHandlerService` (next to `FileAssociationService`, same test-seam shape - an injectable
  registry abstraction):
  - `ThumbnailExtensions` = `.cbz .cbr .cb7 .cbt .pdf .djvu .epub .mobi .azw .azw3`.
  - `IsEnabled(ext)`, `GetCurrentOwner(ext)` (effective handler via `AssocQueryString(ASSOCSTR_SHELLEXTENSION,
    "{e357fccd-…}")` → CLSID → `InprocServer32` DLL → file description / product name; null if none or ours),
    `SetEnabled(ext, bool)`.
  - Enable writes `HKCU\Software\Classes\CLSID\{ourClsid}\InprocServer32` (DLL path, `Apartment`) and
    `HKCU\Software\Classes\<ext>\ShellEx\{e357fccd-a995-4576-b01f-234630154e96}` = our CLSID; if the
    extension's default ProgID is **Paperbunkr's own** (active file association), also under that ProgID's
    `ShellEx`. Before overwriting, the previous value at each location is saved under
    `HKCU\Software\Paperbunkr\ThumbnailHandlers\<ext>`.
  - Disable/uninstall restores saved values (or deletes our key if there was none), removes the CLSID
    key when no extension uses it, then `SHChangeNotify(SHCNE_ASSOCCHANGED)`.
  - **Open risk, verify on a real machine:** an extension whose ProgID belongs to another app that has
    its own ShellEx handler may shadow `.ext\ShellEx` (ProgID looked up first). If confirmed, the fix is
    decided then (write to that ProgID in HKCU vs. show "can't take over" for that row) - not guessed now.
- `FileAssociationSummary` gains `ThumbnailExtensions` (subset of the row's extensions that are thumbnail
  types; empty → "—"), `IsThumbnailEnabled`, `ThumbnailOwner`. `.cbr` state is per extension, so the RAR and
  RAR5 rows reflect the same value.
- `PreferencesScreenViewModel`: `ToggleThumbnailCommand(row)` → service → reload rows (deferred refresh
  per CLAUDE.md's routed-event rule, since the toggle lives in the row).
- `Program.cs`: `--register-thumbnails [exts]` (none given = every thumbnail type with no other owner, Q13)
  and `--unregister-thumbnails`, beside the association switches.

### 3. View (`Views/Preferences/AdvancedSection.axaml`)

- Caption FILE ASSOCIATION → FILE TYPES; a header row "Open with" / "Thumbnail" aligned to the toggle
  columns.
- Row template: `Grid ColumnDefinitions="Auto,*,Auto,Auto"`; the existing Open-with `ToggleSwitch` stays,
  a Thumbnail `ToggleSwitch` (or faint "—") is added. Owner line "Thumbnail: \<name\>" under the extension
  list when `ThumbnailOwner` is set. Theme resources only.
- Description text notes that thumbnails Windows already cached refresh when a file changes, or at once
  via Disk Cleanup → Thumbnails.

### 4. Build and installer

- `BuildInstaller.ps1`: `dotnet publish src/Paperbunkr.ShellThumbnails -r win-x64 -c Release`
  (Native AOT) and copy `Paperbunkr.ShellThumbnails.dll` into the app output.
- `installer/Installer.iss`: task `explorerthumbnails` (unchecked) → `[Run]` `--register-thumbnails`;
  `[UninstallRun]` `--unregister-thumbnails` runs before files are removed. Same per-user-HKCU caveat as
  associations (the installer is per-machine `PrivilegesRequired=admin`; registration lands in the
  installing user's HKCU; other Windows users turn it on in their own Preferences).

## Testing

- Cover finders (unit, in a test project that references the handler's plain C# code, not the AOT
  output): archives via existing `CbzFixture` + a cbr/cb7/cbt fixture; **parity test** that the archive
  finder picks the same entry as `PageDecodeCore` page 0; EPUB with/without declared cover and text-only;
  MOBI via `MobiFixture`; PDF page 0; DjVu page 1; encrypted and corrupt files → no image; deadline.
- `ThumbnailScaler`: output bitmap fits the requested size, alpha preserved.
- `ThumbnailHandlerService` against a temp registry root: enable/disable, previous-owner save/restore,
  CLSID cleanup, owner name resolution, Paperbunkr-ProgID case.
- VM: `.cbr` rows linked, "—" rows not toggleable, owner line.
- Manual, on a real machine (with the user's OK): Explorer folder of every type; PDF owned by another
  app; the ProgID-shadowing risk above; uninstall restores the previous owner; dllhost memory after a
  large folder.

## Out of scope
Magnifier size; FB2, `.cbw`, plain archives, standalone images; preview-pane (`IPreviewHandler`)
support; machine-wide (HKLM) registration.
