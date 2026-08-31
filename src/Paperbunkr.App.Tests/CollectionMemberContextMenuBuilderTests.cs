using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Collections;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CollectionMemberContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-
/// keyboard-operability-design.md) - a new menu for the Collection editor's member rows, which had
/// none before. No DB/DI fixture needed - <see cref="CollectionMemberRowViewModel"/> is a plain
/// wrapper the builder never queries the database through. Still joins
/// <see cref="AvaloniaTestCollection"/> - running an Avalonia-dependent test class outside this
/// collection lets it execute in true parallel with the other context-menu-builder test classes and
/// corrupt their shared state (found as a real cross-class failure cascade during this session).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CollectionMemberContextMenuBuilderTests
{
    private static CollectionMemberRowViewModel MakeRow(int? collectionItemId = 1) =>
        new(new CollectionMember(collectionItemId, 0, CollectionMemberKind.Series, 5, "A Series", null, null, null),
            onRemove: _ => { });

    [Fact]
    public void Build_RemovableRow_ReturnsRemoveEntry()
    {
        var row = MakeRow();
        var builder = new CollectionMemberContextMenuBuilder();

        var entries = builder.Build(row);

        var entry = Assert.Single(entries!);
        Assert.Equal("Remove from Collection", entry.Header);
        Assert.Same(row.RemoveCommand, entry.Command);
        Assert.True(entry.IsEnabled);
    }

    [Fact]
    public void Build_RuleMatchedRow_RemoveCommandCanExecuteIsFalse()
    {
        // ContextMenuEntry.IsEnabled isn't set here (defaults true) - enablement for this entry
        // comes from Avalonia's own Command/CanExecute wiring on the MenuItem, not the entry's own
        // flag, matching the plan's documented choice not to duplicate that gating.
        var row = MakeRow(collectionItemId: null);
        var builder = new CollectionMemberContextMenuBuilder();

        var entries = builder.Build(row);

        Assert.NotNull(entries);
        Assert.False(row.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var builder = new CollectionMemberContextMenuBuilder();

        Assert.Null(builder.Build(new object()));
        Assert.Null(builder.Build(null));
    }
}
