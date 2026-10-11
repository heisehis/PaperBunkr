using Avalonia.Media;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>
/// One row of Home's Up Next list (docs/superpowers/specs/2026-10-06-smart-features-design.md §4.4): the resolver's pick with its
/// rank, the series cover and the time-left estimate, all resolved in the background snapshot pass. The score that ordered the rows is
/// deliberately not carried here - only the rank and the one reason are shown.
/// </summary>
public sealed class HomeUpNextRow
{
    public required UpNextItem Item { get; init; }

    /// <summary>1-based position in the list.</summary>
    public required int Rank { get; init; }

    public required IBrush CoverBrush { get; init; }

    public IImage? CoverImage { get; init; }

    /// <summary>"~25 min", or null when there is no reading pace yet or the page count is unknown.</summary>
    public string? TimeLeft { get; init; }

    public bool HasTimeLeft => !string.IsNullOrEmpty(TimeLeft);

    public int IssueId => Item.IssueId;

    public string Title => $"{Item.SeriesName} {Item.IssueLabel}";

    public string Reason => Item.ReasonText;

    /// <summary>What a screen reader says for the whole row.</summary>
    public string AccessibleName => HasTimeLeft ? $"{Rank}. {Title}. {Reason}. {TimeLeft}" : $"{Rank}. {Title}. {Reason}";
}
