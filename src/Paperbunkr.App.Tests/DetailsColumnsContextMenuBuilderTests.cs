using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="DetailsColumnsContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-
/// keyboard-operability-design.md) - Library's Details-table header column picker, formerly dead
/// (found during a broader sweep, beyond the originally-scoped four dead menus). Joins
/// <see cref="AvaloniaTestCollection"/> defensively - a real cross-class failure cascade this
/// session traced to an Avalonia-dependent test class running outside this collection.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailsColumnsContextMenuBuilderTests
{
    [Fact]
    public void BuildContextMenu_ReturnsOneEntryPerColumn_IgnoringTarget()
    {
        var columns = new[]
        {
            new DetailsColumn { Field = IssueListSortField.Title, DisplayName = "Title", IsVisible = true },
            new DetailsColumn { Field = IssueListSortField.Number, DisplayName = "Number", IsVisible = false },
        };
        IContextMenuProvider provider = new DetailsColumnsContextMenuBuilder(columns);

        var entries = provider.BuildContextMenu("this argument is ignored");

        Assert.NotNull(entries);
        Assert.Equal(new[] { "Title", "Number" }, entries!.Select(e => e.Header));
        Assert.True(entries[0].IsChecked);
        Assert.False(entries[1].IsChecked);
        Assert.Same(columns[0].ToggleVisibleCommand, entries[0].Command);
    }

    [Fact]
    public void BuildContextMenu_EmptyColumns_ReturnsEmptyList()
    {
        IContextMenuProvider provider = new DetailsColumnsContextMenuBuilder(Array.Empty<DetailsColumn>());

        var entries = provider.BuildContextMenu(null);

        Assert.NotNull(entries);
        Assert.Empty(entries!);
    }
}
