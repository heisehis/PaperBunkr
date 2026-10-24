using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.Manga;

/// <summary>One genre or tag with the category and weight the series carries for it.</summary>
public sealed record MangaTagValue(string Value, string? Category, IssueTagWeight Weight);

/// <summary>
/// The series-level values of every inheritable field (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §4/D10), as one plain object:
/// what Edit Series edits, what fan-out reads, and what the seeder proposes. Text fields hold a trimmed string or null; credits hold ordered names per
/// role; tags hold genre and tag lists. <see cref="MangaInheritedFields"/> is the only code that knows how each key maps onto a <see cref="Series"/> or an <see cref="Issue"/>.
/// </summary>
public sealed class MangaFieldValues
{
    public Dictionary<string, string?> Text { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, List<string>> Credits { get; } = new(StringComparer.Ordinal);

    public List<MangaTagValue> Genres { get; } = new();

    public List<MangaTagValue> Tags { get; } = new();

    public MangaFieldValues Clone()
    {
        var copy = new MangaFieldValues();
        foreach (var (key, value) in Text)
        {
            copy.Text[key] = value;
        }

        foreach (var (role, names) in Credits)
        {
            copy.Credits[role] = names.ToList();
        }

        copy.Genres.AddRange(Genres);
        copy.Tags.AddRange(Tags);
        return copy;
    }
}
