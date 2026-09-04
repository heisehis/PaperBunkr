using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Series-level publisher / year / format / age-rating / language, aggregated across the series'
/// issues rather than read off one (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-
/// glyphs-design.md Part 4 - user report: "Absolute Batman" showed no publisher/format/rating
/// because the cover issue happened to have none set). Feeds the hero badge row's whole-series view;
/// a focused issue's own values still take over when one is selected.
/// </summary>
public readonly record struct SeriesMetaFields(string? Publisher, string? Year, string? Format, string? AgeRating, string? LanguageIso)
{
    public static readonly SeriesMetaFields Empty = new(null, null, null, null, null);

    public static SeriesMetaFields FromSeries(Series series)
    {
        var issues = series.Issues;
        return new SeriesMetaFields(
            Publisher: Blank(series.Publisher) is { } p ? p : MostCommon(issues.Select(i => i.Publisher)),
            Year: issues.Select(i => i.ReleasedTime?.Year).Where(y => y is > 0).DefaultIfEmpty(null).Min() is int y0
                ? y0.ToString()
                : null,
            Format: MostCommon(issues.Select(i => i.Format)),
            AgeRating: MostCommon(issues.Select(i => i.AgeRating)),
            LanguageIso: SingleDistinct(issues.Select(i => i.LanguageISO)));
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>The most frequent non-blank value; ties broken by first appearance. Null when none set.</summary>
    private static string? MostCommon(IEnumerable<string?> values) => values
        .Select(Blank)
        .Where(v => v is not null)
        .GroupBy(v => v!, System.StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(g => g.Count())
        .Select(g => g.First())
        .FirstOrDefault();

    /// <summary>The one value every non-blank issue shares - null if they disagree or none are set
    /// (a mixed-language series shouldn't claim a single flag).</summary>
    private static string? SingleDistinct(IEnumerable<string?> values)
    {
        var distinct = values.Select(Blank).Where(v => v is not null)
            .Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }
}
