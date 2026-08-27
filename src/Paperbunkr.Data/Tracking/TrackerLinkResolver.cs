using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// Search + link workflow for tracker-linking (docs/superpowers/specs/2026-08-23-tracker-write-back-
/// sync-design.md) - mirrors <see cref="MetadataLinkResolver"/>'s shape but touches only
/// <see cref="TrackingLink"/>, deliberately never <see cref="ExternalMediaId"/>/
/// <see cref="ExternalMetadataSnapshot"/>/<see cref="SeriesTitle"/>. A tracker-search result already
/// carries everything a <see cref="TrackingLink"/> needs (its <c>ExternalId</c>), and writing
/// canonical metadata from a tracker-linking action would blur the tracker-vs-scraper boundary this
/// feature's own folder split exists to keep visible - a metadata refresh must never touch user
/// state, and a tracker sync must never touch canonical metadata.
/// </summary>
public static class TrackerLinkResolver
{
    /// <summary>Searches <paramref name="provider"/> and scores each result against <paramref name="seriesId"/>'s known titles, best match first. Empty when the series doesn't exist or the query is blank.</summary>
    public static async Task<IReadOnlyList<ScoredMetadataMatch>> SearchAsync(
        ITrackerSearchProvider provider, PaperbunkrDbContext context, int seriesId, string query, CancellationToken cancellationToken)
    {
        var series = context.Series.Include(s => s.Titles).FirstOrDefault(s => s.Id == seriesId);
        if (series is null || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ScoredMetadataMatch>();
        }

        var knownTitles = new[] { series.Name }.Concat(series.Titles.Select(t => t.Value)).ToList();
        var results = await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        return results
            .Select(r =>
            {
                double score = TitleMatchScorer.BestScore(knownTitles, r.Title);
                return new ScoredMetadataMatch(r, score, TitleMatchScorer.Tier(score));
            })
            .OrderByDescending(m => m.Confidence)
            .ToList();
    }

    /// <summary>Upserts the <see cref="TrackingLink"/> for <paramref name="seriesId"/>+<paramref name="service"/>
    /// (one per series-per-service - re-linking replaces the prior external id). No network call, no
    /// metadata writes - a search result already carries everything needed. Does nothing if the
    /// series doesn't exist.</summary>
    public static void Link(PaperbunkrDbContext context, int seriesId, TrackingService service, string externalId)
    {
        if (!context.Series.Any(s => s.Id == seriesId))
        {
            return;
        }

        var existing = context.TrackingLinks.FirstOrDefault(t => t.SeriesId == seriesId && t.Service == service);
        if (existing is null)
        {
            context.TrackingLinks.Add(new TrackingLink
            {
                SeriesId = seriesId,
                Service = service,
                ExternalId = externalId,
            });
        }
        else
        {
            existing.ExternalId = externalId;
        }

        context.SaveChanges();
    }

    /// <summary>Removes only the <see cref="TrackingLink"/> row - per the same "removing a link must
    /// not delete the series' own data" precedent <c>DetailTabsViewModel.UnlinkMetadata</c> already
    /// established for metadata links.</summary>
    public static void Unlink(PaperbunkrDbContext context, int seriesId, TrackingService service)
    {
        var existing = context.TrackingLinks.FirstOrDefault(t => t.SeriesId == seriesId && t.Service == service);
        if (existing is not null)
        {
            context.TrackingLinks.Remove(existing);
            context.SaveChanges();
        }
    }
}
