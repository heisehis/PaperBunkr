namespace Paperbunkr.App.Models;

/// <summary>One issue a Character/Team/Location/Creator appears in, shown on <c>MetadataEntityDetailScreen</c> (docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md).</summary>
public sealed record MetadataEntityAppearanceRow(int SeriesId, string SeriesName, int IssueId, string IssueLabel, string? Role);

/// <summary>One series a Publisher publishes, shown on <c>MetadataEntityDetailScreen</c>.</summary>
public sealed record MetadataEntitySeriesRow(int SeriesId, string SeriesName);
