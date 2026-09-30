namespace Paperbunkr.Data.Entities;

/// <summary>
/// "Don't relate these two series again" (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4): written when you delete a series
/// relation that came from a Grand Comics Database bond, so the weekly bond sync doesn't put it back. Stored in id order.
/// </summary>
public class SeriesRelationDismissal
{
    public int Id { get; set; }

    public int LowerSeriesId { get; set; }

    public Series? LowerSeries { get; set; }

    public int HigherSeriesId { get; set; }

    public Series? HigherSeries { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
