using cYo.Projects.ComicRack.Engine.Database;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>
/// .cbl import/export (docs/superpowers/specs/2026-08-06-reading-lists-design.md §4). Reuses
/// <see cref="ComicReadingListContainer"/>/<see cref="ComicReadingListItem"/> directly — the real,
/// already-ported <c>XmlSerializer</c>-based CBL format (Paperbunkr.Engine/Database), dormant since
/// the retarget spike. No new parser needed.
/// </summary>
public static class CblReadingListIO
{
    public static ReadingList Import(PaperbunkrDbContext context, string filePath)
    {
        var container = ComicReadingListContainer.Deserialize(filePath)
            ?? throw new InvalidDataException($"'{filePath}' is not a valid CBL reading list.");

        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = string.IsNullOrEmpty(container.Name) ? Path.GetFileNameWithoutExtension(filePath) : container.Name,
            CreatedAt = now,
            UpdatedAt = now,
        };

        int sortOrder = 0;
        foreach (var item in container.Items)
        {
            var issue = ReadingListMatcher.ResolveOrCreatePlaceholder(
                context,
                item.Series,
                item.Number,
                item.Volume < 0 ? null : item.Volume.ToString(),
                item.Year < 0 ? null : item.Year,
                string.IsNullOrEmpty(item.Format) ? null : item.Format);

            list.Items.Add(new ReadingListItem { IssueId = issue.Id, SortOrder = sortOrder++ });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        return list;
    }

    public static void Export(PaperbunkrDbContext context, int readingListId, string filePath)
    {
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .First(r => r.Id == readingListId);

        var container = new ComicReadingListContainer { Name = list.Name };
        foreach (var item in list.Items.OrderBy(i => i.SortOrder))
        {
            var issue = item.Issue!;
            container.Items.Add(new ComicReadingListItem
            {
                Series = issue.Series?.Name ?? string.Empty,
                Number = issue.EffectiveNumber() ?? string.Empty,
                Volume = int.TryParse(issue.EffectiveVolume(), out var volumeNumber) ? volumeNumber : -1,
                Year = issue.EffectiveYear() ?? -1,
                Format = issue.Format ?? string.Empty,
            });
        }

        container.Serialize(filePath);
    }
}
