using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Sharing.Protocol;

namespace Paperbunkr.Data.Sharing;

/// <summary>What one sync changed - for the Activity Center summary and for tests.</summary>
/// <param name="PageCountChangedRemoteIds">Host ids of existing issues whose content stamp (or, without one, page count) differs from what was mirrored - the book behind them was replaced, so anything cached for them is stale.</param>
public sealed record MirrorSyncResult(
    int SeriesAdded, int SeriesUpdated, int SeriesRemoved,
    int IssuesAdded, int IssuesUpdated, int IssuesRemoved,
    IReadOnlyList<int>? PageCountChangedRemoteIds = null)
{
    public IReadOnlyList<int> ChangedContent => PageCountChangedRemoteIds ?? Array.Empty<int>();
}

/// <summary>
/// Upserts a pulled catalog into the local database as mirror rows (docs/superpowers/specs/
/// 2026-09-19-remote-library-sharing-design.md §7.1). Rows are keyed by
/// <c>(RemoteSourceId, RemoteIssueId/RemoteSeriesId)</c>; a re-sync updates catalog-sourced fields in
/// place and never touches client-local state (last page read, open count/time, rating, review, notes,
/// checked flag, bookmarks) - that is what makes "progress is client-local" true across refreshes.
/// Mirror rows always keep <see cref="Issue.FilePath"/> null.
/// </summary>
public static class RemoteMirrorSync
{
    /// <param name="context">Must be created with <see cref="PaperbunkrDbContext.IncludeRemote"/> = true, or the existing mirror is invisible and every sync would try to re-insert it.</param>
    public static MirrorSyncResult Apply(
        PaperbunkrDbContext context,
        int sourceId,
        IReadOnlyList<CatalogSeriesDto> catalogSeries,
        IReadOnlyList<CatalogIssueDto> catalogIssues)
    {
        if (!context.IncludeRemote)
        {
            throw new InvalidOperationException("RemoteMirrorSync needs a context created with IncludeRemote = true.");
        }

        using var transaction = context.Database.BeginTransaction();

        Dictionary<int, Series> existingSeries = context.Series
            .Where(s => s.RemoteSourceId == sourceId && s.RemoteSeriesId != null)
            .ToDictionary(s => s.RemoteSeriesId!.Value);
        Dictionary<int, Issue> existingIssues = context.Issues
            .Include(i => i.Tags)
            .Where(i => i.RemoteSourceId == sourceId && i.RemoteIssueId != null)
            .ToDictionary(i => i.RemoteIssueId!.Value);

        int seriesAdded = 0, seriesUpdated = 0, issuesAdded = 0, issuesUpdated = 0;

        // --- series first, saved, so issues can point at real local ids ---
        foreach (CatalogSeriesDto dto in catalogSeries)
        {
            if (existingSeries.TryGetValue(dto.Id, out Series? series))
            {
                Map(dto, series);
                seriesUpdated++;
            }
            else
            {
                series = new Series { RemoteSourceId = sourceId, RemoteSeriesId = dto.Id };
                Map(dto, series);
                context.Series.Add(series);
                existingSeries[dto.Id] = series;
                seriesAdded++;
            }
        }

        context.SaveChanges();

        // --- issues ---
        var keptIssueIds = new HashSet<int>();
        var pageCountChanged = new List<int>();
        foreach (CatalogIssueDto dto in catalogIssues)
        {
            if (!existingSeries.TryGetValue(dto.SeriesId, out Series? owner))
            {
                continue; // a catalog issue whose series wasn't sent: nothing sensible to attach it to
            }

            keptIssueIds.Add(dto.Id);
            if (existingIssues.TryGetValue(dto.Id, out Issue? issue))
            {
                // The book behind this id was replaced if the host's content stamp changed, or - for a host that sends no stamp - if its
                // page count did. A first stamp (null -> value) is just learning the identity, not a change.
                bool replaced = issue.RemoteContentStamp is not null && dto.ContentStamp is not null
                    ? issue.RemoteContentStamp != dto.ContentStamp
                    : issue.PageCount is > 0 && dto.PageCount is > 0 && issue.PageCount != dto.PageCount;
                if (replaced)
                {
                    pageCountChanged.Add(dto.Id);
                }

                Map(dto, issue, owner.Id);
                issuesUpdated++;
            }
            else
            {
                issue = new Issue
                {
                    RemoteSourceId = sourceId,
                    RemoteIssueId = dto.Id,
                    AddedTime = DateTime.UtcNow,
                };
                Map(dto, issue, owner.Id);
                context.Issues.Add(issue);
                existingIssues[dto.Id] = issue;
                issuesAdded++;
            }

            SyncTags(context, issue, dto.Tags);
        }

        // --- rows the host no longer shares ---
        List<Issue> goneIssues = existingIssues.Values.Where(i => i.RemoteIssueId is int id && !keptIssueIds.Contains(id)).ToList();

        // Series.CoverIssueId -> Issue is Restrict (deleting a cover issue must not cascade into its
        // series), so any series whose cover is about to disappear has to let go of it first.
        var goneIssueIds = goneIssues.Select(i => i.Id).ToHashSet();
        foreach (Series s in existingSeries.Values)
        {
            if (s.CoverIssueId is int coverId && goneIssueIds.Contains(coverId))
            {
                s.CoverIssueId = null;
            }
        }

        context.Issues.RemoveRange(goneIssues);
        int issuesRemoved = goneIssues.Count;
        context.SaveChanges();

        var liveSeriesIds = context.Issues.Where(i => i.RemoteSourceId == sourceId).Select(i => i.SeriesId).Distinct().ToHashSet();
        List<Series> goneSeries = existingSeries.Values
            .Where(s => s.Id != 0 && !liveSeriesIds.Contains(s.Id))
            .ToList();
        context.Series.RemoveRange(goneSeries);
        int seriesRemoved = goneSeries.Count;

        // Every surviving mirror series shows its first issue as its cover.
        Dictionary<int, int> firstIssueBySeries = context.Issues
            .Where(i => i.RemoteSourceId == sourceId)
            .AsEnumerable()
            .GroupBy(i => i.SeriesId)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.RemoteIssueId).First().Id);
        foreach (Series s in existingSeries.Values.Except(goneSeries))
        {
            if (s.Id != 0 && firstIssueBySeries.TryGetValue(s.Id, out int coverId) && s.CoverIssueId != coverId)
            {
                s.CoverIssueId = coverId;
            }
        }

        context.SaveChanges();
        transaction.Commit();

        return new MirrorSyncResult(seriesAdded, seriesUpdated, seriesRemoved, issuesAdded, issuesUpdated, issuesRemoved, pageCountChanged);
    }

    private static void Map(CatalogSeriesDto dto, Series s)
    {
        s.Name = dto.Name;
        s.SortName = dto.SortName;
        s.ContentType = ParseOr(dto.ContentType, ContentType.Unknown);
        s.ReadingMode = ParseOr(dto.ReadingMode, ReadingMode.LeftToRight);
        s.Status = ParseOr(dto.Status, SeriesStatus.Unknown);
        s.Publisher = dto.Publisher;
        s.Genre = dto.Genre;
        s.Summary = dto.Summary;
        s.Creator = dto.Creator;
    }

    private static void Map(CatalogIssueDto d, Issue i, int localSeriesId)
    {
        i.SeriesId = localSeriesId;
        i.Title = d.Title;
        i.Number = d.Number;
        i.Count = d.Count;
        i.Volume = d.Volume;
        i.AlternateSeries = d.AlternateSeries;
        i.AlternateNumber = d.AlternateNumber;
        i.AlternateCount = d.AlternateCount;
        i.StoryArc = d.StoryArc;
        i.StoryArcNumber = d.StoryArcNumber;
        i.SeriesGroup = d.SeriesGroup;
        i.IsFinalIssue = d.IsFinalIssue;
        i.Summary = d.Summary;
        i.Year = d.Year;
        i.Month = d.Month;
        i.Day = d.Day;
        i.Writer = d.Writer;
        i.Penciller = d.Penciller;
        i.Inker = d.Inker;
        i.Colorist = d.Colorist;
        i.Letterer = d.Letterer;
        i.CoverArtist = d.CoverArtist;
        i.Editor = d.Editor;
        i.Translator = d.Translator;
        i.Publisher = d.Publisher;
        i.Imprint = d.Imprint;
        i.Web = d.Web;
        i.PageCount = d.PageCount;
        i.LanguageISO = d.LanguageISO;
        i.Format = d.Format;
        i.AgeRating = d.AgeRating;
        i.Characters = d.Characters;
        i.Teams = d.Teams;
        i.Locations = d.Locations;
        i.MainCharacterOrTeam = d.MainCharacterOrTeam;
        i.CommunityRating = d.CommunityRating;
        i.ISBN = d.ISBN;
        i.ColorMode = ParseOr(d.ColorMode, ColorMode.Unknown);
        i.ReleasedTime = d.ReleasedTime;
        i.CoverAspectRatio = d.CoverAspectRatio;
        i.RemoteContentStamp = d.ContentStamp;
        i.FilePath = null; // a mirror row never points at a local file
    }

    /// <summary>Makes <paramref name="issue"/>'s tags equal the catalog's: removes the dropped, updates category/weight of the kept, adds the new.</summary>
    private static void SyncTags(PaperbunkrDbContext context, Issue issue, IReadOnlyList<IssueTagDto> incoming)
    {
        var wanted = new Dictionary<(IssueTagField, string), IssueTagDto>();
        foreach (IssueTagDto t in incoming)
        {
            if (Enum.TryParse(t.Field, out IssueTagField field) && !string.IsNullOrWhiteSpace(t.Value))
            {
                wanted[(field, t.Value)] = t;
            }
        }

        foreach (IssueTag existing in issue.Tags.ToList())
        {
            if (!wanted.ContainsKey((existing.Field, existing.Value)))
            {
                issue.Tags.Remove(existing);
                if (existing.Id != 0)
                {
                    context.IssueTags.Remove(existing);
                }
            }
        }

        foreach (((IssueTagField field, string value), IssueTagDto dto) in wanted)
        {
            IssueTag? tag = issue.Tags.FirstOrDefault(t => t.Field == field && t.Value == value);
            if (tag is null)
            {
                tag = new IssueTag { Field = field, Value = value };
                issue.Tags.Add(tag);
            }

            tag.Category = dto.Category;
            tag.Weight = ParseOr(dto.Weight, IssueTagWeight.Unset);
        }
    }

    private static T ParseOr<T>(string? name, T fallback) where T : struct, Enum =>
        Enum.TryParse(name, ignoreCase: true, out T value) && Enum.IsDefined(value) ? value : fallback;
}
