using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.EventEnrichment;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.Services;

public sealed class EventSmartFillRunner
{
    private EventEvidenceCache _cache = new();
    public void BeginOperation() => _cache = new();
    public Func<PaperbunkrDbContext> ContextFactory { get; init; } = PaperbunkrDb.CreateContext;
    public Func<GcdDataStore?> OpenGcd { get; init; } = () => GcdDataStore.TryOpen();
    public Func<PaperbunkrDbContext, string, IReadingListSource?> ResolveSource { get; init; } = ReadingListSourceRegistry.Get;
    public Func<int, string, CancellationToken, Task<bool>> PrepareCover { get; init; } = StoryEventCoverCache.DownloadAndCacheAsync;

    public Task<EventEnrichmentProposal> FetchAsync(int id, CancellationToken ct)
    {
        var cache = _cache;
        return Task.Run(async () =>
        {
            using var db = ContextFactory(); using var gcd = OpenGcd();
            var collector = new EventEvidenceCollector(key => ResolveSource(db, key), gcd, cache);
            var proposal = await collector.CollectAsync(db, id, ct).ConfigureAwait(false);
            SaveArcLists(db, id, cache);
            return proposal;
        }, ct);
    }

    /// <summary>
    /// Keeps the provider issue lists this run fetched under the event's own verified arc ids (continuity map M4 §1), so the map can show
    /// missing issues without fetching again. Disputed identities (an <c>IdentityConflict</c>) aren't saved for ComicVine. Best effort:
    /// a failure here never fails Smart fill.
    /// </summary>
    private static void SaveArcLists(PaperbunkrDbContext db, int eventId, EventEvidenceCache cache)
    {
        try
        {
            var e = db.StoryEvents.Where(x => x.Id == eventId).Select(x => new { x.MetronArcId, x.ComicVineArcId, x.IdentityConflict }).FirstOrDefault();
            if (e is null) return;
            if (!string.IsNullOrWhiteSpace(e.MetronArcId) && cache.TryGetEntries(EventArcSnapshots.Metron, e.MetronArcId!, out var metron))
                EventArcSnapshots.Save(db, eventId, EventArcSnapshots.Metron, e.MetronArcId!, metron);
            if (!string.IsNullOrWhiteSpace(e.ComicVineArcId) && e.IdentityConflict is null && cache.TryGetEntries(EventArcSnapshots.ComicVine, e.ComicVineArcId!, out var cv))
                EventArcSnapshots.Save(db, eventId, EventArcSnapshots.ComicVine, e.ComicVineArcId!, cv);
        }
        catch (Exception ex) when (ex is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException)
        {
        }
    }

    public async Task<EventApplyResult> ApplyAsync(EventEnrichmentProposal proposal, EventEvidence[]? selection, Guid operation, CancellationToken ct)
    {
        var chosen = selection ?? proposal.Automatic.ToArray();
        var skipped = new List<string>();
        var failedCovers = chosen.Where(c => c.Field == EventMetadataField.Cover).ToArray();
        foreach (var candidate in failedCovers)
        {
            var cover = JsonSerializer.Deserialize<EventCoverValue>(candidate.Value);
            if (cover?.Url is null || !await PrepareCover(proposal.Snapshot.EventId, cover.Url, ct).ConfigureAwait(false))
            {
                chosen = chosen.Where(c => c != candidate).ToArray();
                skipped.Add("Cover: download or image validation failed; existing artwork was preserved.");
            }
        }
        ct.ThrowIfCancellationRequested();
        var result = await Task.Run(() =>
        {
            using var db = ContextFactory();
            // Passing an explicit selection is safe only after deciding automatic eligibility in this proposal.
            return EventEnrichmentService.Apply(db, proposal, chosen, operation, ct);
        }, ct).ConfigureAwait(false);
        return result with { Skipped = result.Skipped.Concat(skipped).ToArray() };
    }

    public Task<EventApplyResult> UndoAsync(Guid operation, int? eventId = null) => Task.Run(() =>
    {
        using var db = ContextFactory(); return EventEnrichmentService.Undo(db, operation, eventId);
    });
}
