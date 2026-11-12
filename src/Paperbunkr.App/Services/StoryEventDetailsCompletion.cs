using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.Services;

/// <summary>Best-effort, throttled provider enrichment for events with already verified provider arc IDs.</summary>
public static class StoryEventDetailsCompletion
{
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);

    public sealed record RefreshResult(bool HasProviderIdentity, bool Attempted, bool Changed, string? Provider);

    /// <summary>Explicit event-page refresh. Bypasses the automatic 30-day throttle while preserving user-entered details.</summary>
    public static async Task<RefreshResult> RefreshEventAsync(PaperbunkrDbContext context, int eventId, CancellationToken cancellationToken)
    {
        var row = await context.StoryEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, cancellationToken).ConfigureAwait(false);
        if (row is null) return new(false, false, false, null);
        bool linked = row.MetronArcId is not null || row.ComicVineArcId is not null || row.GcdArcId is not null;
        string? connectionString = context.Database.GetConnectionString();
        var runner = new EventSmartFillRunner
        {
            ContextFactory = () => new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite(connectionString).Options),
        };
        var proposal = await runner.FetchAsync(eventId, cancellationToken).ConfigureAwait(false);
        var result = await runner.ApplyAsync(proposal, null, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        var checkedSources = proposal.Sources.Where(s => s.Status is "Checked" or "No data" or "Incomplete").ToArray();
        return new(linked, checkedSources.Length > 0, result.Changed > 0, string.Join(", ", checkedSources.Select(s => s.Provider).Distinct()));
    }

    public static async Task<bool> CompleteAsync(PaperbunkrDbContext context, int continuityId, CancellationToken cancellationToken)
    {
        var eventIds = Paperbunkr.App.Services.EventMap.EventMapLoader.EventsInContinuity(context, continuityId)
            .Select(e => e.StoryEventId).Distinct().ToList();
        var events = context.StoryEvents.Where(e => eventIds.Contains(e.Id)
            && (e.DetailsCheckedAt == null || e.DetailsCheckedAt < DateTime.UtcNow - RefreshAfter)
            && ((e.MetronArcId != null) || (e.ComicVineArcId != null))).ToList();
        bool changed = false;
        foreach (var storyEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (storyEvent.IdentityConflict is not null && storyEvent.MetronArcId is null) continue;
            var sourceCandidates = storyEvent.IdentityConflict is not null
                ? new[] { ("Metron", storyEvent.MetronArcId) }
                : new[] { ("Metron", storyEvent.MetronArcId), ("ComicVine", storyEvent.ComicVineArcId) };
            bool attempted = false;
            foreach (var (sourceKey, arcId) in sourceCandidates)
            {
                if (string.IsNullOrWhiteSpace(arcId)) continue;
                var source = ReadingListSourceRegistry.Get(context, sourceKey);
                if (source is null) continue;
                attempted = true;
                try
                {
                    var info = await source.GetArcOverviewAsync(arcId, cancellationToken).ConfigureAwait(false);
                    if (info is not null)
                    {
                        string? synopsis = EventDescriptionText.Overview(info.Deck) ?? EventDescriptionText.Overview(info.Description);
                        if (EventMetadataStore.Eligible(context, storyEvent, EventMetadataField.Description) && synopsis is not null)
                        { storyEvent.Description = synopsis; changed = true; }
                        if (EventMetadataStore.Eligible(context, storyEvent, EventMetadataField.Cover)
                            && !string.IsNullOrWhiteSpace(info.CoverImageUrl)
                            && await StoryEventCoverCache.DownloadAndCacheAsync(storyEvent.Id, info.CoverImageUrl, cancellationToken).ConfigureAwait(false))
                        { storyEvent.CoverImageUrl = info.CoverImageUrl; storyEvent.CoverSource = sourceKey == "Metron" ? StoryEventCoverSource.Metron : StoryEventCoverSource.ComicVine; changed = true; }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* offline and provider errors leave the event usable; retry on a later explicit load */ }
                if (!string.IsNullOrWhiteSpace(storyEvent.Description) && (storyEvent.CoverSource == StoryEventCoverSource.Manual || !string.IsNullOrWhiteSpace(storyEvent.CoverImageUrl))) break;
            }
            if (attempted) { storyEvent.DetailsCheckedAt = DateTime.UtcNow; changed = true; }
        }
        if (changed) await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }
}
