namespace Paperbunkr.Data.Entities;

/// <summary>
/// Page layout/navigation model for a <see cref="Series"/> (and optionally overridden per
/// <see cref="Issue"/> via <see cref="Issue.ReadingModeOverride"/>). New in Paperbunkr, consumed
/// directly by the reader canvas's layout model (docs/onboarding.md §8).
///
/// Double-page spread is deliberately NOT a value here — per docs/onboarding.md §6, it's a
/// display toggle orthogonal to reading mode that applies under LeftToRight/RightToLeft, not a
/// distinct mode.
/// </summary>
public enum ReadingMode
{
    LeftToRight,
    RightToLeft,
    VerticalContinuous,
    HorizontalContinuous
}
