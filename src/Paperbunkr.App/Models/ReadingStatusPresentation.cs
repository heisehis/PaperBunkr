using FluentIcons.Common;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// One source of truth for how a <see cref="ReadingStatus"/> looks - its glyph, friendly label and
/// colour - shared by <see cref="Services.MarkResolver.ResolveReadingStatus"/> (the hero/band mark)
/// and <see cref="ViewModels.ReadingStatusPickerViewModel"/> (the setter flyout), so the chip and
/// the menu row for a status always read identically
/// (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md Part 2 §C).
///
/// <para>Hex colours mirror the app's semantic tokens (named in the comments); kept as literals
/// because <see cref="Services.MarkResolver"/> is deliberately Avalonia-resource-free, same as the
/// age-rating chip colours in <c>age-rating-aliases.tsv</c>.</para>
/// </summary>
public readonly record struct ReadingStatusPresentation(Symbol Glyph, string Label, string? Hex)
{
    public bool HasGlyph => Label.Length > 0;

    /// <summary><see cref="ReadingStatus.Unknown"/> returns an all-default value with an empty
    /// <see cref="Label"/> (<see cref="HasGlyph"/> false) - callers render nothing / a "not set" affordance.</summary>
    public static ReadingStatusPresentation For(ReadingStatus status) => status switch
    {
        ReadingStatus.Reading   => new(Symbol.BookOpen,        "Reading",    "#E0995A"), // PbAccentTextColor
        ReadingStatus.ReReading => new(Symbol.ArrowSync,       "Re-reading", "#E0995A"),
        ReadingStatus.Completed => new(Symbol.CheckmarkCircle, "Completed",  "#5FA889"), // PbSuccessColor
        ReadingStatus.Paused    => new(Symbol.PauseCircle,     "On Hold",    "#D7AC4C"), // PbBadgeColor
        ReadingStatus.Dropped   => new(Symbol.DismissCircle,   "Dropped",    "#D96C6C"), // PbDangerColor
        ReadingStatus.Planned   => new(Symbol.Clock,           "Planned",    "#77726A"), // PbTextFaintColor
        _ => new(Symbol.Circle, string.Empty, null),
    };
}
