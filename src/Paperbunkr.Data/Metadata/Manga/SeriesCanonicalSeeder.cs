using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>
/// Proposes a series' canonical values from what its chapters already hold (design D7, spec §11 "Needs Review"). Read-only: nothing is written until the user
/// saves Edit Series. A field whose non-blank chapter values all agree is simply proposed. A field whose chapters disagree is proposed as the most common value and
/// listed in <c>candidates</c> so the editor can flag it Needs Review and show the alternatives. Chapters with a blank value never count as disagreeing.
/// </summary>
public static class SeriesCanonicalSeeder
{
    public static (MangaFieldValues Values, Dictionary<string, List<string>> Candidates) Seed(Series series, IReadOnlyList<Issue> issues)
    {
        var values = new MangaFieldValues();
        var candidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var field in MangaInheritedFields.All)
        {
            var groups = issues
                .Select(issue =>
                {
                    var one = new MangaFieldValues();
                    MangaInheritedFields.ReadFromIssue(issue, field, one);
                    return (Comparable: MangaInheritedFields.ComparableSeriesValue(one, field), Values: one);
                })
                .Where(x => x.Comparable.Length > 0)
                .GroupBy(x => x.Comparable)
                .OrderByDescending(g => g.Count())
                .ToList();

            if (groups.Count == 0)
            {
                // No chapter holds anything: fall back to the series' own legacy string where there is one (Publisher only).
                if (field.Key == MangaInheritedFields.Publisher)
                {
                    values.Text[field.Key] = MangaInheritedFields.NullIfBlank(series.Publisher);
                }
                else if (field.Key == MangaInheritedFields.Imprint)
                {
                    values.Text[field.Key] = MangaInheritedFields.NullIfBlank(series.Imprint);
                }
                else if (field.Key == MangaInheritedFields.AgeRating)
                {
                    values.Text[field.Key] = MangaInheritedFields.NullIfBlank(series.AgeRating);
                }

                continue;
            }

            Copy(groups[0].First().Values, field, values);
            if (groups.Count > 1)
            {
                candidates[field.Key] = groups.Select(g => Display(g.First().Values, field)).ToList();
            }
        }

        return (values, candidates);
    }

    private static void Copy(MangaFieldValues from, MangaField field, MangaFieldValues to)
    {
        switch (field.Kind)
        {
            case MangaFieldKind.Text:
                to.Text[field.Key] = from.Text.GetValueOrDefault(field.Key);
                break;
            case MangaFieldKind.Credit:
                to.Credits[field.Key] = (from.Credits.GetValueOrDefault(field.Key) ?? new List<string>()).ToList();
                break;
            default:
                (field.TagField == IssueTagField.Genre ? to.Genres : to.Tags).AddRange(field.TagField == IssueTagField.Genre ? from.Genres : from.Tags);
                break;
        }
    }

    private static string Display(MangaFieldValues values, MangaField field) => field.Kind switch
    {
        MangaFieldKind.Text => values.Text.GetValueOrDefault(field.Key) ?? string.Empty,
        MangaFieldKind.Credit => string.Join(", ", values.Credits.GetValueOrDefault(field.Key) ?? new List<string>()),
        _ => string.Join(", ", (field.TagField == IssueTagField.Genre ? values.Genres : values.Tags).Select(t => t.Value)),
    };
}
