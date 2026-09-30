# File Explorer cover thumbnails — Implementation Plan
*Implements: docs/superpowers/specs/2026-09-30-explorer-cover-thumbnails-design.md*

Toolchain note (2026-09-30): this PC has VS 2022 Community **without** the C++ workload or a Windows
10/11 SDK, so `PublishAot` can't link locally. The handler code is plain C# and builds/tests as a normal
library here; the Native AOT publish runs in `installer/BuildInstaller.ps1`, which CI runs on
`windows-latest` (has MSVC). Local AOT publish + the on-screen Explorer check need the user to add the
"Desktop development with C++" workload.

## Step 1: Handler project scaffold
**Files:** `src/Paperbunkr.ShellThumbnails/Paperbunkr.ShellThumbnails.csproj` (new; net10.0-windows,
`IsAotCompatible`, `AllowUnsafeBlocks`, SharpCompress, linked sources `Paperbunkr.Common/Text/ExtendedStringComparer.cs`,
`ExtendedStringComparison.cs`, `Paperbunkr.Engine/.../Mobi/MobiHeaderReader.cs`, `PalmDbReader.cs`), `Paperbunkr.sln` (add).
**Verify:** `dotnet build` of the project.

## Step 2: Cover finders (pure C#)
**Files:** `CoverImageRules.cs` (engine's image whitelist + `__MACOSX`/`.DS_Store` exclusion + natural sort, entry
names normalised to `\`), `ArchiveCoverFinder.cs`, `EpubCoverFinder.cs`, `MobiCoverFinder.cs`, `CoverFinder.cs` (dispatch by extension),
`DeadlineStream.cs` (read-only wrapper that throws once the 3 s deadline passes).
**Depends on:** 1. **Verify:** unit tests (Step 7).

## Step 3: Rendering
**Files:** `Interop/Wic.cs` (raw-vtable WIC: factory → decoder from memory stream → frame → scaler → format converter →
BGRA pixels), `Interop/Pdfium.cs` (`[LibraryImport("pdfium")]`, page 0 → BGRA at target size), `DjvuRenderer.cs`
(`Resources\ddjvu.exe -format=tiff -page=1 -size=…`, killed at deadline), `SkiaWebpFallback` only if SkiaSharp proves
AOT-safe, else skipped and noted, `Interop/Gdi.cs` (BGRA → `CreateDIBSection` HBITMAP), `ThumbnailRenderer.cs`.
**Depends on:** 2. **Verify:** scaler/PDF tests.

## Step 4: COM surface
**Files:** `Com/ThumbnailProvider.cs` (`[GeneratedComClass]` implementing `IInitializeWithStream`, `IThumbnailProvider`),
`Com/ClassFactory.cs`, `Com/Exports.cs` (`DllGetClassObject`, `DllCanUnloadNow` = S_FALSE), `Com/ComStream.cs` (IStream → Stream via vtable).
**Depends on:** 3. **Verify:** build; a test that `DllGetClassObject` hands back a factory that creates the provider (managed call).

## Step 5: App-side registration
**Files:** `src/Paperbunkr.App/Services/ThumbnailHandlerService.cs`, `IThumbnailRegistry.cs` + `WindowsThumbnailRegistry.cs`
(HKCU writes, previous-owner store under `HKCU\Software\Paperbunkr\ThumbnailHandlers`, AssocQueryString owner lookup, SHChangeNotify).
**Verify:** `ThumbnailHandlerServiceTests` against an in-memory registry fake.

## Step 6: Preferences UI + CLI + installer
**Files:** `Models/FileAssociationSummary.cs`, `Services/FileAssociationService.cs` (rows carry thumbnail state),
`ViewModels/PreferencesScreenViewModel.cs` (`ToggleThumbnailCommand`, deferred refresh), `Views/Preferences/AdvancedSection.axaml`
(FILE TYPES caption, column headers, Thumbnail toggle / "—", owner line), `Program.cs` (`--register-thumbnails`,
`--unregister-thumbnails`), `installer/Installer.iss` (task + run + uninstall), `installer/BuildInstaller.ps1` (AOT publish + copy).
**Depends on:** 5. **Verify:** VM tests; build; avalonia review checklist.

## Step 7: Tests
**Files:** `src/Paperbunkr.App.Tests/ShellThumbnails/*Tests.cs` (project reference with `Aliases="thumbs"` so linked
`cYo.Common.Text` types don't collide). Archive parity with `PageDecodeCore` page 0, EPUB cover/spine/text-only,
MOBI, PDF, corrupt/encrypted, deadline, scaler size.
**Verify:** targeted `dotnet test`.

## Step 8: Docs
`docs/paperbunkr-todo.md` entry; memory note. Manual Explorer check outstanding until the C++ workload is installed.
