namespace Paperbunkr.Data.Entities;

/// <summary>
/// Reader canvas background behind the page, an <see cref="AppSettings"/>-global setting (docs/
/// superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §10) -
/// global-only, no per-Issue override, since background is a personal viewing preference rather
/// than a per-book concern.
///
/// Matches ComicRackCE's <c>ImageBackgroundMode</c> (Engine/Display/ImageBackgroundMode.cs) minus
/// <c>Texture</c> - deliberately not ported (needs bundled texture assets or a file-picker surface
/// bigger than this pass), see the design spec §10.
/// </summary>
public enum ImageBackgroundMode
{
    Auto,
    Color
}
