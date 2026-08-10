namespace Paperbunkr.Data.Entities;

/// <summary>
/// Page layout/navigation model for a <see cref="Series"/> (and optionally overridden per
/// <see cref="Issue"/> via <see cref="Issue.ReadingModeOverride"/>). New in Paperbunkr, consumed
/// directly by the reader canvas's layout model (docs/onboarding.md §8).
///
/// Double-page spread is deliberately NOT a value here — per docs/onboarding.md §6, it's a
/// display toggle orthogonal to reading mode that applies under LeftToRight/RightToLeft, not a
/// distinct mode.
///
/// <see cref="HorizontalContinuousRightToLeft"/>/<see cref="Webtoon"/> added per user direction
/// while building continuous scroll (docs/superpowers/specs/2026-08-10-reader-polish-continuous-
/// scroll-chrome-overlays-design.md §4's follow-up refinement, not the original spec) - stored via
/// the same <c>HasConversion&lt;string&gt;()</c> mapping every enum in this codebase uses, so no
/// migration needed for new values. <see cref="VerticalContinuous"/>/<see cref="HorizontalContinuous"/>
/// stack pages with a visible gap between them; <see cref="Webtoon"/> merges pages edge-to-edge
/// (no gap) - the distinction real webtoon/manhwa readers make between "continuous" and "webtoon"
/// scroll, per user direction to check other apps' conventions. Webtoon is vertical-only (that's
/// the format convention) - no HorizontalWebtoon value.
/// </summary>
public enum ReadingMode
{
    LeftToRight,
    RightToLeft,
    VerticalContinuous,
    HorizontalContinuous,
    HorizontalContinuousRightToLeft,
    Webtoon
}
