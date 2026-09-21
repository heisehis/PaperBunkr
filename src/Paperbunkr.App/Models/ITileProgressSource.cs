namespace Paperbunkr.App.Models;

/// <summary>
/// What a Library Poster/Panorama tile's <c>TileCosmeticsOverlay</c> needs from its row model
/// (docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md #1/#2): read progress for the ring and
/// reading direction for which edge the binding spine sits on. The overlay reads its own DataContext
/// through this interface instead of taking bindings, so realizing a tile adds no binding work.
/// </summary>
public interface ITileProgressSource
{
    /// <summary>0..1: pages read for an issue tile, issues read / total for a series tile.</summary>
    double ReadFraction { get; }

    /// <summary>True once everything is read - the ring then shows at rest, not just on hover.</summary>
    bool IsFinished { get; }

    /// <summary>True for a right-to-left series - the spine flips to the cover's right edge.</summary>
    bool IsRightToLeft { get; }
}
