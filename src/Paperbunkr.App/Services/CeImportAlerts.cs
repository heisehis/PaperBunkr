using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// The one Activity Center alert a ComicRack import raises for what it could not carry over - dropped smart-list conditions, refused lists, organizer
/// profiles that were skipped or need a look (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md,
/// docs/superpowers/specs/2026-10-04-lop-profile-import-design.md). The Data layer returns the notes; the screen that ran the import raises this, once per batch.
/// </summary>
public static class CeImportAlerts
{
    private const int MaxLines = 8;

    /// <summary>The alert for <paramref name="notes"/>, or null when there is nothing to tell. <paramref name="what"/> names the import ("smart list", "organizer profile"); empty when a drop mixed kinds.</summary>
    public static ActivityAlert? Build(IReadOnlyList<string>? notes, string what)
    {
        if (notes is null || notes.Count == 0)
        {
            return null;
        }

        string detail = string.Join(Environment.NewLine, notes.Take(MaxLines));
        if (notes.Count > MaxLines)
        {
            detail += $"{Environment.NewLine}…and {notes.Count - MaxLines} more.";
        }

        return new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Warning,
            Title = $"{(string.IsNullOrWhiteSpace(what) ? "ComicRack import" : $"ComicRack {what} import")}: {notes.Count} thing{(notes.Count == 1 ? "" : "s")} to check",
            Detail = detail,
        };
    }

    /// <summary>Raises the alert for <paramref name="notes"/> on <paramref name="activity"/> when there are any.</summary>
    public static void RaiseIfAny(IActivityService? activity, IReadOnlyList<string>? notes, string what)
    {
        if (activity is not null && Build(notes, what) is { } alert)
        {
            activity.RaiseAlert(alert);
        }
    }
}
