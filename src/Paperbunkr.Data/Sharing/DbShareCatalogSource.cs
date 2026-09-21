using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;
using Paperbunkr.Sharing;
using Paperbunkr.Sharing.Protocol;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.Data.Sharing;

/// <summary>
/// The host's <see cref="IShareCatalogSource"/> over the EF context (docs/superpowers/specs/
/// 2026-09-19-remote-library-sharing-design.md §4/§5). It resolves the host's <see cref="ShareScope"/>
/// into one immutable snapshot - the in-scope issues, their series, the shared lists, and a content
/// hash used as the catalog <c>ETag</c> - and pages that snapshot by issue id.
/// </summary>
/// <remarks>
/// <para>Only issues that are actually servable are ever candidates: a real <see cref="Issue.FilePath"/>,
/// not a reading-list placeholder, not flagged missing. That test also keeps a future mirror of
/// another host's library (whose rows carry no local path) from being re-shared. Every page/cover
/// request is re-checked against the snapshot via <see cref="IsIssueSharedAsync"/>.</para>
/// <para>The snapshot is cached briefly so a multi-page pull is consistent and cheap, and rebuilt
/// after <see cref="Invalidate"/> (call it when the scope changes) or when the TTL lapses. The scope
/// is read through a delegate on every rebuild, so narrowing it takes effect within one TTL at most.</para>
/// </remarks>
public sealed class DbShareCatalogSource : IShareCatalogSource
{
    public const string ReadingListKind = "ReadingList";
    public const string CollectionKind = "Collection";
    public const string SmartListKind = "SmartList";

    private sealed record Snapshot(
        string Version,
        IReadOnlyList<CatalogIssueDto> Issues,
        IReadOnlyDictionary<int, CatalogSeriesDto> Series,
        IReadOnlyList<SharedListDto> Lists,
        HashSet<int> IssueIds,
        DateTimeOffset BuiltAt);

    private static readonly JsonSerializerOptions HashJson = new(JsonSerializerDefaults.Web);

    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Func<ShareScope> _scope;
    private readonly TimeProvider _time;
    private readonly TimeSpan _ttl;
    private readonly object _gate = new();

    private Snapshot? _snapshot;

    public DbShareCatalogSource(
        Func<PaperbunkrDbContext> contextFactory,
        Func<ShareScope> scope,
        TimeProvider? time = null,
        TimeSpan? snapshotTtl = null)
    {
        _contextFactory = contextFactory;
        _scope = scope;
        _time = time ?? TimeProvider.System;
        _ttl = snapshotTtl ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>Drops the cached snapshot so the next request re-resolves the scope.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _snapshot = null;
        }
    }

    public Task<string> GetCatalogVersionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(GetSnapshot().Version);

    public Task<IReadOnlyList<SharedListDto>> GetListsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(GetSnapshot().Lists);

    public Task<bool> IsIssueSharedAsync(int issueId, CancellationToken cancellationToken) =>
        Task.FromResult(GetSnapshot().IssueIds.Contains(issueId));

    public Task<CatalogPage> GetCatalogPageAsync(string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        Snapshot snapshot = GetSnapshot();
        int afterId = ParseCursor(cursor);

        // Issues are ordered by id, so "after the last id I saw" is a stable, gap-free cursor.
        var page = snapshot.Issues.Where(i => i.Id > afterId).Take(pageSize + 1).ToList();
        bool hasMore = page.Count > pageSize;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var series = page.Select(i => i.SeriesId).Distinct()
            .Where(snapshot.Series.ContainsKey)
            .Select(id => snapshot.Series[id])
            .ToList();

        string? next = hasMore ? $"after:{page[^1].Id}" : null;
        return Task.FromResult(new CatalogPage(snapshot.Version, series, page, next));
    }

    private static int ParseCursor(string? cursor)
    {
        const string prefix = "after:";
        return cursor is not null && cursor.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(cursor.AsSpan(prefix.Length), out int id) ? id : 0;
    }

    private Snapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_snapshot is null || _time.GetUtcNow() - _snapshot.BuiltAt > _ttl)
            {
                _snapshot = Build();
            }

            return _snapshot;
        }
    }

    private Snapshot Build()
    {
        ShareScope scope = _scope();
        DateTimeOffset builtAt = _time.GetUtcNow();

        if (scope.Mode == ShareMode.None)
        {
            return new Snapshot("empty", Array.Empty<CatalogIssueDto>(), new Dictionary<int, CatalogSeriesDto>(),
                Array.Empty<SharedListDto>(), new HashSet<int>(), builtAt);
        }

        using PaperbunkrDbContext context = _contextFactory();

        List<Issue> issues = context.Issues.AsNoTracking()
            .Include(i => i.Tags)
            .Where(i => i.FilePath != null && !i.IsPlaceholder && !i.FileIsMissing)
            .OrderBy(i => i.Id)
            .ToList();

        var lists = new List<SharedListDto>();
        if (scope.Mode == ShareMode.Selected)
        {
            HashSet<int> allowed = ResolveSelectedIssueIds(context, scope, lists);
            issues = issues.Where(i => allowed.Contains(i.Id)).ToList();
        }
        else
        {
            lists.AddRange(context.ReadingLists.AsNoTracking().OrderBy(l => l.Name)
                .Select(l => new SharedListDto(l.Id, l.Name, ReadingListKind)));
            lists.AddRange(context.Collections.AsNoTracking().OrderBy(c => c.Name)
                .Select(c => new SharedListDto(c.Id, c.Name, CollectionKind)));
        }

        HashSet<int> seriesIds = issues.Select(i => i.SeriesId).ToHashSet();
        Dictionary<int, CatalogSeriesDto> series = context.Series.AsNoTracking()
            .Where(s => seriesIds.Contains(s.Id))
            .AsEnumerable()
            .ToDictionary(s => s.Id, ToDto);

        List<CatalogIssueDto> issueDtos = issues.Select(ToDto).ToList();

        return new Snapshot(
            ComputeVersion(issueDtos, series.Values, lists),
            issueDtos,
            series,
            lists,
            issueDtos.Select(i => i.Id).ToHashSet(),
            builtAt);
    }

    /// <summary>Union of every issue reachable from the selected reading lists, collections and (issue-target) smart lists; fills <paramref name="lists"/> with the ones that still exist.</summary>
    private static HashSet<int> ResolveSelectedIssueIds(PaperbunkrDbContext context, ShareScope scope, List<SharedListDto> lists)
    {
        var ids = new HashSet<int>();

        foreach (ReadingList list in context.ReadingLists.AsNoTracking().Where(l => scope.ReadingListIds.Contains(l.Id)).OrderBy(l => l.Name))
        {
            lists.Add(new SharedListDto(list.Id, list.Name, ReadingListKind));
            ids.UnionWith(context.ReadingListItems.AsNoTracking().Where(x => x.ReadingListId == list.Id).Select(x => x.IssueId));
        }

        foreach (Collection collection in context.Collections.AsNoTracking().Where(c => scope.CollectionIds.Contains(c.Id)).OrderBy(c => c.Name))
        {
            lists.Add(new SharedListDto(collection.Id, collection.Name, CollectionKind));
            foreach (CollectionMember member in CollectionResolver.GetMembers(context, collection.Id))
            {
                if (member.Kind == CollectionMemberKind.Issue && member.Issue is not null)
                {
                    ids.Add(member.Issue.Id);
                }
                else if (member.Kind == CollectionMemberKind.Series && member.Series is not null)
                {
                    int seriesId = member.Series.Id;
                    ids.UnionWith(context.Issues.AsNoTracking().Where(i => i.SeriesId == seriesId).Select(i => i.Id));
                }
            }
        }

        foreach (int smartListId in scope.SmartListIds)
        {
            SmartList? smart = SmartListTreeLoader.LoadWithTree(context, smartListId);
            if (smart is null || smart.TargetKind != SmartListTargetKind.Issue)
            {
                continue;
            }

            lists.Add(new SharedListDto(smart.Id, smart.Name, SmartListKind));
            ids.UnionWith(SmartListQueryBuilder.Build(context, smart).Select(i => i.Id));
        }

        return ids;
    }

    private static string ComputeVersion(IReadOnlyList<CatalogIssueDto> issues, IEnumerable<CatalogSeriesDto> series, IReadOnlyList<SharedListDto> lists)
    {
        // Hashes exactly what a client would see, so any visible change - a metadata edit, a tag, a
        // scope change - produces a new version, and anything invisible (read progress, ratings) doesn't.
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new { issues, series = series.OrderBy(s => s.Id), lists }, HashJson);
        return Convert.ToHexString(SHA256.HashData(payload))[..16].ToLowerInvariant();
    }

    private static CatalogSeriesDto ToDto(Series s) => new(
        s.Id, s.Name, s.SortName, s.ContentType.ToString(), s.ReadingMode.ToString(), s.Status.ToString(),
        s.Publisher, s.Genre, s.Summary, s.Creator);

    private static CatalogIssueDto ToDto(Issue i) => new(
        i.Id, i.SeriesId, i.Title, i.Number, i.Count, i.Volume, i.AlternateSeries, i.AlternateNumber, i.AlternateCount,
        i.StoryArc, i.StoryArcNumber, i.SeriesGroup, i.IsFinalIssue, i.Summary, i.Year, i.Month, i.Day,
        i.Writer, i.Penciller, i.Inker, i.Colorist, i.Letterer, i.CoverArtist, i.Editor, i.Translator,
        i.Publisher, i.Imprint, i.Web, i.PageCount, i.LanguageISO, i.Format, i.AgeRating,
        i.Characters, i.Teams, i.Locations, i.MainCharacterOrTeam, i.CommunityRating, i.ISBN,
        i.ColorMode.ToString(), i.ReleasedTime, i.CoverAspectRatio,
        i.Tags.OrderBy(t => t.Id)
            .Select(t => new IssueTagDto(t.Field.ToString(), t.Category ?? "Uncategorized", t.Weight.ToString(), t.Value))
            .ToList(),
        ContentStamp(i));

    /// <summary>
    /// An opaque identity of what is behind this issue: a short hash of its file size, modified time and page count, so a client can
    /// tell "the host replaced this book" from "same book" without the host disclosing any of those values or a path. Null when the
    /// host has no file facts for it.
    /// </summary>
    public static string? ContentStamp(Issue i)
    {
        if (i.FileSize is null && i.FileModifiedTime is null)
        {
            return null;
        }

        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{i.FileSize}|{i.FileModifiedTime?.Ticks}|{i.PageCount}"));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
