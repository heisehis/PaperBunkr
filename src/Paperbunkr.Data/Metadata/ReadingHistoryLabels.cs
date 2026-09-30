using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The one formatter for the Insights History tab's names (docs/superpowers/specs/2026-09-29-insights-
/// reading-history-design.md §1): what <see cref="ReadingEvent.SeriesTitle"/> / <see cref="ReadingEvent.ItemLabel"/>
/// snapshot at write time, and what the History resolver shows for live rows - so a row reads the same
/// before and after its item is deleted. <see cref="Migrations.ReadingHistoryBackfill"/> mirrors
/// <see cref="IssueLabel"/>'s precedence in SQL (plain columns only; see its remarks).
/// </summary>
public static class ReadingHistoryLabels
{
    /// <summary><c>#12</c>, else <c>Vol. 2</c>, else the issue title, else null.</summary>
    public static string? IssueLabel(Issue issue)
    {
        if (NonBlank(issue.EffectiveNumber()) is string number)
        {
            return $"#{number}";
        }

        if (NonBlank(issue.EffectiveVolume()) is string volume)
        {
            return $"Vol. {volume}";
        }

        return NonBlank(issue.EffectiveTitle());
    }

    /// <summary>The History group's name for a book: its series' name when it has one, else its own title.</summary>
    public static string BookGroupTitle(Book book, BookSeries? series) =>
        series is not null && NonBlank(series.Name) is string name ? name : book.Title;

    /// <summary>A book's per-item label: its title inside a book series, null when standalone (the title is already the group name).</summary>
    public static string? BookLabel(Book book, BookSeries? series) => series is null ? null : NonBlank(book.Title);

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
