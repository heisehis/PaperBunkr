namespace Paperbunkr.App.Models;

/// <summary>Which kind of entity a <see cref="NavigationEntry"/> points at (docs/superpowers/specs/
/// 2026-08-30-app-shell-navigation-history-design.md) - drives both breadcrumb-label resolution and
/// which <c>...Core</c> navigate method <c>MainViewModel.ReplayEntry</c> dispatches to.</summary>
public enum NavigationEntryKind
{
    Series,
    MangaSeries,
    Issue,
    Book,
    BookSeries,

    // Added docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md - all five share one
    // ScreenKey ("metadataEntity") and one ViewModel/View pair; this is what ReplayEntry uses to know
    // which ComicMetadataEntityKind to reload, not a separate screen per kind.
    Character,
    Team,
    Location,
    Creator,
    Publisher,
}
