namespace Paperbunkr.Data.Entities;

/// <summary>
/// "Not interested" on a Home Because-You-Read card (docs/superpowers/specs/2026-09-28-home-improvements-design.md I5): the series is
/// left out of every Because-You-Read row until the user unhides it (Undo toast, or Preferences › Appearance › Home). One row per
/// series; the foreign key cascades, so it goes away with the series. Home-only - <c>RecommendationResolver</c> itself never reads it.
/// </summary>
public class DismissedRecommendation
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public DateTime DismissedUtc { get; set; }
}
