namespace Paperbunkr.Data.Entities;

/// <summary>
/// Reader paged-mode page-turn transition style, backing <see cref="AppSettings.PageTransitionStyle"/>
/// (docs/superpowers/specs/2026-08-13-reader-page-transition-animations-design.md). Not a port of any
/// single CE setting - CE split "which style" and "on or off" across separate `Blender`
/// selection/`BlendWhilePaging` fields (`ComicDisplayControl.cs`), collapsed here into one enum since
/// they're the same question from the user's side. <see cref="None"/> is the default (spec §1: off by
/// default, matching CE's own <c>BlendWhilePaging</c> default of <c>false</c>).
/// </summary>
public enum PageTransitionStyle
{
    None,
    Slide,
    Crossfade
}
