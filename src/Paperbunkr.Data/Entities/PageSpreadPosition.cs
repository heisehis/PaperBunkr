namespace Paperbunkr.Data.Entities;

/// <summary>
/// Per-page manual double-page-spread phase override (docs/superpowers/specs/2026-09-10-reader-
/// backlog-batch-b-design.md Item 2), a per-page escape hatch for pairing drift after an odd run of
/// landscape pages. Mirrors ComicRackCE's <c>ComicPagePosition { Default, Near, Far }</c>
/// (Engine/ComicPagePosition.cs), serialized by CE as <c>&lt;Page PagePosition="Near" /&gt;</c>.
///
/// It does <b>not</b> bind two images together - like CE, it only nudges the navigation step size
/// (1 vs 2), which shifts which page leads a spread. CE's display pairing test ignores it entirely;
/// so does Paperbunkr's (<c>SpreadLayoutMath.IsPairEligible</c> / <c>TryDecodePairedPage</c>).
/// <see cref="Default"/> = automatic (aspect-ratio pairing decides).
/// </summary>
public enum PageSpreadPosition
{
    Default,
    Near,
    Far,
}
