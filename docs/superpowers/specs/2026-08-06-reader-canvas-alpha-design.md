# Reader Canvas (Alpha scope) — Design Spec

*Date: 2026-08-06. Scope: build the real page-rendering pipeline for the Reader screen (`ReaderScreenViewModel`/`ReaderScreen.axaml`), which currently shows real issue/page metadata but the center canvas is a static placeholder tile — no archive is ever opened, no image is ever decoded. This is the Alpha release bar per docs/onboarding.md §16: "one reading mode working end-to-end — paged LeftToRight, the simplest case — proving the decode/dispose/virtualization pipeline actually works," explicitly narrower than the full continuous/webtoon architecture in §8, which stays deferred to Beta.*

**Verification practice for this feature (per the standing CE-verification rule):** the exact page-opening API this design builds on was mapped by direct source investigation of `src/Paperbunkr.Engine` before any of this was designed — not assumed. Findings cited inline. This also surfaced a real, previously-unknown deployment gap (§1) that shaped the design, not just confirmed assumptions.

## 1. Native binary bundling

CBZ and CBR both default to `EngineConfiguration.CbEngines.SevenZip` (confirmed: `EngineConfiguration.cs:670`, `CbzComicProvider.cs`, `CbrComicProvider.cs`/`Rar5ComicProvider.cs` all `default:` to `SevenZipEngine`), which loads native `7z.dll` via `LoadLibrary`/`GetProcAddress`. **Confirmed during design that no `7z.dll` binary is actually bundled anywhere in Paperbunkr's build output** — only copy on disk is inside the read-only `_reference/ComicRackCE` clone. Opening a real CBZ/CBR today would fail at runtime with the current config.

Fix: copy `7z.dll` (x64) from `_reference/ComicRackCE/ComicRack/Output/Resources/x64/7z.dll` into `src/Paperbunkr.App/x64/7z.dll`, added via:

```xml
<ItemGroup>
  <Content Include="x64\7z.dll" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

This lands where `NativeInterop.ResolveNativeAssetPath` (`src/Paperbunkr.Engine/IO/Provider/Native/NativeInterop.cs`) already searches first (`{baseDir}/x64/7z.dll`) — no resolver changes needed. This is the redistributable 7-Zip library itself (LGPL), not ComicRackCE's own code, so bundling it doesn't touch the provenance concerns in onboarding.md §2. Unlocks both CBZ and CBR simultaneously, one binary.

## 2. Page decode service

New `IPageImageDecoder`/`PageImageDecoder` in `Paperbunkr.App/Services` (bridges Engine types to Avalonia's `Bitmap` — not pure data-layer, so it lives in App, not Data):

```csharp
public interface IPageImageDecoder : IDisposable
{
    int PageCount { get; }
    Bitmap GetPage(int pageIndex); // 0-based
}
```

Built on the confirmed-working, already-implemented engine path: `ComicBook.OpenProvider()` (`ComicBook.cs:1865`) → `ImageProvider` (caller-disposed) → `GetByteImage(index)` (`ImageProvider.cs:128`, raw bytes, synchronous) or `GetImage(index)` (`ImageProvider.cs:120`, `System.Drawing.Bitmap`, synchronous). No `NotImplementedException`, no `TODO(Paperbunkr)`, no WinForms-excluded code anywhere in this path (confirmed by tracing `ComicBook` → `ArchiveComicProvider` → `CbzComicProvider`/`SevenZipEngine` → `GetByteImage`).

`GetPage(index)`:
1. Try `imageProvider.GetByteImage(index)` → `new Avalonia.Media.Imaging.Bitmap(new MemoryStream(bytes))` directly. Works for standard JPEG/PNG pages — the overwhelming majority of real comic files — with **no `System.Drawing` involved at all**, since Avalonia decodes these formats natively via Skia.
2. On failure (exotic format the engine's own codec providers handle specially — WebP/HEIF/JPEG2000/JPEGXL), fall back to `imageProvider.GetImage(index)` (`System.Drawing.Bitmap`) → re-encode to a PNG `MemoryStream` → load into an Avalonia `Bitmap`.

One `ImageProvider` opened per issue-viewing session (on `LoadIssue`, not per page-turn — `ArchiveComicProvider` caches the parsed entry list per file in a static cache, confirmed at `ArchiveComicProvider.cs:17,81-91`, so nothing is lost by keeping it open across page turns within the same issue). Disposed when a different issue loads or the Reader screen's issue changes.

## 3. Real PageCount reconciliation

`ComicBook.RefreshInfoFromFile` (`ComicBook.cs:2439-2531`, already implemented) sets `PageCount` from the opened `ImageProvider.Count` (line 2522). When `ReaderScreenViewModel.LoadIssue` opens an issue with a real `FilePath`, call this and — if the real count differs from the stored `Issue.PageCount` — update and persist it. Self-healing metadata, not a separate library-scan feature. Issues with no `FilePath` (placeholders, seed data) keep showing their stored/placeholder state unchanged, exactly as today.

## 4. Rendering

Confirmed Avalonia 12.1.1 API (`ref/net8.0/Avalonia.Base.xml`) before designing against it:
- `ICustomDrawOperation` (`Avalonia.Rendering.SceneGraph`): `Bounds` (property), `HitTest(Point)`, `Render(ImmediateDrawingContext)`, plus `IDisposable`/`IEquatable<ICustomDrawOperation>`.
- `ImmediateDrawingContext.DrawBitmap(Bitmap, Rect sourceRect, Rect destRect)` takes the public `Bitmap` type directly — no low-level `IBitmapImpl`/Skia-surface juggling needed.
- `DrawingContext.Custom(ICustomDrawOperation)` is how a `Control.Render` override pushes one.

New `PageCanvas : Control` (Views) holds the current page's `Bitmap?`. Its `Render(DrawingContext context)` override pushes a `ReaderPageDrawOperation : ICustomDrawOperation` via `context.Custom(...)`. That operation's `Render(ImmediateDrawingContext ctx)` computes a letterboxed (uniform-fit, centered) destination `Rect` from the control's `Bounds` and the bitmap's `PixelSize`, then calls `ctx.DrawBitmap(bitmap, sourceRect, destRect)`. `Bounds` = the control's bounds; `HitTest` always returns true (the whole canvas is a page-turn click target, §6).

Matches onboarding.md §8's mechanism choice for paged mode (`ICustomDrawOperation`, distinct from continuous/webtoon's `CompositionCustomVisualHandler`) from the start, per explicit call — not the simpler plain-`Image`-control shortcut that would also have visually worked for Alpha's bar alone.

## 5. Bitmap cache & disposal (paged virtualization)

Paged mode only ever shows one page at a time — no wide scroll buffer needed the way continuous/webtoon will (§8's "start ±2, tune later" is a continuous-mode concern). `PageImageDecoder` keeps at most 3 decoded `Bitmap`s alive at once (previous/current/next page, keyed by page index in a small dictionary); any bitmap that falls outside that window after a navigation gets `Dispose()`d immediately. Same explicit-disposal principle §8 mandates for the harder continuous case, scaled to what paged mode actually needs.

## 6. Navigation

- Wire the existing ◀/▶ scrubber arrows (`ReaderScreen.axaml`, currently dead) to `PreviousPage`/`NextPage` commands.
- `PageCanvas` gets a `PointerPressed` handler: click on the left half of the canvas → previous page, right half → next page (consistent with `HitTest` always being true).
- Left/Right arrow keys, same commands.
- Each navigation updates `Issue.LastPageRead` and persists it via EF Core — the same field `DetailScreenViewModel`'s "Continue" logic already reads (`DetailScreenViewModel.cs`) — throttled to once per actual page change, not per keystroke-repeat event.
- Thumbnail rail selection (`Thumbnails`, currently `ReaderThumbnailSample` placeholders) updates to track the current page.

## 7. Error handling

A corrupt archive, missing file, or single-page decode failure shows a per-page inline error state (message + page number) inside `PageCanvas` rather than crashing the screen. Navigation past the failed page continues to work normally.

## 8. Format scope

CBZ + CBR only, per explicit scope call — exercises `CbzComicProvider`, `CbrComicProvider`, and `Rar5ComicProvider` (RAR5-specific variant, same `SevenZipEngine` dependency). CB7/PDF/DjVu are out of scope for this pass — each has its own native-binary deployment gap (onboarding.md §4's table) and becomes its own future pass. If `ProviderFactory.GetSourceProviderType` finds no provider for an issue's file extension, the Reader shows a clear "unsupported format" state instead of erroring.

## 9. Testing

- **Synthetic CBZ fixture**: generated via `System.IO.Compression` (a handful of solid-color PNG pages, built programmatically) — same "generate via the real code path, don't hand-write a fixture" precedent as `CeLibraryMigratorTests`. Drives automated `PageImageDecoder` tests: correct `PageCount`, byte-path decode producing a valid `Bitmap`, dispose-on-navigate-outside-window behavior.
- **Manual smoke test**: point the seeded demo library at a real file (`Spectacular_Spider_Man_Brand_New_Day_003_2026_3_covers_Digital_dekabro.cbz`, ~76MB, supplied for this purpose) and confirm open → decode → navigate (forward/back, arrows/clicks/keyboard) → dispose all work correctly in the running app, and that `LastPageRead` persists across a Reader → Detail → Reader round trip.
