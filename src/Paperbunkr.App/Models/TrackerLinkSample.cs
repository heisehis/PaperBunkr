using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One already-linked tracker shown on the Detail screen's Details tab (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md) - separate list from <see cref="ExternalLinkSample"/>'s metadata links, per the same tracker-vs-scraper distinction that shapes this whole feature.</summary>
public sealed class TrackerLinkSample
{
    public required TrackingService Service { get; init; }
    public required string ExternalId { get; init; }
    public string? Url { get; init; }

    /// <summary>The service name as a plain string for a <c>BrandMark Family="Service"</c> binding
    /// (its <c>Value</c> is <see cref="string"/>?) - docs/superpowers/specs/2026-09-04-detail-
    /// screen-icons-and-glyphs-design.md Part 2 §A.</summary>
    public string ServiceName => Service.ToString();
}
