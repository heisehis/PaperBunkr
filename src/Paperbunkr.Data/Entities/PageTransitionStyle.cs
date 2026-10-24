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
    Crossfade,

    /// <summary>CE's <c>TopDown</c>: the pages slide vertically whatever the reading direction (Motion v2 E, Q30).</summary>
    Vertical,

    /// <summary>CE's <c>Paging</c>: the new page sweeps in over the old one from the outer edge, casting a spine shadow; turning back slides
    /// the old page off to reveal the previous one underneath (Motion v2 E, Q30).</summary>
    Book,

    /// <summary>An animated corner curl: the old page folds away from its outer bottom corner, its paper back showing, revealing the new page
    /// beneath (Motion v2 E, Q30). Not in CE - a deliberate addition. Mirrored for right-to-left reading.</summary>
    Curl,
}
