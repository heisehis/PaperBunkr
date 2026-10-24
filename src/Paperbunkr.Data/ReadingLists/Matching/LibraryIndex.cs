using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>One comic in the library as the matcher sees it. Number, volume and year are the effective values (an accepted proposal counts).</summary>
public sealed record LibraryIssue(
    int Id,
    int SeriesId,
    string SeriesName,
    string SeriesKey,
    string? Number,
    string NumberKey,
    string? Volume,
    int? Year,
    string? Format,
    bool FileIsMissing,
    int PageCount,
    long FileSize);

/// <summary>
/// Everything the matcher needs from the library, read once per import or recheck (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-
/// design.md §5.4, decision D2) instead of a full series-table load per entry. Holds every real (non-placeholder) issue, indexed by provider
/// id and by series key. Provider ids come from <see cref="ComicMetadataExternalId"/>, which the scraper and the embedded-metadata reader
/// already maintain.
/// </summary>
public sealed class LibraryIndex
{
    private readonly Dictionary<(ComicProvider Provider, string Id), List<LibraryIssue>> _byProviderId = new();
    private readonly Dictionary<string, List<LibraryIssue>> _bySeriesKey = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LibraryIssue> _byId = new();

    public LibraryIndex(IEnumerable<LibraryIssue> issues, IEnumerable<(int IssueId, ComicProvider Provider, string ExternalId)>? providerIds = null,
        IEnumerable<(int SeriesId, string Title)>? alternateTitles = null)
    {
        var bySeries = new Dictionary<int, List<LibraryIssue>>();
        foreach (var issue in issues)
        {
            _byId[issue.Id] = issue;
            Add(_bySeriesKey, issue.SeriesKey, issue);
            Add(bySeries, issue.SeriesId, issue);
        }

        // A series' alternate titles answer for it too: every issue of that series is reachable under each title's key.
        foreach (var (seriesId, title) in alternateTitles ?? [])
        {
            string key = SeriesKeys.Of(title);
            if (key.Length == 0 || !bySeries.TryGetValue(seriesId, out var seriesIssues) || seriesIssues[0].SeriesKey == key)
            {
                continue;
            }

            foreach (var issue in seriesIssues)
            {
                Add(_bySeriesKey, key, issue);
            }
        }

        foreach (var (issueId, provider, externalId) in providerIds ?? [])
        {
            if (!string.IsNullOrWhiteSpace(externalId) && _byId.TryGetValue(issueId, out var issue))
            {
                Add(_byProviderId, (provider, externalId.Trim()), issue);
            }
        }
    }

    public int Count => _byId.Count;

    public LibraryIssue? Find(int issueId) => _byId.GetValueOrDefault(issueId);

    public IReadOnlyList<LibraryIssue> WithProviderId(ComicProvider provider, string? externalId) =>
        !string.IsNullOrWhiteSpace(externalId) && _byProviderId.TryGetValue((provider, externalId.Trim()), out var found) ? found : [];

    /// <summary>Issues of every series row whose name (or alternate title) has this key - all rows, not the first one found.</summary>
    public IReadOnlyList<LibraryIssue> InSeries(string seriesKey) =>
        _bySeriesKey.TryGetValue(seriesKey, out var found) ? found : [];

    public static LibraryIndex Load(PaperbunkrDbContext context)
    {
        // Latest accepted proposal per issue and field: the same overlay IssueMetadataExtensions.Effective* applies, read in one query.
        var accepted = new Dictionary<(int IssueId, MetadataProposalField Field), string?>();
        var fields = new[] { MetadataProposalField.Number, MetadataProposalField.Volume, MetadataProposalField.Year };
        foreach (var p in context.MetadataProposals.AsNoTracking()
                     .Where(p => p.IssueId != null && p.Status == MetadataProposalStatus.Accepted && fields.Contains(p.Field))
                     .OrderBy(p => p.CreatedAt)
                     .Select(p => new { IssueId = p.IssueId!.Value, p.Field, p.ProposedValue }))
        {
            accepted[(p.IssueId, p.Field)] = p.ProposedValue;
        }

        var issues = context.Issues.AsNoTracking()
            .Where(i => !i.IsPlaceholder)
            .Select(i => new { i.Id, i.SeriesId, SeriesName = i.Series!.Name, i.Number, i.Volume, i.Year, i.Format, i.FileIsMissing, i.PageCount, i.FileSize })
            .AsEnumerable()
            .Select(i =>
            {
                string? number = i.Number ?? accepted.GetValueOrDefault((i.Id, MetadataProposalField.Number));
                string? volume = i.Volume ?? accepted.GetValueOrDefault((i.Id, MetadataProposalField.Volume));
                int? year = i.Year ?? (int.TryParse(accepted.GetValueOrDefault((i.Id, MetadataProposalField.Year)), out int y) ? y : null);
                return new LibraryIssue(i.Id, i.SeriesId, i.SeriesName, SeriesKeys.Of(i.SeriesName), number, IssueNumberKey.Of(number), volume, year,
                    i.Format, i.FileIsMissing, i.PageCount ?? 0, i.FileSize ?? 0);
            })
            .ToList();

        var providerIds = context.ComicMetadataExternalIds.AsNoTracking()
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Issue)
            .Select(e => new { e.EntityId, e.Provider, e.ExternalId })
            .AsEnumerable()
            .Select(e => (e.EntityId, e.Provider, e.ExternalId));

        var titles = context.SeriesTitles.AsNoTracking()
            .Select(t => new { t.SeriesId, t.Value })
            .AsEnumerable()
            .Select(t => (t.SeriesId, t.Value));

        return new LibraryIndex(issues, providerIds, titles);
    }

    private static void Add<TKey>(Dictionary<TKey, List<LibraryIssue>> map, TKey key, LibraryIssue issue)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = new List<LibraryIssue>();
        }

        if (!list.Contains(issue))
        {
            list.Add(issue);
        }
    }
}
