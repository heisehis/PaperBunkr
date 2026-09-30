using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// What <see cref="StoryEventIdCompletion"/> asks one comic provider (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md
/// §3). A seam so tests run against fakes - no live network in the suite.
/// </summary>
public interface IArcIdentitySource
{
    ComicProvider Provider { get; }

    /// <summary>The story arcs (with this provider's arc ids) an issue is credited to, by this provider's issue id.</summary>
    Task<IReadOnlyList<ComicVineIdName>> GetIssueArcsAsync(int providerIssueId, CancellationToken cancellationToken);

    /// <summary>Metron only: the ComicVine id (<c>cv_id</c>) Metron records for one of its arcs. Null from ComicVine, or when Metron has none.</summary>
    Task<string?> GetComicVineIdForArcAsync(string arcId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ArcSearchResult>> SearchArcsAsync(string query, CancellationToken cancellationToken);

    Task<IReadOnlyList<ArcIssue>> GetArcIssuesAsync(string arcId, CancellationToken cancellationToken);
}

/// <summary>The production <see cref="IArcIdentitySource"/>: issue details from the scraper's client, arc search/lists from the CBL arc source.</summary>
public sealed class ProviderArcIdentitySource : IArcIdentitySource
{
    private readonly IComicVineIssueDetailsSource _issues;
    private readonly IReadingListSource _arcs;

    public ProviderArcIdentitySource(ComicProvider provider, IComicVineIssueDetailsSource issues, IReadingListSource arcs)
    {
        Provider = provider;
        _issues = issues;
        _arcs = arcs;
    }

    public ComicProvider Provider { get; }

    /// <summary>A source for every provider whose credentials are saved (background priority, so interactive lookups go first).</summary>
    public static IReadOnlyDictionary<ComicProvider, IArcIdentitySource> CreateAvailable(PaperbunkrDbContext context)
    {
        var sources = new Dictionary<ComicProvider, IArcIdentitySource>();
        foreach (var provider in ComicProviderFactory.All)
        {
            var issues = ComicProviderFactory.Create(context, provider, ComicVineRequestPriority.Low);
            var arcs = ReadingListSourceRegistry.Get(context, provider == ComicProvider.Metron ? "Metron" : "ComicVine");
            if (issues is not null && arcs is not null)
            {
                sources[provider] = new ProviderArcIdentitySource(provider, issues, arcs);
            }
        }

        return sources;
    }

    public async Task<IReadOnlyList<ComicVineIdName>> GetIssueArcsAsync(int providerIssueId, CancellationToken cancellationToken)
    {
        var details = await _issues.GetIssueDetailsAsync(providerIssueId, cancellationToken).ConfigureAwait(false);
        return details?.StoryArcs ?? (IReadOnlyList<ComicVineIdName>)Array.Empty<ComicVineIdName>();
    }

    public async Task<string?> GetComicVineIdForArcAsync(string arcId, CancellationToken cancellationToken)
    {
        if (Provider != ComicProvider.Metron)
        {
            return null;
        }

        var overview = await _arcs.GetArcOverviewAsync(arcId, cancellationToken).ConfigureAwait(false);
        return overview?.ComicVineId;
    }

    public Task<IReadOnlyList<ArcSearchResult>> SearchArcsAsync(string query, CancellationToken cancellationToken) =>
        _arcs.SearchAsync(query, cancellationToken);

    public Task<IReadOnlyList<ArcIssue>> GetArcIssuesAsync(string arcId, CancellationToken cancellationToken) =>
        _arcs.GetArcIssuesInOrderAsync(arcId, cancellationToken);
}
