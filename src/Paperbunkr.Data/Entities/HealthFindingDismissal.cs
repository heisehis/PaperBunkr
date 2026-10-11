namespace Paperbunkr.Data.Entities;

/// <summary>
/// "This is fine, stop showing it" for one computed finding (docs/superpowers/specs/2026-10-06-smart-features-design.md §2): a collection
/// gap, a metadata-consistency finding, a continuity suggestion, a reading-list order check. Those findings are recomputed on demand and
/// have no row of their own to carry a flag, so the dismissal is keyed on what the finding is about. <see cref="Kind"/> names the
/// feature (see <see cref="Metadata.HealthDismissals"/>); <see cref="Key"/> is that feature's own stable key and deliberately
/// includes whatever must change for the finding to come back (a gap's key includes its missing numbers, so a new hole resurfaces it).
/// No foreign keys: a stale row for a deleted series simply never matches again.
/// </summary>
public class HealthFindingDismissal
{
    public int Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>What was dismissed, in words, for the "Dismissed" sub-group (the finding itself may no longer be computable).</summary>
    public string? Label { get; set; }

    public DateTime DismissedAt { get; set; }
}
