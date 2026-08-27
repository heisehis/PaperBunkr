using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Models;

/// <summary>Home screen's "Try This Reading List" card (docs/superpowers/specs/
/// 2026-08-18-home-screen-design.md Module 5) - a synopsis/tags treatment for a
/// <see cref="ReadingList"/>, not a per-issue or per-series card.</summary>
public sealed class ReadingListSpotlightSample
{
    public int ReadingListId { get; init; }
    public required string Name { get; init; }
    public required string Synopsis { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public int FirstUnreadIssueId { get; init; }
    public required IBrush CoverBrush { get; init; }
    public Bitmap? CoverImage { get; init; }

    public static ReadingListSpotlightSample FromReadingList(ReadingList list)
    {
        var orderedItems = list.Items.OrderBy(i => i.SortOrder).ToList();
        var firstIssue = orderedItems.FirstOrDefault()?.Issue;
        string firstSeriesName = firstIssue?.Series?.Name ?? string.Empty;

        string synopsis = !string.IsNullOrWhiteSpace(list.Description)
            ? list.Description!
            : $"A {list.Items.Count}-issue reading order starting with {firstSeriesName}.";

        var genreFrequency = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var item in orderedItems)
        {
            if (item.Issue is null)
            {
                continue;
            }

            foreach (string token in TextTokenizer.Tokenize(item.Issue.JoinedGenre()))
            {
                genreFrequency[token] = genreFrequency.GetValueOrDefault(token) + 1;
            }
        }

        var tags = genreFrequency
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Take(5)
            .Select(kv => kv.Key)
            .ToList();

        int firstUnreadIssueId = orderedItems
            .Select(i => i.Issue)
            .FirstOrDefault(i => i is not null && i.IsUnread())?.Id ?? 0;

        return new ReadingListSpotlightSample
        {
            ReadingListId = list.Id,
            Name = list.Name,
            Synopsis = synopsis,
            Tags = tags,
            FirstUnreadIssueId = firstUnreadIssueId,
            CoverBrush = SeriesCardSample.CoverBrushFor(firstSeriesName),
            CoverImage = firstIssue is not null ? CoverImageCache.Get(firstIssue.Id) : null,
        };
    }
}
