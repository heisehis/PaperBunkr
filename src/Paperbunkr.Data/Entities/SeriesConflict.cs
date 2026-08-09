namespace Paperbunkr.Data.Entities;

/// <summary>
/// A pending "is this the same series?" decision surfaced by
/// <see cref="CeMigration.SeriesNameMatcher"/> during a CE library migration
/// (docs/onboarding.md §14 step 3, docs/superpowers/specs/2026-08-06-migration-ux-design.md §A).
/// Only rows left <see cref="SeriesConflictStatus.Pending"/> represent a real Needs Review item —
/// anything the user actively resolves during the migration flow is written straight to
/// <see cref="SeriesConflictStatus.Merged"/>/<see cref="SeriesConflictStatus.KeptSeparate"/> and
/// never needs to surface again.
/// </summary>
public class SeriesConflict
{
    public int Id { get; set; }

    /// <summary>
    /// Set when the incoming series matched a series already in the library before this import
    /// (a re-run / against-existing match). Null when both candidates are new series created by
    /// this same import (an intra-import match) — see <see cref="SeriesAId"/>/<see cref="SeriesBId"/>.
    /// </summary>
    public int? ExistingSeriesId { get; set; }

    public Series? ExistingSeries { get; set; }

    /// <summary>Set alongside <see cref="SeriesBId"/> for an intra-import match between two newly-created series.</summary>
    public int? SeriesAId { get; set; }

    public Series? SeriesA { get; set; }

    public int? SeriesBId { get; set; }

    public Series? SeriesB { get; set; }

    /// <summary>The CE series name that triggered the match.</summary>
    public string IncomingName { get; set; } = string.Empty;

    /// <summary>The name it was matched against (an existing series' name, or the other incoming name).</summary>
    public string MatchedName { get; set; } = string.Empty;

    /// <summary>0.0-1.0 similarity score from <see cref="CeMigration.SeriesNameMatcher"/>.</summary>
    public double Similarity { get; set; }

    public SeriesConflictStatus Status { get; set; } = SeriesConflictStatus.Pending;

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}
