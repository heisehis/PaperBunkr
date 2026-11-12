using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata.EventEnrichment;

/// <summary>Per-event transaction boundary shared by fill, comparison and undo. Never performs network I/O.</summary>
public static class EventEnrichmentService
{
    public static EventApplyResult Apply(PaperbunkrDbContext db, EventEnrichmentProposal proposal,
        IReadOnlyList<EventEvidence>? selected = null, Guid? operationId = null, CancellationToken cancellationToken = default)
    {
        Guid operation = operationId ?? Guid.NewGuid();
        var skipped = new List<string>(); int changed = 0;
        using var transaction = db.Database.BeginTransaction();
        db.ChangeTracker.Clear();
        var e = EventMetadataStore.Query(db).SingleOrDefault(e => e.Id == proposal.Snapshot.EventId);
        if (e is null) return new(operation, 0, ["Event no longer exists."]);
        if (EventMetadataStore.Identity(e) != proposal.Snapshot.Identity) return new(operation, 0, ["Event identity or membership changed; fetch again."]);
        var now = EventMetadataStore.Capture(db, e);
        var chosen = selected ?? proposal.Automatic;
        var dateChoices = chosen.Where(c => c.Field is EventMetadataField.StartDate or EventMetadataField.EndDate)
            .Where(c => proposal.Snapshot.Fields[c.Field] == now.Fields[c.Field]).GroupBy(c => c.Field).ToDictionary(g => g.Key, g => (string?)g.Single().Value);
        bool validDates = EventEnrichmentEvaluator.ValidDates(dateChoices.GetValueOrDefault(EventMetadataField.StartDate, now.Fields[EventMetadataField.StartDate].Value),
            dateChoices.GetValueOrDefault(EventMetadataField.EndDate, now.Fields[EventMetadataField.EndDate].Value));
        db.EventMetadataWriteManaged = true;
        try
        {
            foreach (var candidate in chosen.GroupBy(c => c.Field).Select(g => g.Single()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!proposal.Evidence.Contains(candidate)) { skipped.Add($"{candidate.Field}: candidate is not part of this fetch."); continue; }
                var before = proposal.Snapshot.Fields[candidate.Field]; var current = now.Fields[candidate.Field];
                if (before != current) { skipped.Add($"{candidate.Field}: changed since fetching."); continue; }
                if (selected is null && (current.Value is not null || current.Protected)) continue;
                if (current.Value == candidate.Value && !current.Protected) continue;
                if (candidate.Field is EventMetadataField.StartDate or EventMetadataField.EndDate)
                {
                    if (!validDates) { skipped.Add($"{candidate.Field}: would reverse the publication span."); continue; }
                }
                EventMetadataStore.Set(db, e, candidate.Field, candidate.Value);
                if (candidate.Automatic && candidate.Provider == "GCD" && int.TryParse(candidate.SourceId, out int gcdId)
                    && candidate.SourceUrl == $"https://www.comics.org/story_arc/{gcdId}/") e.GcdArcId ??= gcdId;
                var state = EventMetadataStore.State(e, candidate.Field);
                state.Revision = Guid.NewGuid().ToString("N"); state.ProtectedBlank = false;
                state.EvidenceJson = JsonSerializer.Serialize(candidate);
                db.EventMetadataChanges.Add(new()
                {
                    OperationId = operation, StoryEvent = e, Field = candidate.Field,
                    BeforeValue = current.Value, AfterValue = candidate.Value, BeforeProtected = current.Protected,
                    AppliedRevision = state.Revision, EvidenceJson = state.EvidenceJson,
                });
                changed++;
            }
            if (changed > 0) { e.UpdatedAt = DateTime.UtcNow; db.SaveChanges(); }
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }
        finally { db.EventMetadataWriteManaged = false; }
        return new(operation, changed, skipped, EventMetadataStore.Identity(e));
    }

    public static EventApplyResult Undo(PaperbunkrDbContext db, Guid operation, int? eventId = null)
    {
        var skipped = new List<string>(); int count = 0;
        using var transaction = db.Database.BeginTransaction(); db.ChangeTracker.Clear();
        var changes = db.EventMetadataChanges.Where(c => c.OperationId == operation && !c.Undone
            && (eventId == null || c.StoryEventId == eventId)).OrderByDescending(c => c.Id).ToList();
        db.EventMetadataWriteManaged = true;
        try
        {
            foreach (var change in changes)
            {
                var e = EventMetadataStore.Query(db).SingleOrDefault(e => e.Id == change.StoryEventId);
                var state = e?.MetadataStates.SingleOrDefault(s => s.Field == change.Field);
                if (e is null || state?.Revision != change.AppliedRevision || change.MergeInvalidated)
                { skipped.Add($"Event {change.StoryEventId}, {change.Field}: edited or merged since this change."); continue; }
                if (change.Field is EventMetadataField.StartDate or EventMetadataField.EndDate)
                {
                    var eligibleDates = changes.Where(c => c.StoryEventId == e.Id && !c.MergeInvalidated && c.Field is EventMetadataField.StartDate or EventMetadataField.EndDate)
                        .Where(c => e.MetadataStates.Any(s => s.Field == c.Field && s.Revision == c.AppliedRevision)).ToDictionary(c => c.Field, c => c.BeforeValue);
                    if (!EventEnrichmentEvaluator.ValidDates(eligibleDates.GetValueOrDefault(EventMetadataField.StartDate, EventMetadataStore.Value(db, e, EventMetadataField.StartDate)),
                        eligibleDates.GetValueOrDefault(EventMetadataField.EndDate, EventMetadataStore.Value(db, e, EventMetadataField.EndDate))))
                    { skipped.Add($"{change.Field}: restoring it would reverse a later edited span."); continue; }
                }
                EventMetadataStore.Set(db, e, change.Field, change.BeforeValue);
                state!.Revision = Guid.NewGuid().ToString("N"); state.ProtectedBlank = change.BeforeValue is null || change.BeforeProtected;
                state.EvidenceJson = null; change.Undone = true; e.UpdatedAt = DateTime.UtcNow; count++;
            }
            db.SaveChanges(); transaction.Commit();
        }
        finally { db.EventMetadataWriteManaged = false; }
        return new(operation, count, skipped);
    }
}
