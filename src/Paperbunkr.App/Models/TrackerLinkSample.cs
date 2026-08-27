using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One already-linked tracker shown on the Detail screen's Details tab (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md) - separate list from <see cref="ExternalLinkSample"/>'s metadata links, per the same tracker-vs-scraper distinction that shapes this whole feature.</summary>
public sealed class TrackerLinkSample
{
    public required TrackingService Service { get; init; }
    public required string ExternalId { get; init; }
    public string? Url { get; init; }
}
