using System.Collections.Generic;
using System.Text.RegularExpressions;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.VirtualTags;

/// <summary>
/// Small, purpose-built template evaluator for Virtual Tags (docs/superpowers/specs/
/// 2026-08-07-preferences-libraries-tab-design.md §1) - deliberately not CE's
/// <c>ComicBook.GetFullTitle</c>/<c>ExtendedStringFormater</c>, which is built on a reflection +
/// "shadow value" system tied to CE's whole legacy object model. Flat <c>{FieldName}</c> token
/// substitution only, same complexity ceiling as CE's own format strings.
/// </summary>
public static class VirtualTagTemplateEvaluator
{
    private static readonly Regex TokenPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private static readonly HashSet<string> KnownFields = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "series", "number", "volume", "year", "title", "publisher", "writer", "penciller",
    };

    /// <summary>
    /// Substitutes every recognized <c>{FieldName}</c> token with the corresponding
    /// <paramref name="issue"/>/<paramref name="series"/> value (empty string if that value is
    /// null). An unrecognized token name passes through unchanged - visibly wrong is easier to
    /// debug in the editor's live preview than a silently-dropped token.
    /// </summary>
    public static string Evaluate(string template, Issue issue, Series? series)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return TokenPattern.Replace(template, match =>
        {
            string field = match.Groups[1].Value;
            return KnownFields.Contains(field) ? GetFieldValue(field, issue, series) ?? string.Empty : match.Value;
        });
    }

    private static string? GetFieldValue(string field, Issue issue, Series? series) => field.ToLowerInvariant() switch
    {
        "series" => series?.Name,
        "number" => issue.EffectiveNumber(),
        "volume" => issue.EffectiveVolume(),
        "year" => issue.EffectiveYear()?.ToString(),
        "title" => issue.Title,
        "publisher" => issue.Publisher,
        "writer" => issue.Writer,
        "penciller" => issue.Penciller,
        _ => null,
    };
}
