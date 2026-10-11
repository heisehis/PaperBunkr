using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>
/// Refreshes one series' data from the metadata providers it is linked to (docs/superpowers/specs/2026-10-06-smart-features-design.md
/// §7.3): the same relations refresh the Detail screen's Related tab runs, for every link whose provider supports it. Used when a
/// linked series is marked completed. Cover candidates are not part of it - they are fetched on demand by the cover picker and have
/// no per-series store to refresh.
/// </summary>
public static class SeriesProviderRefresh
{
    /// <summary>The provider client for a link, or null for a provider with no metadata client (a tracker-only link).</summary>
    private static IMetadataProvider? ProviderFor(ExternalMetadataProvider provider) => provider switch
    {
        ExternalMetadataProvider.AniList => new AniListMetadataProvider(AniListHttpClient.Shared),
        ExternalMetadataProvider.MangaBaka => MangaBakaMetadataProvider.Shared,
        ExternalMetadataProvider.MangaDex => MangaDexMetadataProvider.Shared,
        _ => null,
    };

    /// <summary>True when <paramref name="seriesId"/> has at least one link this can refresh - nothing is fetched for a series without one.</summary>
    public static bool CanRefresh(PaperbunkrDbContext context, int seriesId) =>
        ExternalMetadataResolver.GetExternalIds(context, seriesId).Any(link => ProviderFor(link.Provider) is not null);

    public static async Task<string> RefreshAsync(Func<PaperbunkrDbContext> contextFactory, int seriesId, CancellationToken cancellationToken)
    {
        using var context = contextFactory();
        int refreshed = 0;
        foreach (var link in ExternalMetadataResolver.GetExternalIds(context, seriesId))
        {
            if (ProviderFor(link.Provider) is not { } provider)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await MetadataLinkResolver.RefreshRelationsAsync(provider, context, seriesId, link.ExternalId, cancellationToken).ConfigureAwait(false);
            refreshed++;
        }

        return refreshed == 1 ? "Refreshed from 1 provider." : $"Refreshed from {refreshed} providers.";
    }
}
