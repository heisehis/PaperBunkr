using System;

namespace Paperbunkr.Data.Entities;

/// <summary>
/// Why a <see cref="RemovedLibraryEntry"/> exists (docs/superpowers/specs/2026-09-06-missing-
/// files-library-health-design.md). Only <see cref="MissingFileCleanup"/> is written today; left
/// open so Duplicate Files (or any other future Library Health cleanup) can reuse this same table
/// instead of inventing its own removal log.
/// </summary>
public enum RemovedLibraryEntryReason
{
    MissingFileCleanup,
}

/// <summary>
/// A durable "Recently Removed" record of one <see cref="Issue"/> deleted from Library Health
/// (single-item or bulk), with a 30-day Restore window (docs/superpowers/specs/2026-09-06-missing-
/// files-library-health-design.md). Written once per removed issue, in the same transaction,
/// immediately before <c>LibraryDeletionHelper.RemoveIssue</c> - never for a removal that happens
/// anywhere else in the app (Needs Review's Duplicate Files, bulk delete elsewhere, etc.).
/// </summary>
public class RemovedLibraryEntry
{
    public int Id { get; set; }

    public string SeriesName { get; set; } = "";

    /// <summary>The still-existing <see cref="Series"/> id, if any, so Restore can re-link into it instead of recreating a series by name. No FK - a dangling id here (series later deleted through some other path) just means Restore falls back to recreating by <see cref="SeriesName"/>.</summary>
    public int? SeriesId { get; set; }

    public string? Number { get; set; }

    public string? Volume { get; set; }

    public string? Title { get; set; }

    public string? FilePath { get; set; }

    public DateTime RemovedAtUtc { get; set; }

    public RemovedLibraryEntryReason Reason { get; set; }
}
