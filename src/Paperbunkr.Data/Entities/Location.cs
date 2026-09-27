namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class named location, mirroring <see cref="Character"/> exactly (docs/superpowers/specs/
/// 2026-09-23-metron-api-utilization-design.md). Auto-materialized from the free-text
/// <see cref="Issue.Locations"/> ComicInfo field by <c>LocationResolver</c>; that string field stays
/// the editable source of truth. Metron has no location data at all (confirmed - no such REST
/// resource), so this table is only ever populated via ComicVine's <c>location_credits</c>.
/// </summary>
public class Location
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<LocationAppearance> Appearances { get; set; } = new();
}
