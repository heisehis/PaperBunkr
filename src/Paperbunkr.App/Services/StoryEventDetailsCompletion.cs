using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
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
        var storyEvent = await context.StoryEvents.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken).ConfigureAwait(false);
        if (storyEvent is null) return new RefreshResult(false, false, false, null);

        var candidates = storyEvent.IdentityConflict is not null
            ? new[] { ("Metron", storyEvent.MetronArcId) }
            : new[] { ("Metron", storyEvent.MetronArcId), ("ComicVine", storyEvent.ComicVineArcId) };
        bool hasIdentity = candidates.Any(c => !string.IsNullOrWhiteSpace(c.Item2));
        if (!hasIdentity) return new RefreshResult(false, false, false, null);

        bool attempted = false;
        bool detailsChanged = false;
        string? providerUsed = null;
        foreach (var (sourceKey, arcId) in candidates)
        {
            if (string.IsNullOrWhiteSpace(arcId)) continue;
            var source = ReadingListSourceRegistry.Get(context, sourceKey);
            if (source is null) continue;
            attempted = true;
            try
            {
                var info = await source.GetArcOverviewAsync(arcId, cancellationToken).ConfigureAwait(false);
                if (info is null) continue;
                providerUsed ??= sourceKey;
                if (string.IsNullOrWhiteSpace(storyEvent.Description) && !string.IsNullOrWhiteSpace(info.Description))
                { storyEvent.Description = info.Description; detailsChanged = true; }
                if (storyEvent.CoverSource != StoryEventCoverSource.Manual && !string.IsNullOrWhiteSpace(info.CoverImageUrl)
                    && storyEvent.CoverImageUrl != info.CoverImageUrl)
                {
                    storyEvent.CoverImageUrl = info.CoverImageUrl;
                    storyEvent.CoverSource = sourceKey == "Metron" ? StoryEventCoverSource.Metron : StoryEventCoverSource.ComicVine;
                    detailsChanged = true;
                }
                if (!string.IsNullOrWhiteSpace(storyEvent.Description)
                    && (storyEvent.CoverSource == StoryEventCoverSource.Manual || !string.IsNullOrWhiteSpace(storyEvent.CoverImageUrl))) break;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* Report the attempt in the UI; provider/network failures leave the event usable. */ }
        }

        if (attempted)
        {
            storyEvent.DetailsCheckedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        return new RefreshResult(true, attempted, detailsChanged, providerUsed);
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
                        if (string.IsNullOrWhiteSpace(storyEvent.Description) && !string.IsNullOrWhiteSpace(info.Description))
                        { storyEvent.Description = info.Description; changed = true; }
                        if (storyEvent.CoverSource != StoryEventCoverSource.Manual && string.IsNullOrWhiteSpace(storyEvent.CoverImageUrl)
                            && !string.IsNullOrWhiteSpace(info.CoverImageUrl))
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
