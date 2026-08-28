using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Models;

/// <summary>
/// One cover card in a <c>PosterRail</c> (docs/superpowers/specs/2026-08-28-detail-screens-
/// streaming-redesign-design.md) - a related series, a continuity/event sibling, or a
/// recommendation. <see cref="Id"/> is the target series id for click-through; <see cref="Payload"/>
/// carries the original sample object back to the rail's Remove command.
/// </summary>
public sealed class PosterRailItem
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? SubLabel { get; init; }

    public IBrush? CoverBrush { get; init; }

    public Bitmap? CoverImage { get; init; }

    /// <summary>The source sample (e.g. <c>RelatedSeriesSample</c>) - passed to Remove/click commands unchanged.</summary>
    public object? Payload { get; init; }
}
