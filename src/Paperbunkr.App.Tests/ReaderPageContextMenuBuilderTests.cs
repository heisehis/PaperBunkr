using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReaderPageContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-
/// keyboard-operability-design.md) - the ported page-thumbnail menu, formerly dead (a plain
/// <c>ContextMenu</c> element that never renders in this Avalonia build). First builder in this
/// batch to exercise nested submenus.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderPageContextMenuBuilderTests
{
    private static ReaderThumbnailSample MakeThumbnail() => new() { CoverBrush = Brushes.Gray };

    [Fact]
    public void Build_Thumbnail_ReturnsPageTypeAndRotateSubmenus()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);
        var thumbnail = MakeThumbnail();

        var entries = builder.Build(thumbnail);

        Assert.NotNull(entries);
        Assert.Equal(2, entries!.Count);
        Assert.Equal("Page Type", entries[0].Header);
        Assert.Equal("Rotate", entries[1].Header);
    }

    [Fact]
    public void Build_Thumbnail_PageTypeSubmenuHasFourOptions()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);
        var thumbnail = MakeThumbnail();

        var entries = builder.Build(thumbnail);
        var pageType = entries![0];

        Assert.NotNull(pageType.Children);
        Assert.Equal(new[] { "Story", "Cover", "Advertisement", "Deleted" }, pageType.Children!.Select(c => c.Header));
        Assert.Same(vm.SetPageTypeStoryCommand, pageType.Children[0].Command);
        Assert.Same(thumbnail, pageType.Children[0].CommandParameter);
    }

    [Fact]
    public void Build_Thumbnail_RotateSubmenuHasFourOptions()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);
        var thumbnail = MakeThumbnail();

        var entries = builder.Build(thumbnail);
        var rotate = entries![1];

        Assert.NotNull(rotate.Children);
        Assert.Equal(new[] { "No rotation", "90°", "180°", "270°" }, rotate.Children!.Select(c => c.Header));
        Assert.Same(vm.SetPageRotation90Command, rotate.Children[1].Command);
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);

        Assert.Null(builder.Build(new object()));
        Assert.Null(builder.Build(null));
    }
}
