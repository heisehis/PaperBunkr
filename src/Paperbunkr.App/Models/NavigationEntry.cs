namespace Paperbunkr.App.Models;

/// <summary>One step in the app shell's drill-down navigation history (docs/superpowers/specs/
/// 2026-08-30-app-shell-navigation-history-design.md) - <paramref name="ScreenKey"/> matches
/// <c>MainViewModel.CurrentScreen</c>'s existing string values ("detail", "mangaDetail",
/// "bookDetail", "reader", "bookReader", "pdfReader"). <paramref name="Label"/> is captured at push
/// time (the entity's display name/title) so the breadcrumb trail doesn't need a live re-query.</summary>
public sealed record NavigationEntry(string ScreenKey, NavigationEntryKind Kind, int EntityId, string Label);
