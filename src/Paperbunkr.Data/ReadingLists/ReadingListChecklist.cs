using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>One printed checklist row.</summary>
public sealed record ChecklistRow(int Position, string? GroupLabel, string Display, int? Year, bool IsRead, bool IsOwned, string? Note);

/// <summary>Everything the printable checklist shows, independent of how it is drawn.</summary>
public sealed record ChecklistModel(string Title, int IssueCount, int ReadCount, int MissingCount, DateTime PrintedLocal, IReadOnlyList<ChecklistRow> Rows)
{
    public string MetaLine =>
        $"{IssueCount} issue{(IssueCount == 1 ? "" : "s")} · {ReadCount} read · {MissingCount} missing · printed {PrintedLocal:d MMM yyyy} from Paperbunkr";
}

/// <summary>
/// Builds the printable checklist's content from a reading list (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-
/// design.md §6). The drawing lives in the App (<c>ReadingListChecklistPdf</c>). ComicRack CE has no print or checklist feature.
/// </summary>
public static class ReadingListChecklist
{
    public static ChecklistModel Build(PaperbunkrDbContext context, int readingListId, DateTime printedLocal)
    {
        var list = context.ReadingLists.AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .First(r => r.Id == readingListId);

        var rows = list.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select((item, index) =>
            {
                var issue = item.Issue!;
                return new ChecklistRow(
                    index + 1,
                    string.IsNullOrWhiteSpace(item.GroupLabel) ? null : item.GroupLabel,
                    $"{issue.Series?.Name ?? "Unknown Series"} #{issue.EffectiveNumber()}",
                    issue.EffectiveYear(),
                    issue.HasBeenRead(),
                    !issue.FileIsMissing,
                    string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim());
            })
            .ToList();

        return new ChecklistModel(list.Name, rows.Count, rows.Count(r => r.IsRead), rows.Count(r => !r.IsOwned), printedLocal, rows);
    }
}
