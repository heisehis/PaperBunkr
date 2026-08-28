namespace Paperbunkr.App.Models;

/// <summary>
/// Which mode the Story Events screen is showing (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4f-continuity-browse-design.md introduces the switcher; docs/superpowers/specs/2026-08-27-
/// metadata-model-phase4g-age-progression-design.md adds <see cref="Timeline"/>). All three modes
/// reuse the same screen chrome (sidebar + detail pane) - the switcher only swaps their contents.
/// </summary>
public enum EventsScreenMode
{
    Events,
    Continuities,
    Timeline,
}
