using System.Globalization;
using System.Linq;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Tags a series' <see cref="Continuity"/> membership directly from Metron's <c>universes</c> field
/// (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md), which is already present in
/// the issue-detail response every scrape fetches - unlike <see cref="ContinuityWikidataMatchResolver"/>,
/// this needs no extra network call and isn't a scheduled task; it runs inline right after the other
/// derived-index resolvers during a scrape. Same visibility/namespace as <see cref="ContinuityResolver"/>
/// (both internal) so it can call <see cref="ContinuityResolver.GetOrCreate"/>/
/// <see cref="ContinuityResolver.AddSeriesToContinuity"/> directly.
/// </summary>
internal static class ContinuityMetronMatchResolver
{
    /// <summary>No-op for anything but a Metron-sourced scrape, or one with no universe tags - Metron is the only source that ever populates <see cref="ComicVineIssueDetails.Universes"/>.</summary>
    public static void SyncFromIssueDetails(PaperbunkrDbContext context, int seriesId, ComicProvider provider, IReadOnlyList<ComicVineIdName> universes)
    {
        if (provider != ComicProvider.Metron || universes.Count == 0)
        {
            return;
        }

        foreach (var universe in universes.Where(u => !string.IsNullOrWhiteSpace(u.Name)))
        {
            var continuity = ContinuityResolver.GetOrCreate(context, universe.Name);

            if (universe.ExternalId is int externalId)
            {
                string metronId = externalId.ToString(CultureInfo.InvariantCulture);
                if (continuity.MetronId != metronId)
                {
                    continuity.MetronId = metronId;
                    context.SaveChanges();
                }
            }

            ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);
        }
    }
}
