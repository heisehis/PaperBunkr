namespace Paperbunkr.Data.Entities;

/// <summary>
/// "Don't relate these two series again" (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4): written when you delete a series
/// relation that something automatic made (a Grand Comics Database bond, the series connector, Metron's associated series) or dismiss
/// a related-series suggestion (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §3.5), so no sweep puts it back.
/// Blocks every automatic relation and suggestion for the pair; your own relations are unaffected. Stored in id order.
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
