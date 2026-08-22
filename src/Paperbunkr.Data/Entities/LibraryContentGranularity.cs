namespace Paperbunkr.Data.Entities;

/// <summary>
/// Library's card granularity - independent of <see cref="LibraryViewMode"/> (the layout *shape*:
/// grid/list/tiles/etc.). Added back per explicit user request after docs/superpowers/specs/
/// 2026-08-18-library-book-centric-redesign-design.md Slice 3 made every Display mode per-issue by
/// default with no way back to the original one-card-per-series view - this restores that as a
/// real, switchable option rather than the sole model. <see cref="Issue"/> is the default, matching
/// what Slice 3 shipped; <see cref="Series"/> is the original aggregated-card behavior.
/// </summary>
public enum LibraryContentGranularity
{
    Issue,
    Series,
}
