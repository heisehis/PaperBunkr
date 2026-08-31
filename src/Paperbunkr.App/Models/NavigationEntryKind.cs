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
}
