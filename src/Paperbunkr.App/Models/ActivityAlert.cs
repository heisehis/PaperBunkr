using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One non-job signal in the Activity Center - update available, a watched folder went offline, an
/// API rate-limited, a plugin command failed (docs/superpowers/specs/2026-09-03-activity-center-
/// design.md). Deduped by <see cref="DedupeKey"/>: re-raising the same key refreshes
/// <see cref="CreatedUtc"/> on the existing row instead of stacking a second one. Session-scoped -
/// alerts are not persisted to history in v1.
/// </summary>
public sealed partial class ActivityAlert : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    public required ActivityAlertSeverity Severity { get; init; }

    public required string Title { get; init; }

    public string? Detail { get; init; }

    /// <summary>Label for the optional action link, e.g. "What's new". Null = no action.</summary>
    public string? ActionLabel { get; init; }

    public ActivityLink? ActionLink { get; init; }

    /// <summary>
    /// Stable key that collapses repeats of the same condition. E.g. <c>"watch-offline:D:\Comics"</c>
    /// or <c>"update-available"</c>. Defaults to <see cref="Id"/> (never dedupes) when not set.
    /// </summary>
    public string DedupeKey { get; init; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    private DateTime _createdUtc = DateTime.UtcNow;
}
