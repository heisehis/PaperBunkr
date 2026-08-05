# Retarget Spike Report

Ran the retarget spike described in `docs/onboarding.md` §4 and `docs/open_items_resolved.md`
on a machine with .NET 8/9 SDKs installed (confirmed via `dotnet --list-sdks`). Result: **the
solution builds clean (0 errors) on net8.0** after a WinForms sweep, several genuinely-portable
files ported out of the excluded WinForms projects, and a documented set of exclusions for real
coupling that isn't a mechanical strip.

## What was built

- `Paperbunkr.sln` at the repo root, referencing:
  - `src/Paperbunkr.Common/Paperbunkr.Common.csproj` — net8.0 class library, ported from
    `_reference/ComicRackCE/cYo.Common` (excludes `cYo.Common.Windows`/`cYo.Common.Presentation`,
    the separate WinForms-only projects).
  - `src/Paperbunkr.Engine/Paperbunkr.Engine.csproj` — net8.0 class library, ported from
    `_reference/ComicRackCE/ComicRack.Engine` (excludes `Controls/` and
    `ComicRack.Engine.Display.Forms`).
- `dotnet build Paperbunkr.sln` succeeds: **0 errors, ~330 warnings** (mostly `CA1416` platform-
  compatibility notices on `System.Drawing` APIs — see the System.Drawing verdict below).

### File counts

| | Ported (.cs files on disk) | Reference docs' claim |
|---|---|---|
| `Paperbunkr.Common` (from `cYo.Common`) | 322 | ~326 |
| `Paperbunkr.Engine` (from `ComicRack.Engine`, excl. `Controls/`) | 584 | ~577 (excl. `Controls/`'s 10) |

Counts differ slightly from the docs' upfront estimate because a handful of files were pulled in
from the *excluded* WinForms projects after turning out to be portable (see below), and a
handful were dropped entirely (see "Files removed outright"). Net effect is a wash.

## WinForms sweep: every reference found and how it was handled

Initial sweep found **31 files** with `using System.Windows.Forms;` (25 in `cYo.Common`, 6 in
`ComicRack.Engine` after excluding `Controls/`) — close to the docs' claim of 27/326 and 15/81
(the Engine number differs because the docs' 81 apparently undercounted the full excl.-`Controls/`
tree, which is actually 577 files, not 81; regardless, the *reference rate* was low as predicted).

**Trivial cases (as the docs predicted), fixed as ported:**
- `ComicBook.cs`: `Clipboard.SetDataObject()` in `ToClipboard()` — body replaced with a
  `TODO(Paperbunkr)` comment describing the original behavior; needs an Avalonia
  `IClipboard`-based implementation at the UI layer eventually.
- `BackupArchiveCreator.cs`, `PdfStorageProvider.cs`, `Diagnostic.cs`, `IniFile.cs`,
  `UserCredentialsDialog.cs`: `Application.CompanyName`/`ProductName` replaced with a new
  `cYo.Common.Runtime.ApplicationInfo` static class (`src/Paperbunkr.Common/Runtime/ApplicationInfo.cs`)
  holding the same literal values ComicRack CE's assembly attributes carried.
- ~15 files had a simply-unused `using System.Windows.Forms;` (or `using cYo.Common.Windows.Forms;`)
  line with no actual WinForms type used — removed outright (`GitVersion.cs`, `HttpAccess.cs`,
  `FunctionFactory.cs`, `IComicDisplayConfig.cs` partially, `ComicBookVirtualTagComparer.cs`,
  `ComicBookMetadata*.cs` partially, `ComicDatabase.cs` partially, `StacksConfig.cs`/
  `DisplayListConfig.cs` partially, `XmlInfoProviderFactory.cs`, and others).

**Small portable-type substitutions** (a WinForms type used as plain data, swapped for a
Paperbunkr-owned equivalent with the same shape):
- `RectangleExtensions.cs`: `System.Windows.Forms.Padding` → new `cYo.Common.Drawing.Padding`
  struct (four ints + `Horizontal`/`Vertical`, nothing UI-specific about the original use).
- `BackgroundRunner.cs`: `MethodInvoker` delegate → `Action`.
- `GestureEventArgs.cs`: `MouseButtons` enum → new `cYo.Projects.ComicRack.Engine.Display.MouseButton`
  flags enum with matching values.
- `IComicDisplayConfig.cs`: `ImageLayout` enum → new local `ImageLayout` enum (None/Tile/Center/
  Stretch/Zoom, matching WinForms' values).
- `Win32/FileOperations/*.cs` (6 files) + `ShellFile.cs`: `IWin32Window` (a one-member interface,
  just `IntPtr Handle`) → new `cYo.Common.Win32.FileOperations.IWin32Window` interface. These
  wrap the real Shell "delete to Recycle Bin with progress" API (`IFileOperation`/`SHFileOperation`)
  and are actually used by Engine (`ShellFile.DeleteFile`), so this wasn't a stub — the WinForms
  dependency really was just an owner-window handle.
- `UserCredentialsDialog.cs`: dropped `: CommonDialog` base class and `DialogResult` return type
  in favor of a plain class with `bool ShowDialog(IntPtr hwndOwner = default)`; the underlying
  logic is a native `CredUIPromptForCredentials` call keyed off a raw HWND, nothing WinForms-
  specific. `HttpAccess.cs` (its one caller) updated accordingly.
- `ComicPageInfo.cs`, `MetronInfoProvider.cs`: `LocalizeUtility.LocalizeEnum(...)` — the original
  `LocalizeUtility` class is a genuinely deep WinForms `Control`/`ToolStrip`/`ListView`/
  `DataGridView` tree-walking localizer, but `LocalizeEnum` (the only member Engine actually
  calls) has zero WinForms dependency. Ported that one method alone into a new
  `cYo.Common.Windows.LocalizeUtility` in `Paperbunkr.Common`, rather than the whole class.
- `ComicDatabase.cs`: `AutomaticProgressDialog.Process(...)` (a WinForms modal progress dialog
  used during `FinalizeLoading()`'s consistency check) — replaced with a headless stand-in
  (`src/Paperbunkr.Engine/Windows/Forms/AutomaticProgressDialog.cs`) that just runs the work
  synchronously with no UI and no cancel support, clearly marked `TODO(Paperbunkr)` for a real
  Avalonia-backed progress UI later.

**Files removed outright** (dead WinForms-only helpers in `cYo.Common`, confirmed to have zero
callers anywhere in either ported tree before removal): `Win32/TrueVisibility.cs`,
`Win32/VariableHeightTreeNode.cs`, `Win32/Keyboard.cs`, `Win32/IdleProcess.cs`,
`Win32/BitmapCursor.cs`, `Win32/IBitmapCursor.cs`, `Win32/DataObjectEx.cs`,
`Drawing/ExtendedColors/ProfessionalColorTableEx.cs` (+ its `.KnownColors.cs` partial),
`Drawing/Thumb{Renderer,TileRenderer,IconRenderer}.cs` (see "real open problems" below for why
the Thumb renderers specifically were dropped rather than fixed).

## Genuinely portable code found *inside* the excluded WinForms projects

The sweep surfaced a real, useful finding beyond what the docs anticipated: a handful of types
that live in `cYo.Common.Windows`/`cYo.Common.Windows.Forms`/`cYo.Common.Presentation` (all
excluded per the porting brief) turned out to have **no actual WinForms dependency** once
inspected — they were just organizationally placed in the wrong project. Ported these in (with
attribution comments pointing back at the original file) rather than either excluding their
consumers or leaving core Engine code broken:

- `RendererImage.cs` / `RendererGdiImage.cs` (from `cYo.Common.Presentation`) — a pure
  `System.Drawing.Bitmap` wrapper with weak-reference semantics, needed by
  `IO/MemoryOptimizedImage.cs`, the shared base of `ThumbnailImage`/`PageImage` (core image-cache
  memory accounting). Ported to `Paperbunkr.Engine/Presentation/`.
- `ItemViewConfig.cs`, `ItemViewColumnInfo.cs`, `ItemViewMode.cs` (from
  `cYo.Common.Windows.Forms`) — plain serializable saved-view config records (column widths,
  sort order, thumbnail size), needed by `Database/DisplayListConfig.cs` and
  `Database/StacksConfig.cs` (core per-list/per-stack display settings, not UI code). The only
  WinForms type inside them was `SortOrder` (a 3-value enum) and a constructor overload that
  snapshotted a live `IColumn` — the enum was reimplemented locally
  (`cYo.Common.Windows.Forms.SortOrder`) and the `IColumn` constructor was dropped (that's UI
  glue, not persistence). Ported to `Paperbunkr.Engine/Windows/Forms/`.

## Real open problems (categorized, not papered over)

Per the spike's instructions, these were **excluded via `<Compile Remove>`** rather than hacked
around, because fixing them means either rewriting real logic or adopting a different technology
— both out of scope for a mechanical port:

1. **`Display/ComicDisplay.cs`** (Engine) — the concrete `IComicDisplay` implementation is a
   `DisposableObject` wrapping a live `ContainerControl` and pulling from
   `cYo.Common.Windows.Forms` (`KeySearch`, etc.) throughout. This is the actual legacy reader
   canvas; it's already the plan (docs §8) to rebuild this natively against Avalonia, so excluding
   it here just confirms that plan rather than contradicting it. The `IComicDisplay`/
   `IComicDisplayConfig` *interfaces* remain and compile fine.

2. **`Drawing/ViewItemRenderer.cs`, `Drawing/Thumb{Renderer,TileRenderer,IconRenderer}.cs`,
   `Metadata/ComicBook/ComicBookMetadata{,Collection,Manager}.cs`** (Engine) — genuinely need
   `IViewableItem`/`IColumn` from the excluded `cYo.Common.Windows.Forms`. `IColumn` in particular
   has a `DrawHeader(Graphics gr, Rectangle rc, HeaderState style)` GDI+ method — this is real
   WinForms ListView-column rendering/sorting-and-grouping-key infrastructure, not core domain
   data, correctly excluded from the WinForms project it lives in.

3. **WCF self-hosting has no net8 equivalent without adopting CoreWCF** (a different hosting
   model, ASP.NET-Core-based) — unrelated to WinForms, a separate portability gap found during
   this pass:
   - `Common/Runtime/{SingleInstance,ISingleInstance}.cs` — single-instance-app IPC over a WCF
     net.pipe `ServiceHost`.
   - `Engine/IO/Network/*.cs` (`ComicLibraryServer.cs`, `ComicLibraryClient.cs`,
     `IRemoteComicLibrary.cs`, `IRemoteServerInfo.cs`, `ServerRegistration.cs`,
     `RemoteComicBookProvider.cs`) and `Engine/NetworkManager.cs` — the "remote library" client/
     server feature, entirely WCF `ServiceContract`/`ServiceHost` based.

4. **`Sync/` + `QueueManager.cs`** (Engine, 13 files) — the portable-device and wireless-sync
   subsystem. Excluded per docs/onboarding.md §15, which already scoped WiFi-sync as "out of
   scope entirely, the feature doesn't exist in Paperbunkr." `WirelessSyncProvider.cs` also has
   its own WCF dependency on top of being out of scope. One exception: `Sync/ExtraSyncInformation.cs`
   turned out to be a plain POCO that `ComicBook.cs` (core domain model) stores a reference to —
   kept in via an explicit `<Compile Include>` override.

None of #2–#4 are WinForms issues in the "strip a stray using" sense the docs anticipated; they're
either genuine UI-rendering coupling or a genuine WCF-hosting portability gap. Flagging both
categories explicitly rather than force-fitting them into the "trivial WinForms strip" narrative.

## The System.Drawing / GDI+ verdict

**Evidence, not a guess:** 46 files in `Paperbunkr.Common` and 52 files in `Paperbunkr.Engine`
(98 total, out of 906 ported files) have a `using System.Drawing` (or `System.Drawing.*`)
directive. The build produced **~330 `CA1416` warnings** — .NET's platform-compatibility analyzer
flagging specific `System.Drawing`/GDI+ call sites (`Bitmap` construction, `LockBits`/`UnlockBits`,
`Graphics`, pixel format enums, etc.) as "supported on windows only," concentrated in the
image-codec providers (`JpegXLImage.cs` alone accounts for dozens) and drawing/rendering code
(`PageRendering.cs`, `RatingRenderer.cs`, the archive/PDF/image provider tree).

**Verdict: shallow enough to keep Windows-only for now, but not confined to a couple of files —
plan for the SkiaSharp/ImageSharp migration as a real future project, not an afterthought.**
Reasoning:
- It built clean with zero errors against `System.Drawing.Common` 8.0.10 on net8.0 — the "keep it
  Windows-only for now" path costs nothing today, matching the docs' framing.
- But 98/906 files (~11%) touching `System.Drawing`, concentrated in exactly the
  image-codec/rendering code that's explicitly called out (docs §3) as "the single biggest
  scope-saver in the codebase" (the format/provider architecture), means a future SkiaSharp/
  ImageSharp migration isn't a small, isolated change — it touches the same files that make the
  format-provider architecture worth preserving in the first place. It's tractable (every hit so
  far is ordinary bitmap-buffer manipulation, not anything GDI+-exotic like `Graphics.DrawString`
  layout or `Region`/`GraphicsPath` hit-testing that would be hard to port 1:1), but it's a
  cross-cutting rewrite across the codec layer, not a couple of isolated fixes.
- This reinforces, rather than overturns, the docs' framing: do it later, deliberately, as its own
  pass — but budget it as touching ~50-100 files in Engine when that pass happens, not "a handful."

## Status of the five P/Invoke shims

All five are **written fresh** per docs/onboarding.md §2/§4 (not ported verbatim), using a new
shared `NativeLibrary.SetDllImportResolver`-based helper
(`src/Paperbunkr.Engine/IO/Provider/Native/NativeInterop.cs`) that replaces each library's
hardcoded-path/extension resolution with an arch-subfolder search (`x64`/`x86`/`arm64`, then
`runtimes/{rid}/native/`, then bare-name fallback). All build clean.

| Shim | File | What it exposes | Notes |
|---|---|---|---|
| `libwebp` | `IO/Provider/WebpImage.cs` | Decode (`WebPGetInfo`, `WebPDecodeBGRAInto`) + encode (lossless/lossy BGR/BGRA) | CE hand-picked 32/64-bit entry points at call time from two hardcoded `DllImport` strings (`Resources\x86\libwebp.dll`/`Resources\x64\libwebp.dll`); collapsed to one bare `"libwebp"` import + resolver. |
| `jxl` | `IO/Provider/JpegXLImage.cs` | Full decoder/encoder API (JXL container + codestream, JPEG-recompression path) | CE baked the `.dll` extension into a `const string`; now bare `"jxl"` + resolver. Largest of the five (~780 lines) — the encode/decode orchestration logic itself was kept mechanically (it's ordinary buffer-copying/format-conversion code operating on the native API, not itself native-interop shim code) per the spike's "don't rewrite logic" instruction; only the P/Invoke declaration layer was rewritten. |
| `libheif` | `IO/Provider/HeifAvifImage.cs` | One raw P/Invoke (`heif_check_filetype`); the actual decode/encode goes through the `LibHeifSharp` NuGet package (already CE's real interop layer, kept as-is — not something to "rewrite fresh," it's a separately-licensed third-party wrapper, not CE's own shim code) | CE's raw import was already the portable bare-name pattern; this mainly adds explicit resolver registration for consistency with the other four. |
| `7z` | `Common/Compression/SevenZip/SevenZipFactory.cs` + `Engine/IO/Provider/Readers/Archive/SevenZipEngine.cs` | Dynamic `LoadLibrary`/`GetProcAddress`/`CreateObject` binding (COM-style, not static `DllImport`) | CE's real bug was in `SevenZipEngine.cs`: `PackDll32`/`PackDll64` were built from `Assembly.GetExecutingAssembly().Location + "Resources\x86\7z.dll"` etc. Replaced with `NativeInterop.ResolveNativeAssetPath(...)`, same arch-subfolder search as the DllImport resolver, returning a path for `LoadLibrary` to consume. `SevenZipFactory.cs` itself (the `LoadLibrary`/`GetProcAddress` layer) needed no changes — it already took an explicit path parameter. |
| `pdfium` | New: `IO/Provider/Native/PdfiumNative.cs` | `FPDF_InitLibrary`, `FPDF_LoadDocument`, `FPDF_LoadPage`, `FPDF_RenderPageBitmap`, `FPDFBitmap_*`, etc. — the core open/render/close surface | **Important scoping note:** CE never actually hand-rolled a raw P/Invoke layer for pdfium at all — `Pdfium.cs`/`PdfiumReaderEngine.cs` depend entirely on the third-party `PDFiumSharpV2`/`bblanchon.PDFium.Win32` NuGet packages (whose native binary was bundled via a post-build `xcopy`, the "Highest risk" mechanism flagged in the docs' table). This file is a standalone fresh shim against PDFium's public C API, satisfying the "rewrite fresh" requirement on its own terms, but it is **not currently wired into** `Pdfium.cs` (which keeps using PDFiumSharpV2 unchanged for this spike, since swapping the actual call sites is a larger, riskier change than the spike's budget allows). Follow-up: either wire `PdfiumNative` in to fully retire the PDFiumSharpV2 dependency, or accept PDFiumSharpV2 as the long-term interop layer and treat `PdfiumNative.cs` as documentation/a fallback. |

Also fixed one adjacent native-interop gap discovered during the build (not one of the five, but
in the same family): CSJ2K (JPEG2000 decoder, via the `CSJ2K` NuGet package) ships a
`System.Drawing`-based `BitmapImageCreator` only in its .NET Framework build target — the
`netstandard1.3` build net8.0 actually resolves to omits it. Wrote a fresh
`IO/Provider/BitmapImageCreator.cs` implementing CSJ2K's public `IImageCreator`/`IImage`
interfaces against `System.Drawing.Bitmap` so `Jpeg2000Image.cs`'s existing call sites
(`J2kImage.FromBytes(data).As<Bitmap>()`) keep working. This is a best-effort reimplementation
(pixel-format/stride handling assumes 24bpp RGB source data) rather than a verified pixel-perfect
port — flagging as a spot worth a closer look/test pass before relying on it for real decoding.

## Commit

Committed to the Paperbunkr repo (`Paperbunkr.sln`, `src/Paperbunkr.Common/`,
`src/Paperbunkr.Engine/`, this report) — 930 files changed, 77972 insertions.
