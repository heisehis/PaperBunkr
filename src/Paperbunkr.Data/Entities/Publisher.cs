namespace Paperbunkr.Data.Entities;

/// <summary>
/// A first-class named publisher (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md).
/// Unlike <see cref="Character"/>/<see cref="Team"/>/<see cref="Location"/>/<see cref="Creator"/>,
/// publisher is single-valued per <see cref="Series"/>/<see cref="Issue"/>, not a list - so there is
/// no appearance/credit join table. <see cref="Series.PublisherEntityId"/> and
/// <see cref="Issue.PublisherEntityId"/> point directly at this table, auto-materialized from the
/// existing <see cref="Issue.Publisher"/>/<see cref="Series.Publisher"/> free-text fields by
/// <c>PublisherResolver</c>, which stay the editable source of truth.
/// </summary>
public class Publisher
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
