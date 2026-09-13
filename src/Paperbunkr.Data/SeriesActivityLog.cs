using System;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data;

/// <summary>
/// Write side of the Detail screen's Activity tab expansion (docs/superpowers/specs/2026-09-13-
/// activity-tab-event-log-expansion-design.md) - same static-resolver shape as
/// <c>ContinuityResolver</c>/<c>CollectionResolver</c>/<c>MediaRelationResolver</c>.
/// </summary>
public static class SeriesActivityLog
{
    /// <summary>Adds a pending <see cref="SeriesActivityEvent"/> row - does not call
    /// <see cref="PaperbunkrDbContext.SaveChanges"/> itself, matching every other resolver in this
    /// layer; the caller's own save covers it.</summary>
    public static void Record(PaperbunkrDbContext context, int seriesId, SeriesActivityEventKind kind, string detail, int? issueId = null)
    {
        context.SeriesActivityEvents.Add(new SeriesActivityEvent
        {
            SeriesId = seriesId,
            IssueId = issueId,
            Kind = kind,
            TimestampUtc = DateTime.UtcNow,
            Detail = detail,
        });
    }
}
