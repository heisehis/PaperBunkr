using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.Data.Metadata;

/// <summary>What one <see cref="CommunityRatingBackfillService.RunAsync"/> pass did.</summary>
/// <param name="Stored">Community ratings written.</param>
/// <param name="NoScore">Series the provider had no score for (unrated there), so nothing was written.</param>
/// <param name="StoppedEarly">The provider rate-limited or went offline; what was fetched is kept and a later run continues.</param>
public sealed record CommunityBackfillResult(int Total, int Stored, int NoScore, bool StoppedEarly);

/// <summary>
/// Fills in the community ratings the "You vs Community" card compares against (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md
/// Part 2): for each series <b>you have rated</b> that is linked to AniList, MangaBaka or MangaDex but has no stored rating from that provider, fetch the
/// provider's score and keep it as an <see cref="ExternalRating"/>. One request per series, spaced by the providers' own rate limiters.
/// Comics are not fetched here: an issue doesn't remember its Metron issue id, so a comic's community rating arrives through the normal scrape
/// (<see cref="ScrapeField.CommunityRating"/>, on by default) rather than by id.
/// </summary>
public static class CommunityRatingBackfillService
{
    private static readonly ExternalMetadataProvider[] RatingProviders =
        { ExternalMetadataProvider.AniList, ExternalMetadataProvider.MangaBaka, ExternalMetadataProvider.MangaDex };

    /// <summary>The (series, provider, external id) triples a run would fetch: rated series with a link and no rating from that provider yet.</summary>
    public static IReadOnlyList<(int SeriesId, ExternalMetadataProvider Provider, string ExternalId)> Candidates(PaperbunkrDbContext context)
    {
        var ratedIds = context.Series.Where(s => s.Rating != null && s.Rating > 0).Select(s => s.Id).ToList().ToHashSet();
        if (ratedIds.Count == 0)
        {
            return Array.Empty<(int, ExternalMetadataProvider, string)>();
        }

        var have = context.ExternalRatings.Select(r => new { r.SeriesId, r.Provider }).ToList()
            .Select(r => (r.SeriesId, r.Provider)).ToHashSet();
        return context.ExternalMediaIds
            .Where(e => RatingProviders.Contains(e.Provider))
            .Select(e => new { e.SeriesId, e.Provider, e.ExternalId })
            .ToList()
            .Where(e => ratedIds.Contains(e.SeriesId) && !have.Contains((e.SeriesId, e.Provider)))
            .OrderBy(e => e.SeriesId)
            .Select(e => (e.SeriesId, e.Provider, e.ExternalId))
            .ToList();
    }

    /// <param name="providerFor">The fetcher for a provider, or null when the app has none (that link is skipped).</param>
    public static async Task<CommunityBackfillResult> RunAsync(
        Func<PaperbunkrDbContext> contextFactory,
        Func<ExternalMetadataProvider, IMetadataProvider?> providerFor,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<(int SeriesId, ExternalMetadataProvider Provider, string ExternalId)> candidates;
        using (var context = contextFactory())
        {
            candidates = Candidates(context).ToList();
        }

        int done = 0, stored = 0, noScore = 0;
        progress?.Report((0, candidates.Count));
        foreach (var (seriesId, providerKey, externalId) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = providerFor(providerKey);
            if (provider is not null)
            {
                ExternalMediaMetadata? metadata;
                try
                {
                    metadata = await provider.GetAsync(externalId, cancellationToken).ConfigureAwait(false);
                }
                catch (MetadataProviderUnavailableException)
                {
                    return new CommunityBackfillResult(candidates.Count, stored, noScore, StoppedEarly: true);
                }

                using var context = contextFactory();
                if (metadata is not null && MangaProviderApply.StoreRating(context, seriesId, metadata, providerKey))
                {
                    context.SaveChanges();
                    stored++;
                }
                else
                {
                    noScore++;
                }
            }

            progress?.Report((++done, candidates.Count));
        }

        return new CommunityBackfillResult(candidates.Count, stored, noScore, StoppedEarly: false);
    }
}
