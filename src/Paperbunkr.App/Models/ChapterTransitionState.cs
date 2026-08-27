namespace Paperbunkr.App.Models;

/// <summary>
/// Reader chapter-transition overlay state (docs/superpowers/specs/2026-08-23-reader-chapter-
/// transition-design.md) - shown when crossing an issue boundary, either automatically
/// (<c>AutoNavigateComics</c>) or via the explicit Previous/Next Chapter buttons.
/// </summary>
public enum ChapterTransitionState
{
    Hidden,

    /// <summary>Continuous-mode only - shown while the adjacent issue's decoder is starting up (paged mode's adjacent-issue decode is fast enough this state would just flicker, so it skips straight to <see cref="Card"/>).</summary>
    Loading,

    /// <summary>Cover art + "Previous: #N" / "Current: #N+1" labels.</summary>
    Card
}
