using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using System.Text;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>Idempotently updates only app-generated Reading List tags.</summary>
public static class ReadingListTagReconciler
{
    public static int Reconcile(PaperbunkrDbContext context, IEnumerable<int> readingListIds)
    {
        int changed = 0;
        foreach (int listId in readingListIds.Distinct())
        {
            var list = context.ReadingLists
                .Include(l => l.StoryEvent)
                .Include(l => l.Continuity)
                .Include(l => l.Items).ThenInclude(item => item.Issue).ThenInclude(issue => issue!.Tags)
                .Include(l => l.Tags)
                .FirstOrDefault(l => l.Id == listId);
            if (list is null)
            {
                continue;
            }

            var desired = ReadingListTagGenerator.Generate(list);
            var generated = list.Tags.Where(tag => tag.Origin == ReadingListTagOrigin.Generated)
                .ToDictionary(tag => tag.GeneratedKey ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var userTags = list.Tags.Where(tag => tag.Origin == ReadingListTagOrigin.User).ToList();

            foreach (var wanted in desired)
            {
                if (userTags.Any(tag => SameTag(tag.Value, tag.Category, wanted.Value, wanted.Category)))
                {
                    if (generated.Remove(wanted.Key, out var redundant))
                    {
                        context.ReadingListTags.Remove(redundant);
                        changed++;
                    }

                    continue;
                }

                if (generated.Remove(wanted.Key, out var existing))
                {
                    if (existing.Value != wanted.Value || existing.Category != wanted.Category || existing.Weight != IssueTagWeight.Unset)
                    {
                        existing.Value = wanted.Value;
                        existing.Category = EmptyAsNull(wanted.Category);
                        existing.Weight = IssueTagWeight.Unset;
                        changed++;
                    }
                }
                else
                {
                    context.ReadingListTags.Add(new ReadingListTag
                    {
                        ReadingListId = listId,
                        Value = wanted.Value,
                        Category = EmptyAsNull(wanted.Category),
                        Weight = IssueTagWeight.Unset,
                        Origin = ReadingListTagOrigin.Generated,
                        GeneratedKey = wanted.Key,
                    });
                    changed++;
                }
            }

            foreach (var obsolete in generated.Values)
            {
                context.ReadingListTags.Remove(obsolete);
                changed++;
            }
        }

        if (changed > 0)
        {
            context.SaveChanges();
        }

        return changed;
    }

    /// <summary>Runs a resumable, bounded local reconciliation pass. Safe to repeat after interruption.</summary>
    public static int ReconcileAll(Func<PaperbunkrDbContext> contextFactory, int batchSize = 100)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        int total = 0;
        int lastId = 0;
        while (true)
        {
            int[] ids;
            using (var context = contextFactory())
            {
                ids = context.ReadingLists.AsNoTracking().Where(list => list.Id > lastId)
                    .OrderBy(list => list.Id).Select(list => list.Id).Take(batchSize).ToArray();
            }

            if (ids.Length == 0)
            {
                return total;
            }

            using (var context = contextFactory())
            {
                total += Reconcile(context, ids);
            }

            lastId = ids[^1];
        }
    }

    public static bool SameTag(string? leftValue, string? leftCategory, string? rightValue, string? rightCategory) =>
        string.Equals(Normalize(leftValue), Normalize(rightValue), StringComparison.OrdinalIgnoreCase)
        && string.Equals(Normalize(leftCategory), Normalize(rightCategory), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) => string.Join(' ', (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static string? EmptyAsNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
