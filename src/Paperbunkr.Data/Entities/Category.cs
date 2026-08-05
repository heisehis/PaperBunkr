namespace Paperbunkr.Data.Entities;

/// <summary>
/// A user-defined, flat (non-nested — closed as an open item, docs/onboarding.md §15) collapsible
/// category, Mihon-style. New entity; M:M with <see cref="Series"/> via EF Core 8's implicit
/// skip-navigation join table (no extra columns needed on the join, so no explicit join entity).
/// </summary>
public class Category
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public List<Series> Series { get; set; } = new();
}
