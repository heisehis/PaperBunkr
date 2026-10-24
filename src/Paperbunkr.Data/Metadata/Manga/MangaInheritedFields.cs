using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

public enum MangaFieldKind
{
    /// <summary>One string column: Publisher, Imprint, AgeRating.</summary>
    Text,

    /// <summary>One of the seven per-role credit columns; the series side is ordered <see cref="SeriesCredit"/> rows.</summary>
    Credit,

    /// <summary>Genre or Tags: <see cref="IssueTag"/> rows on the chapter, <see cref="SeriesTag"/> rows on the series.</summary>
    Tags,
}

public sealed record MangaField(string Key, string Label, MangaFieldKind Kind, IssueTagField? TagField = null);

/// <summary>
/// The single registry of fields a manga series passes down to its chapters (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md D10).
/// The editor, fan-out, overrides, seeder and reclassify snapshot all read this one list, so they cannot drift apart. Summary is deliberately absent:
/// a chapter's own synopsis is never overwritten, and export falls back to the series summary instead.
/// </summary>
public static class MangaInheritedFields
{
    public const string Publisher = "Publisher";
    public const string Imprint = "Imprint";
    public const string AgeRating = "AgeRating";
    public const string Genre = "Genre";
    public const string Tags = "Tags";

    public static readonly IReadOnlyList<string> CreditRoles = new[] { "Writer", "Penciller", "Inker", "Colorist", "Letterer", "CoverArtist", "Editor" };

    public static readonly IReadOnlyList<MangaField> All = BuildAll();

    private static IReadOnlyList<MangaField> BuildAll()
    {
        var list = new List<MangaField>
        {
            new(Publisher, "Publisher", MangaFieldKind.Text),
            new(Imprint, "Imprint", MangaFieldKind.Text),
            new(AgeRating, "Age rating", MangaFieldKind.Text),
        };
        list.AddRange(CreditRoles.Select(role => new MangaField(role, role, MangaFieldKind.Credit)));
        list.Add(new MangaField(Genre, "Genres", MangaFieldKind.Tags, IssueTagField.Genre));
        list.Add(new MangaField(Tags, "Tags", MangaFieldKind.Tags, IssueTagField.Tags));
        return list;
    }

    public static MangaField? Find(string key) => All.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));

    /// <summary>Manga, Manhua and Manhwa: the series that use series-level inheritance. Comics stay issue-centric.</summary>
    public static bool IsMangaFamily(ContentType contentType) => contentType is ContentType.Manga or ContentType.Manhua or ContentType.Manhwa;

    // ---------- Chapter side ----------

    /// <summary>
    /// The chapter's value for <paramref name="field"/> in a form that two values can be compared as equal when they mean the same thing
    /// (case, spacing and separators ignored; tag order ignored). Not for display.
    /// </summary>
    public static string ComparableIssueValue(Issue issue, MangaField field) => field.Kind switch
    {
        MangaFieldKind.Text => ComparableText(ReadIssueText(issue, field.Key)),
        MangaFieldKind.Credit => ComparableNames(CreatorResolver.ParseNames(ReadIssueText(issue, field.Key))),
        _ => ComparableTags(issue.Tags.Where(t => t.Field == field.TagField).Select(t => (t.Value, t.Category, t.Weight))),
    };

    /// <summary>The series' value for <paramref name="field"/>, in the same comparable form as <see cref="ComparableIssueValue"/>.</summary>
    public static string ComparableSeriesValue(MangaFieldValues values, MangaField field) => field.Kind switch
    {
        MangaFieldKind.Text => ComparableText(values.Text.GetValueOrDefault(field.Key)),
        MangaFieldKind.Credit => ComparableNames(values.Credits.GetValueOrDefault(field.Key) ?? new List<string>()),
        _ => ComparableTags((field.TagField == IssueTagField.Genre ? values.Genres : values.Tags).Select(t => (t.Value, t.Category, t.Weight))),
    };

    /// <summary>The text a chapter's string column should hold for <paramref name="field"/>, or null when empty. Tag fields have no column.</summary>
    public static string? IssueTextFor(MangaFieldValues values, MangaField field) => field.Kind switch
    {
        MangaFieldKind.Text => NullIfBlank(values.Text.GetValueOrDefault(field.Key)),
        MangaFieldKind.Credit => NullIfBlank(string.Join(", ", values.Credits.GetValueOrDefault(field.Key) ?? new List<string>())),
        _ => null,
    };

    /// <summary>Writes <paramref name="field"/> from the series values onto one chapter (its string column, or its <see cref="IssueTag"/> rows; the caller saves).</summary>
    public static void WriteToIssue(Issue issue, MangaField field, MangaFieldValues values)
    {
        if (field.Kind == MangaFieldKind.Tags)
        {
            SyncTags(issue, field.TagField!.Value, field.TagField == IssueTagField.Genre ? values.Genres : values.Tags);
            return;
        }

        WriteIssueText(issue, field.Key, IssueTextFor(values, field));
    }

    /// <summary>Copies the chapter's current value for <paramref name="field"/> into <paramref name="values"/> (used by the seeder and the reclassify snapshot).</summary>
    public static void ReadFromIssue(Issue issue, MangaField field, MangaFieldValues values)
    {
        switch (field.Kind)
        {
            case MangaFieldKind.Text:
                values.Text[field.Key] = NullIfBlank(ReadIssueText(issue, field.Key));
                break;
            case MangaFieldKind.Credit:
                values.Credits[field.Key] = CreatorResolver.ParseNames(ReadIssueText(issue, field.Key)).ToList();
                break;
            default:
                var target = field.TagField == IssueTagField.Genre ? values.Genres : values.Tags;
                target.Clear();
                target.AddRange(issue.Tags.Where(t => t.Field == field.TagField).Select(t => new MangaTagValue(t.Value, t.Category, t.Weight)));
                break;
        }
    }

    private static string? ReadIssueText(Issue issue, string key) => key switch
    {
        Publisher => issue.Publisher,
        Imprint => issue.Imprint,
        AgeRating => issue.AgeRating,
        "Writer" => issue.Writer,
        "Penciller" => issue.Penciller,
        "Inker" => issue.Inker,
        "Colorist" => issue.Colorist,
        "Letterer" => issue.Letterer,
        "CoverArtist" => issue.CoverArtist,
        "Editor" => issue.Editor,
        _ => null,
    };

    public static void WriteIssueText(Issue issue, string key, string? value)
    {
        switch (key)
        {
            case Publisher: issue.Publisher = value; break;
            case Imprint: issue.Imprint = value; break;
            case AgeRating: issue.AgeRating = value; break;
            case "Writer": issue.Writer = value; break;
            case "Penciller": issue.Penciller = value; break;
            case "Inker": issue.Inker = value; break;
            case "Colorist": issue.Colorist = value; break;
            case "Letterer": issue.Letterer = value; break;
            case "CoverArtist": issue.CoverArtist = value; break;
            case "Editor": issue.Editor = value; break;
        }
    }

    /// <summary>Makes the chapter's tags of one field exactly the wanted list, keeping rows that already match and taking the series' category and weight.</summary>
    public static void SyncTags(Issue issue, IssueTagField tagField, IReadOnlyList<MangaTagValue> wanted)
    {
        var existing = issue.Tags.Where(t => t.Field == tagField).ToList();
        foreach (var tag in existing.Where(t => !wanted.Any(w => string.Equals(w.Value, t.Value, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            issue.Tags.Remove(tag);
        }

        foreach (var want in wanted)
        {
            var match = existing.FirstOrDefault(t => string.Equals(t.Value, want.Value, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                issue.Tags.Add(new IssueTag { IssueId = issue.Id, Field = tagField, Value = want.Value, Category = want.Category, Weight = want.Weight });
            }
            else
            {
                match.Category = want.Category;
                match.Weight = want.Weight;
            }
        }
    }

    // ---------- Series side ----------

    /// <summary>Reads the series' own canonical values. <see cref="Series.Credits"/> and <see cref="Series.SeriesTags"/> must be loaded.</summary>
    public static MangaFieldValues ReadFromSeries(Series series)
    {
        var values = new MangaFieldValues();
        values.Text[Publisher] = NullIfBlank(series.Publisher);
        values.Text[Imprint] = NullIfBlank(series.Imprint);
        values.Text[AgeRating] = NullIfBlank(series.AgeRating);

        foreach (string role in CreditRoles)
        {
            values.Credits[role] = series.Credits.Where(c => c.Role == role).OrderBy(c => c.SortOrder).Select(c => c.Name).ToList();
        }

        values.Genres.AddRange(series.SeriesTags.Where(t => t.Field == IssueTagField.Genre).OrderBy(t => t.Id).Select(t => new MangaTagValue(t.Value, t.Category, t.Weight)));
        values.Tags.AddRange(series.SeriesTags.Where(t => t.Field == IssueTagField.Tags).OrderBy(t => t.Id).Select(t => new MangaTagValue(t.Value, t.Category, t.Weight)));
        return values;
    }

    /// <summary>Replaces the series' canonical values with <paramref name="values"/> (the caller saves). Only the fields in <paramref name="keys"/> are touched.</summary>
    public static void WriteToSeries(Series series, MangaFieldValues values, IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            var field = Find(key);
            if (field is null)
            {
                continue;
            }

            switch (field.Kind)
            {
                case MangaFieldKind.Text:
                    string? text = NullIfBlank(values.Text.GetValueOrDefault(key));
                    if (key == Publisher) series.Publisher = text;
                    else if (key == Imprint) series.Imprint = text;
                    else if (key == AgeRating) series.AgeRating = text;
                    break;
                case MangaFieldKind.Credit:
                    foreach (var old in series.Credits.Where(c => c.Role == key).ToList())
                    {
                        series.Credits.Remove(old);
                    }

                    int order = 0;
                    foreach (string name in (values.Credits.GetValueOrDefault(key) ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        series.Credits.Add(new SeriesCredit { SeriesId = series.Id, Role = key, Name = name, SortOrder = order++ });
                    }

                    break;
                default:
                    foreach (var old in series.SeriesTags.Where(t => t.Field == field.TagField).ToList())
                    {
                        series.SeriesTags.Remove(old);
                    }

                    foreach (var tag in (field.TagField == IssueTagField.Genre ? values.Genres : values.Tags)
                                 .GroupBy(t => t.Value, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                    {
                        series.SeriesTags.Add(new SeriesTag { SeriesId = series.Id, Field = field.TagField!.Value, Value = tag.Value, Category = tag.Category, Weight = tag.Weight });
                    }

                    break;
            }
        }
    }

    // ---------- helpers ----------

    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ComparableText(string? value) => NullIfBlank(value)?.ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// Tags compare by name, category and weight together: changing only how central a tag is, or the group it sits under, is a real change (it must be saved and passed to the chapters),
    /// and a chapter whose tag has a different weight than the series' is an exception.
    /// </summary>
    private static string ComparableTags(IEnumerable<(string Value, string? Category, IssueTagWeight Weight)> tags) =>
        string.Join("|", tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Value))
            .Select(t => $"{t.Value.Trim().ToLowerInvariant()}~{(t.Category ?? string.Empty).Trim().ToLowerInvariant()}~{(int)t.Weight}")
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal));

    private static string ComparableNames(IEnumerable<string> names) =>
        string.Join("|", names.Select(n => n.Trim().ToLowerInvariant()).Where(n => n.Length > 0).Distinct().OrderBy(n => n, StringComparer.Ordinal));
}
