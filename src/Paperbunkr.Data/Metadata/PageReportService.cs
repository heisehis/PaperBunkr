using System;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>What <see cref="PageReportService.Upsert"/> replaced, so the reader's "Undo" can put it back.</summary>
public sealed record PageReportUndo(int IssueId, int PageNumber, bool WasNew, PageReportReason PreviousReason, bool PreviousAcknowledged);

/// <summary>
/// Reads and writes <see cref="PageReport"/> rows (docs/superpowers/specs/2026-09-21-comic-reader-page-
/// intelligence-design.md §4). Each method saves its own change, so callers (the reader, Library Health) stay
/// one-liners.
/// </summary>
public static class PageReportService
{
    /// <summary>Creates the report for the page, or - if it is already reported - updates its reason and un-dismisses it.</summary>
    public static PageReportUndo Upsert(PaperbunkrDbContext context, int issueId, int pageNumber, PageReportReason reason)
    {
        var existing = context.PageReports.FirstOrDefault(r => r.IssueId == issueId && r.PageNumber == pageNumber);
        if (existing is null)
        {
            context.PageReports.Add(new PageReport
            {
                IssueId = issueId,
                PageNumber = pageNumber,
                Reason = reason,
                CreatedAt = DateTime.UtcNow,
            });
            context.SaveChanges();
            return new PageReportUndo(issueId, pageNumber, WasNew: true, reason, PreviousAcknowledged: false);
        }

        var undo = new PageReportUndo(issueId, pageNumber, WasNew: false, existing.Reason, existing.Acknowledged);
        existing.Reason = reason;
        existing.Acknowledged = false;
        context.SaveChanges();
        return undo;
    }

    /// <summary>Reverses an <see cref="Upsert"/>: deletes a report that upsert created, or restores the reason and dismissed state it overwrote.</summary>
    public static void Undo(PaperbunkrDbContext context, PageReportUndo undo)
    {
        var report = context.PageReports.FirstOrDefault(r => r.IssueId == undo.IssueId && r.PageNumber == undo.PageNumber);
        if (report is null)
        {
            return;
        }

        if (undo.WasNew)
        {
            context.PageReports.Remove(report);
        }
        else
        {
            report.Reason = undo.PreviousReason;
            report.Acknowledged = undo.PreviousAcknowledged;
        }

        context.SaveChanges();
    }

    /// <summary>Library Health's Dismiss: hides the report without deleting it, so a later re-report can bring it back.</summary>
    public static void Acknowledge(PaperbunkrDbContext context, int reportId)
    {
        var report = context.PageReports.Find(reportId);
        if (report is null)
        {
            return;
        }

        report.Acknowledged = true;
        context.SaveChanges();
    }

    /// <summary>Removes a report outright (used when its page is tagged Deleted from Library Health).</summary>
    public static void Remove(PaperbunkrDbContext context, int reportId)
    {
        var report = context.PageReports.Find(reportId);
        if (report is null)
        {
            return;
        }

        context.PageReports.Remove(report);
        context.SaveChanges();
    }
}
