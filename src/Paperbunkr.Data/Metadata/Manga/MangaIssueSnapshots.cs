using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>
/// The one-time per-chapter record of inheritable values from before series-level fan-out first touched the chapter (design D8). Text fields are JSON strings,
/// credit roles are JSON string arrays, tag fields are arrays of [value, category, weight]. Needs the chapter's <see cref="Issue.Tags"/> loaded.
/// </summary>
public static class MangaIssueSnapshots
{
    public static string Serialize(Issue issue)
    {
        var values = new MangaFieldValues();
        foreach (var field in MangaInheritedFields.All)
        {
            MangaInheritedFields.ReadFromIssue(issue, field, values);
        }

        var root = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, text) in values.Text)
        {
            root[key] = text;
        }

        foreach (var (role, names) in values.Credits)
        {
            root[role] = names;
        }

        root[MangaInheritedFields.Genre] = values.Genres.Select(t => new[] { t.Value, t.Category, t.Weight.ToString() }).ToList();
        root[MangaInheritedFields.Tags] = values.Tags.Select(t => new[] { t.Value, t.Category, t.Weight.ToString() }).ToList();
        return JsonSerializer.Serialize(root);
    }

    /// <summary>Puts every snapshotted field back on the chapter. A key the snapshot lacks is left alone. The caller saves.</summary>
    public static void Restore(Issue issue, string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var field in MangaInheritedFields.All)
        {
            if (!doc.RootElement.TryGetProperty(field.Key, out var element))
            {
                continue;
            }

            var values = new MangaFieldValues();
            switch (field.Kind)
            {
                case MangaFieldKind.Text:
                    values.Text[field.Key] = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
                    break;
                case MangaFieldKind.Credit:
                    values.Credits[field.Key] = element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList();
                    break;
                default:
                    var target = field.TagField == IssueTagField.Genre ? values.Genres : values.Tags;
                    foreach (var row in element.EnumerateArray())
                    {
                        string value = row[0].GetString() ?? string.Empty;
                        string? category = row[1].ValueKind == JsonValueKind.String ? row[1].GetString() : null;
                        var weight = Enum.TryParse<IssueTagWeight>(row[2].GetString(), out var w) ? w : IssueTagWeight.Unset;
                        if (value.Length > 0)
                        {
                            target.Add(new MangaTagValue(value, category, weight));
                        }
                    }

                    break;
            }

            MangaInheritedFields.WriteToIssue(issue, field, values);
        }
    }
}
