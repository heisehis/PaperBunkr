namespace Paperbunkr.Data.Entities;

/// <summary>
/// Reader canvas background behind the page, an <see cref="AppSettings"/>-global setting (docs/
/// superpowers/specs/2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §10) -
/// global-only, no per-Issue override, since background is a personal viewing preference rather
/// than a per-book concern.
///
/// Matches ComicRackCE's <c>ImageBackgroundMode</c> (Engine/Display/ImageBackgroundMode.cs).
/// <c>Texture</c> was originally a named deviation (skipped) - reversed in
/// docs/superpowers/specs/2026-09-10-reader-backlog-batch-b-design.md Item 1: a curated set of 3
/// bundled seamless tiles (<c>AppSettings.BackgroundTexture</c> holds the id), statically tiled
/// (not zoom-scaled like CE), no user file picker or layout picker in v1. CE's separate "paper
/// texture" (a texture multiplied over each page, hardware-renderer only) remains unported.
/// </summary>
public enum ImageBackgroundMode
{
    Auto,
    Color,
    Texture,
}
