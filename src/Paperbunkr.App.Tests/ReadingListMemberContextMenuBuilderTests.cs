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
        Assert.Equal(new[] { "Open", "Mark as Read", "Set Role", "Add a Note", null, "Move Up", "Move Down", null, "Remove from List" }, headers);
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

    // ---- Plugin API 4.5 (docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §E) ----

    private static Paperbunkr.Plugins.CSharpCommand PluginCommand(string name) => new()
    {
        PluginKey = "p",
        Hook = Paperbunkr.Plugins.Hooks.PluginHooks.Library,
        Key = "p." + name,
        Name = name,
        Menus = "readinglist",
        ScriptPath = "x.csx",
    };

    [Fact]
    public async Task Plugin_commands_that_opt_in_appear_above_remove_and_run_on_the_rows_issue()
    {
        var issue = new Issue { Id = 7 };
        var row = MakeRow(new ReadingListItem { Issue = issue });
        var ran = new List<(string, int)>();
        var builder = new ReadingListMemberContextMenuBuilder(
            () => new[] { PluginCommand("Scrobble") },
            (command, target) => { ran.Add((command.Name, target.Id)); return Task.CompletedTask; });

        var entries = builder.Build(row)!;
        var plugins = entries.Single(e => e.Header == "Plugins");
        var item = Assert.Single(plugins.Children!);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)item!.Command!).ExecuteAsync(null);

        Assert.Equal(entries.Count - 3, entries.ToList().IndexOf(plugins));   // before the separator and "Remove from List"
        Assert.Equal(new[] { ("Scrobble", 7) }, ran);
    }

    [Fact]
    public void A_placeholder_row_with_no_issue_gets_no_plugin_submenu()
    {
        var builder = new ReadingListMemberContextMenuBuilder(() => new[] { PluginCommand("Scrobble") }, (_, _) => Task.CompletedTask);

        Assert.DoesNotContain(builder.Build(MakeRow())!, e => e.Header == "Plugins");
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var builder = new ReadingListMemberContextMenuBuilder();

        Assert.Null(builder.Build(new object()));
        Assert.Null(builder.Build(null));
    }
}
