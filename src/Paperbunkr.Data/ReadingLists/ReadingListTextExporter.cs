using System.Text;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>
/// Plain-text/Markdown export (docs/superpowers/specs/2026-08-06-reading-lists-design.md §6),
/// adopted from CBLManager's <c>ExportReadingListAsText</c> essentially as-is: a numbered list that
/// reads correctly as both plain text and Markdown with no special syntax, so there's no separate
/// mode to pick.
/// </summary>
public static class ReadingListTextExporter
{
    public static string Export(PaperbunkrDbContext context, int readingListId)
    {
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .First(r => r.Id == readingListId);

        var items = list.Items.OrderBy(i => i.SortOrder).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# {list.Name}");
        sb.AppendLine();
        sb.AppendLine($"{items.Count} issue{(items.Count == 1 ? string.Empty : "s")}");
        sb.AppendLine();

        for (int i = 0; i < items.Count; i++)
        {
            var issue = items[i].Issue!;
            string series = issue.Series?.Name ?? "Unknown Series";
            string year = issue.EffectiveYear() is int y ? $" ({y})" : string.Empty;
            sb.AppendLine($"{i + 1}. {series} #{issue.EffectiveNumber()}{year}");
        }

        return sb.ToString();
    }
}
