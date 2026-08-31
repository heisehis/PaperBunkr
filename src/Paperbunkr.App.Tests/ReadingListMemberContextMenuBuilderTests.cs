using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReadingListMemberContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-
/// keyboard-operability-design.md) - a new menu for Reading List member rows, which had none before.
/// No DB fixture needed - <see cref="ReadingListItemRowViewModel"/> wraps a plain
/// <see cref="ReadingListItem"/> the builder never queries the database through. Still joins
/// <see cref="AvaloniaTestCollection"/> - running an Avalonia-dependent test class outside this
/// collection lets it execute in true parallel with the other context-menu-builder test classes and
/// corrupt their shared state (found as a real cross-class failure cascade during this session).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingListMemberContextMenuBuilderTests
{
    private static ReadingListItemRowViewModel MakeRow(ReadingListItem? item = null) => new(
        item ?? new ReadingListItem(),
        onMoveUp: _ => { },
        onMoveDown: _ => { },
        onRemove: _ => { },
        onFieldChanged: _ => { },
        onLink: _ => { },
        onOpen: _ => { },
        onToggleRead: _ => { });

    [Fact]
    public void Build_Row_ReturnsExpectedTopLevelEntries()
    {
        var row = MakeRow();
        var builder = new ReadingListMemberContextMenuBuilder();

        var entries = builder.Build(row);

        Assert.NotNull(entries);
        var headers = entries!.Select(e => e.IsSeparator ? null : e.Header).ToList();
        Assert.Equal(new[] { "Open", "Mark as Read", "Set Role", null, "Remove from List" }, headers);
    }

    [Fact]
    public void Build_SetRoleSubmenu_HasOneEntryPerRoleOption()
    {
        var builder = new ReadingListMemberContextMenuBuilder();

        var entries = builder.Build(MakeRow());
        var setRole = entries!.First(e => e.Header == "Set Role");

        Assert.NotNull(setRole.Children);
        Assert.Equal(ReadingListItemRowViewModel.RoleOptions.Length, setRole.Children!.Count);
    }

    [Fact]
    public void Build_ToggleReadLabel_FlipsWithIsRead()
    {
        // IsRead comes from Item.Issue?.HasBeenRead() - a row with no linked Issue is never read.
        var row = MakeRow();
        var builder = new ReadingListMemberContextMenuBuilder();

        var entries = builder.Build(row);

        Assert.Equal("Mark as Read", entries!.First(e => e.Command == row.ToggleReadCommand).Header);
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var builder = new ReadingListMemberContextMenuBuilder();

        Assert.Null(builder.Build(new object()));
        Assert.Null(builder.Build(null));
    }
}
