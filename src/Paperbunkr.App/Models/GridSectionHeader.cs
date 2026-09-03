namespace Paperbunkr.App.Models;

/// <summary>
/// A section-header row in a flattened, virtualized list (docs/superpowers/specs — Library issue
/// view-mode virtualization, 2026-09-03). The grouped List / Details modes render one `ListBox`
/// over a flat <c>object</c> collection that interleaves these headers with the real row items
/// (<see cref="IssueListRow"/> / <see cref="SeriesCardSample"/>), so a single
/// <c>VirtualizingStackPanel</c> virtualizes headers and rows alike - rather than an outer
/// <c>ItemsControl</c> whose per-group inner <c>ItemsControl</c> realizes every row up front.
/// Resolved to its own <c>DataTemplate</c> by runtime type.
/// </summary>
/// <param name="Header">The group label (already computed by the sort/group engine).</param>
/// <param name="Count">Number of item rows that follow this header in its section.</param>
public sealed record GridSectionHeader(string Header, int Count);
