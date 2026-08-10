namespace Paperbunkr.Data.Entities;

/// <summary>
/// Reader page-fit strategy for a <see cref="Issue"/> (via <see cref="Issue.PageFitModeOverride"/>),
/// falling back to a fixed code default when unset (docs/superpowers/specs/
/// 2026-08-10-reader-polish-core-viewing-controls-design.md §3) rather than an <c>AppSettings</c>
/// column, since no Reader Preferences surface exists yet to edit a global default.
///
/// Matches ComicRackCE's <c>ImageFitMode</c> (Engine/Display/ImageFitMode.cs) minus
/// <c>FitWidthAdaptive</c> - deliberately not ported, see the design spec §1.
/// </summary>
public enum ImageFitMode
{
    Original,
    Fit,
    FitWidth,
    FitHeight,
    BestFit
}
