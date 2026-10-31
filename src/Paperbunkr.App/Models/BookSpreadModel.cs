using System.Linq;
using Paperbunkr.App.Services.Books;

namespace Paperbunkr.App.Models;

/// <summary>What the Books screen's "Now reading" spread shows (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §3.1).</summary>
public sealed record BookSpreadModel(BookCardSample Card, BookSpreadMode Mode, string? Quote, string MetaLine)
{
    public bool IsNowReading => Mode == BookSpreadMode.NowReading;

    /// <summary>Caps by hand: Avalonia has no text-transform.</summary>
    public string Overline => IsNowReading ? "NOW READING" : "UP NEXT";

    public string ActionLabel => IsNowReading ? "Continue reading" : "Start reading";

    public bool HasQuote => !string.IsNullOrWhiteSpace(Quote);

    public bool HasMetaLine => !string.IsNullOrWhiteSpace(MetaLine);

    /// <summary>"Maarja Ilves · The Orchard Cycle #2"; whichever parts exist.</summary>
    public string Byline
    {
        get
        {
            string? series = Card.HasSeries
                ? Card.SeriesIndex is float index ? $"{Card.SeriesName} #{index:0.##}" : Card.SeriesName
                : null;
            return string.Join(" · ", new[] { Card.Author, series }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }
}
