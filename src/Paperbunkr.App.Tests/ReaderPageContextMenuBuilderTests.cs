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
    public void Build_Thumbnail_ReturnsPageTypeRotateAndSpreadPositionSubmenus()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);
        var thumbnail = MakeThumbnail();

        var entries = builder.Build(thumbnail);

        Assert.NotNull(entries);
        Assert.Equal(4, entries!.Count);
        Assert.Equal("Page Type", entries[0].Header);
        Assert.Equal("Rotate", entries[1].Header);
        Assert.Equal("Spread position", entries[2].Header);
        Assert.Equal("Report Bad Page…", entries[3].Header);
    }

    [Fact]
    public void Build_Thumbnail_ReportBadPage_TargetsThatThumbnail()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);
        var thumbnail = MakeThumbnail();

        var report = builder.Build(thumbnail)!.Single(e => e.Header == "Report Bad Page…");

        Assert.Same(vm.ReportBadPageCommand, report.Command);
        Assert.Same(thumbnail, report.CommandParameter);
    }

    [Fact]
    public void Build_MainPage_OffersReportBadPage_WithNoParameter()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);

        var report = builder.Build(null)!.Single(e => e.Header == "Report Bad Page…");

        Assert.Same(vm.ReportBadPageCommand, report.Command);
        Assert.Null(report.CommandParameter);
    }

    [Fact]
    public void Build_Thumbnail_SpreadPositionSubmenuHasThreeOptions()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);
        var thumbnail = MakeThumbnail();

        var entries = builder.Build(thumbnail);
        var spread = entries![2];

        Assert.NotNull(spread.Children);
        Assert.Equal(new[] { "Automatic", "Near side (leading)", "Far side (trailing)" }, spread.Children!.Select(c => c.Header));
        Assert.Same(vm.SetSpreadPositionNearCommand, spread.Children[1].Command);
        Assert.Same(thumbnail, spread.Children[1].CommandParameter);
    }

    /// <summary>Pairing (and therefore the manual spread-position escape hatch) never applies in
    /// continuous/webtoon scroll - the submenu is omitted entirely rather than shown disabled.</summary>
    [Fact]
    public void Build_Thumbnail_InContinuousMode_OmitsTheSpreadPositionSubmenu()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }) { IsContinuousMode = true };
        var builder = new ReaderPageContextMenuBuilder(vm);
        var thumbnail = MakeThumbnail();

        var entries = builder.Build(thumbnail);

        Assert.NotNull(entries);
        Assert.Equal(3, entries!.Count);
        Assert.DoesNotContain(entries, e => e.Header == "Spread position");
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

    /// <summary>
    /// docs/superpowers/specs/2026-09-17-reader-save-page-and-cover-picker-design.md - the "not a
    /// thumbnail" fallback is the main displayed page (right-click anywhere else in the reader), so
    /// it now returns the Save Page As entries instead of null.
    /// </summary>
    [Fact]
    public void Build_UnrecognizedTarget_ReturnsSavePageAsEntries()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var builder = new ReaderPageContextMenuBuilder(vm);

        foreach (var entries in new[] { builder.Build(new object()), builder.Build(null) })
        {
            Assert.NotNull(entries);
            // Copy Page joined the menu with the comfort slice (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md section 2); the spread items only appear while a spread is showing. The image quality slice added
            // "Auto-crop this page" (2026-09-26 #3) and the in-reader reference slice "Add Note…", "Clip a Region…" and "Pin this page as a reference" (2026-09-26 #7, #29).
            Assert.Equal(
                new[] { "Copy Page", "Add Note…", "Clip a Region…", "Pin this page as a reference", "Save Page as PNG…", "Save Page as JPEG…", "Auto-crop this page", "Report Bad Page…" },
                entries!.Select(e => e.Header).ToArray());
            Assert.Same(vm.CopyPageCommand, entries[0].Command);
            Assert.Same(vm.NoteOnThisPageCommand, entries[1].Command);
            Assert.Same(vm.ToggleClipModeCommand, entries[2].Command);
            Assert.Same(vm.PinCurrentPageCommand, entries[3].Command);
            Assert.Same(vm.SavePageAsPngCommand, entries[4].Command);
            Assert.Same(vm.SavePageAsJpegCommand, entries[5].Command);
            Assert.Same(vm.ReportBadPageCommand, entries[7].Command);
        }
    }
}
