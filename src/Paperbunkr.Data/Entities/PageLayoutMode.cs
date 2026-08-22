namespace Paperbunkr.Data.Entities;

/// <summary>
/// Reader paged-mode double-page spread layout, backing <see cref="AppSettings.DefaultPageLayoutMode"/>/
/// <see cref="Series.PageLayoutMode"/>/<see cref="Issue.PageLayoutModeOverride"/> (docs/superpowers/
/// specs/2026-08-15-reader-double-page-spread-design.md). Collapses CE's three-way
/// `PageLayoutMode { Single, Double, DoubleAdaptive }` (`ComicRack.Engine/Display/PageLayoutMode.cs`)
/// into two - <see cref="Double"/> here behaves like CE's `DoubleAdaptive` (no forced-width padding
/// for an un-pairable solo landscape page); CE's plain `Double` mode's padding was the only real
/// difference between its two variants and is out of scope (spec §1/§8).
/// </summary>
public enum PageLayoutMode
{
    Single,
    Double
}
