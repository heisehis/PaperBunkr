using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tracking;

/// <summary>Persistence operations for the coalesced automatic tracker push queue.</summary>
public static class TrackerPushRetryStore
{
    public static TrackerPushRetry EnqueueTransientFailure(
        PaperbunkrDbContext context,
        TrackingLink link,
        ReadingStatus status,
        int? chapter,
        string? error,
        DateTime nowUtc,
        DateTime? retryAfterUtc = null)
    {
        var row = context.TrackerPushRetries.SingleOrDefault(x => x.TrackingLinkId == link.Id);
        if (row is null)
        {
            row = new TrackerPushRetry
            {
                TrackingLinkId = link.Id,
                TargetExternalId = link.ExternalId,
                DesiredStatus = status,
                HighestChapter = chapter,
                RetriesUsed = 0,
                NextAttemptUtc = Later(nowUtc.AddMinutes(1), retryAfterUtc),
                LastAttemptUtc = nowUtc,
                LastError = error,
                State = TrackerPushRetryState.Pending,
                CreatedUtc = nowUtc,
            };
            context.TrackerPushRetries.Add(row);
        }
        else
        {
            bool targetChanged = !string.Equals(row.TargetExternalId, link.ExternalId, StringComparison.Ordinal);
            bool advanced = chapter is int current && (row.HighestChapter is null || current > row.HighestChapter.Value);
            row.TargetExternalId = link.ExternalId;
            row.DesiredStatus = status;
            row.HighestChapter = Max(row.HighestChapter, chapter);
            row.LastAttemptUtc = nowUtc;
            row.LastError = error;
            if (targetChanged || advanced)
            {
                row.RetriesUsed = 0;
                row.State = TrackerPushRetryState.Pending;
                row.NextAttemptUtc = Later(nowUtc.AddMinutes(1), retryAfterUtc);
            }
            else if (row.State == TrackerPushRetryState.Pending && retryAfterUtc is DateTime providerDue && providerDue > row.NextAttemptUtc)
            {
                row.NextAttemptUtc = providerDue;
            }
        }

        context.SaveChanges();
        return row;
    }

    public static List<TrackerPushRetry> LoadAll(PaperbunkrDbContext context) =>
        context.TrackerPushRetries.Include(x => x.TrackingLink).ThenInclude(x => x!.Series).OrderBy(x => x.NextAttemptUtc).ToList();

    public static List<TrackerPushRetry> LoadDue(PaperbunkrDbContext context, DateTime nowUtc) =>
        context.TrackerPushRetries.Where(x => x.State == TrackerPushRetryState.Pending && x.NextAttemptUtc <= nowUtc)
            .OrderBy(x => x.NextAttemptUtc).ToList();

    public static void Clear(PaperbunkrDbContext context, int trackingLinkId)
    {
        var row = context.TrackerPushRetries.SingleOrDefault(x => x.TrackingLinkId == trackingLinkId);
        if (row is null) return;
        context.TrackerPushRetries.Remove(row);
        context.SaveChanges();
    }

    public static void Dismiss(PaperbunkrDbContext context, int retryId)
    {
        var row = context.TrackerPushRetries.SingleOrDefault(x => x.Id == retryId && x.State == TrackerPushRetryState.Exhausted);
        if (row is null) return;
        context.TrackerPushRetries.Remove(row);
        context.SaveChanges();
    }

    public static void RecordScheduledFailure(PaperbunkrDbContext context, TrackerPushRetry row, string? error, DateTime nowUtc, DateTime? retryAfterUtc = null)
    {
        var current = context.TrackerPushRetries.SingleOrDefault(x => x.Id == row.Id);
        if (current is null) return;
        current.LastAttemptUtc = nowUtc;
        current.LastError = error;
        current.RetriesUsed++;
        if (current.RetriesUsed >= 5)
        {
            current.State = TrackerPushRetryState.Exhausted;
        }
        else
        {
            int delayMinutes = 1 << current.RetriesUsed;
            current.NextAttemptUtc = Later(nowUtc.AddMinutes(delayMinutes), retryAfterUtc);
        }
        context.SaveChanges();
    }

    public static void Exhaust(PaperbunkrDbContext context, TrackerPushRetry row, string? error, DateTime nowUtc)
    {
        var current = context.TrackerPushRetries.SingleOrDefault(x => x.Id == row.Id);
        if (current is null) return;
        current.State = TrackerPushRetryState.Exhausted;
        current.LastAttemptUtc = nowUtc;
        current.LastError = error;
        context.SaveChanges();
    }

    private static int? Max(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
    private static DateTime Later(DateTime scheduled, DateTime? provider) => provider is DateTime value && value > scheduled ? value : scheduled;
}
