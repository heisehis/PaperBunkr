using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>The pinned reference page (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #29): the pure geometry, the copy and its lifetime, and dragging it on the real screen.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderPinTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pin_test_{Guid.NewGuid():N}.db");
    private readonly string _cbzA = Path.Combine(Path.GetTempPath(), $"paperbunkr_pin_test_a_{Guid.NewGuid():N}.cbz");
    private readonly string _cbzB = Path.Combine(Path.GetTempPath(), $"paperbunkr_pin_test_b_{Guid.NewGuid():N}.cbz");
    private readonly int _issueA;
    private readonly int _issueB;

    public ReaderPinTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        CbzFixture.Create(_cbzA, pageCount: 4);
        CbzFixture.Create(_cbzB, pageCount: 2);
        var series = new Series { Name = "Pin" };
        context.Series.Add(series);
        context.SaveChanges();
        var a = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzA };
        var b = new Issue { SeriesId = series.Id, Number = "2", FilePath = _cbzB };
        context.Issues.AddRange(a, b);
        context.SaveChanges();
        _issueA = a.Id;
        _issueB = b.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string path in new[] { _dbPath, _cbzA, _cbzB })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    // ===== Pure geometry =====

    [Fact]
    public void Sizes_CycleAndStepAndHaveWidths()
    {
        Assert.Equal(160, ReaderPinMath.WidthFor(PinSize.Small));
        Assert.Equal(260, ReaderPinMath.WidthFor(PinSize.Medium));
        Assert.Equal(400, ReaderPinMath.WidthFor(PinSize.Large));
        Assert.Equal(PinSize.Small, ReaderPinMath.Next(PinSize.Large));
        Assert.Equal(PinSize.Large, ReaderPinMath.Next(PinSize.Medium));
        Assert.Equal(PinSize.Large, ReaderPinMath.Step(PinSize.Large, up: true));      // the ends hold
        Assert.Equal(PinSize.Small, ReaderPinMath.Step(PinSize.Small, up: false));
        Assert.Equal(PinSize.Small, ReaderPinMath.Step(PinSize.Medium, up: false));
    }

    [Theory]
    [InlineData(100, 100, PinCorner.TopLeft)]
    [InlineData(800, 100, PinCorner.TopRight)]
    [InlineData(100, 600, PinCorner.BottomLeft)]
    [InlineData(800, 600, PinCorner.BottomRight)]
    public void ADragReleasedAnywhere_LandsInTheNearestCorner(double x, double y, PinCorner expected) =>
        Assert.Equal(expected, ReaderPinMath.NearestCorner(new Point(x, y), new Size(900, 700)));

    [Fact]
    public void TheCopy_KeepsTheAspectRatio_AndIsNeverEnlarged()
    {
        Assert.Equal(new PixelSize(400, 600), ReaderPinMath.CopySize(new PixelSize(1200, 1800)));   // long side 600
        Assert.Equal(new PixelSize(64, 96), ReaderPinMath.CopySize(new PixelSize(64, 96)));          // a small page stays as it is
        Assert.Equal(new PixelSize(600, 300), ReaderPinMath.CopySize(new PixelSize(2400, 1200)));
        Assert.Equal(new PixelSize(1, 1), ReaderPinMath.CopySize(default));
    }

    [Fact]
    public void TheCorners_LeaveRoomForTheChromeClusters()
    {
        Assert.True(ReaderPinMath.MarginFor(PinCorner.TopRight).Top >= 64);                // below the navigate and actions clusters
        Assert.True(ReaderPinMath.MarginFor(PinCorner.BottomLeft).Bottom >= 90);           // above the view cluster and page-turn strip
    }

    // ===== The view model =====

    [Fact]
    public void Pinning_TakesACopyOfTheDisplayedPage_WithACaption_AndUnpinClearsIt()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueA);
        vm.GoToPage(2);
        var toasts = new List<Models.ToastRequest>();
        vm.ToastRequested += toasts.Add;

        vm.PinCurrentPageCommand.Execute(null);

        Assert.True(vm.HasPin);
        Assert.NotNull(vm.PinnedImage);
        Assert.NotSame(vm.CurrentPage, vm.PinnedImage);                     // a copy: the pipeline cannot drop it
        Assert.Contains("page 3", vm.PinnedCaption);
        Assert.Contains(toasts, t => t.Title == "Page pinned");

        vm.UnpinPageCommand.Execute(null);
        Assert.False(vm.HasPin);
        Assert.Null(vm.PinnedCaption);
    }

    [Fact]
    public void ThePin_SurvivesAnotherIssueAndPaging_AndIsClearedWhenTheReaderIsLeft()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueA);
        vm.PinCurrentPageCommand.Execute(null);
        var pinned = vm.PinnedImage;

        vm.GoToPage(1);
        vm.LoadIssue(_issueB);                                              // the recap-page case: read on into the next issue

        Assert.Same(pinned, vm.PinnedImage);
        Assert.True(vm.HasPin);

        vm.GoBackCommand.Execute(null);
        Assert.False(vm.HasPin);
    }

    [Fact]
    public void PinningAgain_ReplacesThePin()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueA);
        vm.PinCurrentPageCommand.Execute(null);
        var first = vm.PinnedImage;

        vm.GoToPage(1);
        vm.PinCurrentPageCommand.Execute(null);

        Assert.NotSame(first, vm.PinnedImage);
        Assert.Contains("page 2", vm.PinnedCaption);
    }

    [Fact]
    public void ThePin_IsHiddenWhileTheInfoPanelIsOpen_AndSizeAndCornerFollowTheCommands()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        vm.LoadIssue(_issueA);
        vm.PinCurrentPageCommand.Execute(null);
        Assert.True(vm.IsPinShown);

        vm.ToggleInfoPanelCommand.Execute(null);
        Assert.False(vm.IsPinShown);
        vm.ToggleInfoPanelCommand.Execute(null);
        Assert.True(vm.IsPinShown);

        Assert.Equal(260, vm.PinWidth);
        vm.CyclePinSizeCommand.Execute(null);
        Assert.Equal(400, vm.PinWidth);

        vm.SetPinCorner(PinCorner.BottomLeft);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, vm.PinHorizontalAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom, vm.PinVerticalAlignment);
    }

    [Fact]
    public void ThePalette_ThePageMenuAndTheKey_OfferThePin()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }, new InputService(InputActionCatalog.CreateWithCoreActions(), new MemoryKeymapStore()));
        vm.LoadIssue(_issueA);

        Assert.Contains(vm.BuildPaletteEntries(), e => e.Title == "Pin this page as a reference");
        Assert.Equal([InputBinding.ForKey(Key.P, KeyModifiers.Shift)], vm.Input.GetBindings(InputActionIds.PinPage).Take(1));
        Assert.Contains(new ReaderPageContextMenuBuilder(vm).Build(null)!, e => e.Header == "Pin this page as a reference");

        vm.PinCurrentPageCommand.Execute(null);
        Assert.Contains(new ReaderPageContextMenuBuilder(vm).Build(null)!, e => e.Header == "Unpin reference page");
    }

    // ===== On screen =====

    [Fact]
    public void ShiftP_PinsThePage_AndDraggingItSnapsToTheNearestCorner()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }, ReaderTestInput.Create());
        var (window, screen, canvas) = ReaderScreenTestHost.Open(vm, _issueA);
        try
        {
            ReaderScreenTestHost.Press(window, Key.P, RawInputModifiers.Shift);
            ReaderScreenTestHost.Pump(window, () => vm.HasPin);
            Assert.True(vm.HasPin);
            Assert.Equal(PinCorner.TopRight, vm.PinCorner);
            window.UpdateLayout();

            var panel = screen.PinPanel;
            var centre = panel.TranslatePoint(new Point(panel.Bounds.Width / 2, panel.Bounds.Height / 2), window)!.Value;
            var target = new Point(120, 600);                                 // bottom-left of the 900x700 window
            window.MouseMove(centre);
            window.MouseDown(centre, MouseButton.Left);
            window.MouseMove(new Point((centre.X + target.X) / 2, (centre.Y + target.Y) / 2));
            window.MouseMove(target);
            window.MouseUp(target, MouseButton.Left);
            TestDispatcher.Drain();

            Assert.Equal(PinCorner.BottomLeft, vm.PinCorner);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ThePinDoesNotTakeClicksMeantForThePage()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }, ReaderTestInput.Create());
        var (window, screen, canvas) = ReaderScreenTestHost.Open(vm, _issueA);
        try
        {
            vm.PinCurrentPageCommand.Execute(null);
            window.UpdateLayout();
            var pin = screen.PinPanel.TranslatePoint(new Point(0, 0), window)!.Value;
            var below = new Point(pin.X + 40, pin.Y + screen.PinPanel.Bounds.Height + 60);   // right half of the page, under the pin
            Assert.True(below.Y < 690, $"pin at {pin}, height {screen.PinPanel.Bounds.Height}");
            Assert.Equal("PAGE 1 / 4", vm.PageLabel);

            window.MouseMove(below);
            window.MouseDown(below, MouseButton.Left);
            window.MouseUp(below, MouseButton.Left);
            ReaderScreenTestHost.Pump(window, () => vm.PageLabel == "PAGE 2 / 4");

            Assert.Equal("PAGE 2 / 4", vm.PageLabel);                           // the click went through to the page
            Assert.True(vm.HasPin);
        }
        finally
        {
            window.Close();
        }
    }
}
