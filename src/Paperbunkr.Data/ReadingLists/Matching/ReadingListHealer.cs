using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>What a recheck did.</summary>
/// <param name="Relinked">Placeholder entries now pointing at the real comic.</param>
/// <param name="NeedChoice">Entries the library has candidates for, but not one the source singles out.</param>
/// <param name="StillMissing">Entries nothing in the library fits.</param>
/// <param name="AlreadyInList">Entries whose comic is already elsewhere in the same list, left alone, by name.</param>
public sealed record HealResult(int Relinked, int NeedChoice, int StillMissing, IReadOnlyList<string> AlreadyInList)
{
    public static HealResult Nothing { get; } = new(0, 0, 0, Array.Empty<string>());

    public string Summary
    {
        get
        {
            var parts = new List<string> { Relinked == 1 ? "1 entry linked" : $"{Relinked} entries linked" };
            if (NeedChoice > 0)
            {
                parts.Add(NeedChoice == 1 ? "1 needs a choice" : $"{NeedChoice} need a choice");
            }

            if (StillMissing > 0)
            {
                parts.Add($"{StillMissing} still missing");
            }

            if (AlreadyInList.Count > 0)
            {
                parts.Add($"{AlreadyInList.Count} already in the list under another entry");
            }

            return string.Join(", ", parts) + ".";
        }
    }
}

/// <summary>
/// Re-matches the entries that are still waiting for their comic (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.9,
/// decisions Q7-Q9). Before this existed a placeholder was only ever replaced by the acquisition daemon or a source refresh, so a comic
/// that arrived by an ordinary scan left its entries reading "missing" for good.
/// <para>It changes placeholders only, and only on a match the engine may link by itself (<see cref="MatchResult.AutoLink"/>). An entry
/// pointing at a real comic, and one the user linked, is never looked at. Each pass is one transaction.</para>
/// </summary>
public static class ReadingListHealer
{
    public static HealResult RecheckList(PaperbunkrDbContext context, int readingListId, LibraryIndex? library = null)
    {
        ReadingListSourceKeys.FillMissing(context);
        return Recheck(context, Waiting(context).Where(s => s.Item!.ReadingListId == readingListId), library);
    }

    public static HealResult RecheckAll(PaperbunkrDbContext context, LibraryIndex? library = null)
    {
        ReadingListSourceKeys.FillMissing(context);
        return Recheck(context, Waiting(context), library);
    }

    /// <summary>
    /// The pass after a scan: only entries that could be waiting for one of <paramref name="newIssueIds"/> are read, found through the
    /// source table's indexes (same series and number key, or a provider id the comic carries) instead of walking every list.
    /// </summary>
    public static HealResult RecheckFor(PaperbunkrDbContext context, IReadOnlyCollection<int> newIssueIds)
    {
        if (newIssueIds.Count == 0 || !Waiting(context).Any())
        {
            return HealResult.Nothing;
        }

        ReadingListSourceKeys.FillMissing(context);

        var library = LibraryIndex.Load(context);
        var arrived = newIssueIds.Select(library.Find).OfType<LibraryIssue>().ToList();
        var seriesKeys = arrived.Select(i => i.SeriesKey).Distinct().ToList();
        var numberKeys = arrived.Select(i => i.NumberKey).Distinct().ToList();
        var ids = context.ComicMetadataExternalIds.AsNoTracking()
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Issue && newIssueIds.Contains(e.EntityId))
            .Select(e => new { e.Provider, e.ExternalId })
            .ToList();
        var comicVineIds = ids.Where(e => e.Provider == ComicProvider.ComicVine).Select(e => e.ExternalId).ToList();
        var metronIds = ids.Where(e => e.Provider == ComicProvider.Metron).Select(e => e.ExternalId).ToList();

        var candidates = Waiting(context).Where(s =>
            (seriesKeys.Contains(s.SeriesKey) && numberKeys.Contains(s.NumberKey))
            || (s.ComicVineIssueId != null && comicVineIds.Contains(s.ComicVineIssueId))
            || (s.MetronIssueId != null && metronIds.Contains(s.MetronIssueId)));
        return Recheck(context, candidates, library);
    }

    /// <summary>Entries whose item still points at a placeholder and that the user has not settled by hand.</summary>
    private static IQueryable<ReadingListItemSource> Waiting(PaperbunkrDbContext context) =>
        context.ReadingListItemSources.Where(s => s.Method != MatchMethod.User && s.Item!.Issue!.IsPlaceholder);

    private static HealResult Recheck(PaperbunkrDbContext context, IQueryable<ReadingListItemSource> waiting, LibraryIndex? library)
    {
        var sources = waiting
            .Include(s => s.Item).ThenInclude(i => i!.Issue)
            .OrderBy(s => s.Item!.ReadingListId).ThenBy(s => s.Item!.SortOrder)
            .ToList();
        if (sources.Count == 0)
        {
            return HealResult.Nothing;
        }

        library ??= LibraryIndex.Load(context);
        var listIds = sources.Select(s => s.Item!.ReadingListId).Distinct().ToList();
        var inList = context.ReadingListItems.AsNoTracking()
            .Where(i => listIds.Contains(i.ReadingListId))
            .Select(i => new { i.ReadingListId, i.IssueId })
            .AsEnumerable()
            .GroupBy(i => i.ReadingListId)
            .ToDictionary(g => g.Key, g => g.Select(i => i.IssueId).ToHashSet());

        int relinked = 0, needChoice = 0, missing = 0;
        var collisions = new List<string>();
        var freed = new HashSet<int>();
        var now = DateTime.UtcNow;

        // Already inside someone else's transaction (a refresh, the repair): join it instead of opening a second one.
        using var transaction = context.Database.CurrentTransaction is null ? context.Database.BeginTransaction() : null;
        foreach (var source in sources)
        {
            var item = source.Item!;
            var match = MatchEngine.Match(ReadingListResolver.ToReference(source), library);
            source.LastCheckedUtc = now;
            if (match.AutoLink is not { } comic)
            {
                source.State = match.NeedsChoice ? MatchState.NeedsChoice : MatchState.Unresolved;
                _ = match.NeedsChoice ? needChoice++ : missing++;
                continue;
            }

            if (!inList[item.ReadingListId].Add(comic.Id))
            {
                // The list holds a comic once; which of the two entries should keep it is the user's call.
                source.State = MatchState.NeedsChoice;
                collisions.Add(ReadingListResolver.ToReference(source).Display);
                continue;
            }

            if (ReadingListItemLinker.Repoint(context, item, comic.Id, match.ById ? MatchMethod.ProviderId : MatchMethod.Metadata) is { IsPlaceholder: true } old)
            {
                freed.Add(old.Id);
            }

            relinked++;
        }

        context.SaveChanges();
        RemoveUnused(context, freed);
        transaction?.Commit();
        return new HealResult(relinked, needChoice, missing, collisions);
    }

    /// <summary>Deletes the placeholders in <paramref name="issueIds"/> that no reading-list entry points at any more.</summary>
    internal static int RemoveUnused(PaperbunkrDbContext context, IReadOnlyCollection<int> issueIds)
    {
        if (issueIds.Count == 0)
        {
            return 0;
        }

        int removed = 0;
        foreach (int issueId in issueIds.Distinct())
        {
            if (ReadingListItemLinker.DeleteIfUnreferencedPlaceholder(context, issueId))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            context.SaveChanges();
        }

        return removed;
    }
}
