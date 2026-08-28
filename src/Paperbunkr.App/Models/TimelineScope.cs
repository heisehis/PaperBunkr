namespace Paperbunkr.App.Models;

/// <summary>
/// What set of series the Timeline mode lays out (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4g-age-progression-design.md - the per-Continuity and whole-library scopes it flags as
/// "plausible future modes on the same Timeline view").
/// </summary>
public enum TimelineScope
{
    /// <summary>The graph-driven family of a chosen series (<c>SeriesFamilyResolver</c>).</summary>
    SeriesFamily,

    /// <summary>Every series in a chosen continuity.</summary>
    Continuity,

    /// <summary>Every series in the library.</summary>
    Library,
}
