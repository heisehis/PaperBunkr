using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>One entry the repair would link: the list, what was asked for, and the comic found.</summary>
public sealed record RepairRelink(int ReadingListId, string ListName, string Asked, string Found, string Why);

/// <summary>
/// What <see cref="ReadingListRepair.Apply"/> would do, and what it found but leaves alone.
/// </summary>
/// <param name="Relinks">Entries reading "missing" whose comic is in the library.</param>
/// <param name="NeedChoice">Entries with candidates the source does not single out - settled one by one with Find &amp; link.</param>
/// <param name="StillMissing">Entries nothing in the library fits.</param>
/// <param name="AlreadyInList">Entries whose comic is already elsewhere in the same list.</param>
/// <param name="SplitSeries">Titles held as more than one series row where each row owns comics ("The Ultimates · Ultimates"). Reported only:
/// they may be different series, and the matcher now searches all of them either way.</param>
/// <param name="UnusedPlaceholders">Placeholder issues no reading list uses. Reported only: the Library's "add a physical book" makes
/// placeholders the same way, so these cannot be told from deliberate entries (decision Q10).</param>
public sealed record RepairPreview(
    IReadOnlyList<RepairRelink> Relinks,
    int NeedChoice,
    int StillMissing,
    IReadOnlyList<string> AlreadyInList,
    IReadOnlyList<string> SplitSeries,
    int UnusedPlaceholders)
{
    public bool HasWork => Relinks.Count > 0;
}

/// <summary>
/// The one-time clean-up for lists built before entries were re-matched after a scan (docs/superpowers/specs/2026-10-13-cbl-smart-match-
/// core-design.md §6, decisions Q2, Q10 and Q14). <see cref="Preview"/> changes nothing; <see cref="Apply"/> is a full
/// <see cref="ReadingListHealer.RecheckAll"/>, one transaction. A series row left with no issues afterwards shows up in Library Health's
/// existing Empty Rows queue, which already knows what else may refer to a row before removing it - rows are not deleted here.
/// </summary>
public static class ReadingListRepair
{
    public static RepairPreview Preview(PaperbunkrDbContext context)
    {
        var library = LibraryIndex.Load(context);
        var waiting = context.ReadingListItemSources.AsNoTracking()
            .Where(s => s.Method != MatchMethod.User && s.Item!.Issue!.IsPlaceholder)
            .Select(s => new { Source = s, s.Item!.ReadingListId, s.Item.SortOrder, ListName = s.Item.ReadingList!.Name })
            .AsEnumerable()
            .OrderBy(s => s.ListName, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.ReadingListId).ThenBy(s => s.SortOrder)
            .ToList();

        var listIds = waiting.Select(w => w.ReadingListId).Distinct().ToList();
        var inList = context.ReadingListItems.AsNoTracking()
            .Where(i => listIds.Contains(i.ReadingListId))
            .Select(i => new { i.ReadingListId, i.IssueId })
            .AsEnumerable()
            .GroupBy(i => i.ReadingListId)
            .ToDictionary(g => g.Key, g => g.Select(i => i.IssueId).ToHashSet());

        var relinks = new List<RepairRelink>();
        var collisions = new List<string>();
        int needChoice = 0, missing = 0;
        foreach (var entry in waiting)
        {
            var reference = ReadingListResolver.ToReference(entry.Source);
            var match = MatchEngine.Match(reference, library);
            if (match.AutoLink is not { } comic)
            {
                _ = match.NeedsChoice ? needChoice++ : missing++;
            }
            else if (!inList[entry.ReadingListId].Add(comic.Id))
            {
                collisions.Add($"{entry.ListName}: {reference.Display}");
            }
            else
            {
                string found = comic.Year is int year ? $"{comic.SeriesName} #{comic.Number} ({year})" : $"{comic.SeriesName} #{comic.Number}";
                relinks.Add(new RepairRelink(entry.ReadingListId, entry.ListName, reference.Display, found, match.Candidates[0].Summary));
            }
        }

        var owningRows = context.Issues.AsNoTracking()
            .Where(i => !i.IsPlaceholder && i.Series!.RemoteSourceId == null)
            .Select(i => new { i.SeriesId, i.Series!.Name })
            .Distinct()
            .AsEnumerable()
            .GroupBy(s => SeriesKeys.Of(s.Name))
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(" · ", g.OrderBy(s => s.SeriesId).Select(s => s.Name)))
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        int unused = context.Issues.Count(i => i.IsPlaceholder && !context.ReadingListItems.Any(item => item.IssueId == i.Id));
        return new RepairPreview(relinks, needChoice, missing, collisions, owningRows, unused);
    }

    public static HealResult Apply(PaperbunkrDbContext context) => ReadingListHealer.RecheckAll(context);
}
