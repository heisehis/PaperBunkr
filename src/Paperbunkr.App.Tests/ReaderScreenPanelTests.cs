using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Reader.Panels;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Guided panel view and smart double-click through the real screen and canvas, driven by key presses and mouse clicks (docs/superpowers/specs/2026-09-25-comic-reader-panels-and-zoom-design.md
/// sections 3 and 4). Detection is faked with a known 2x2 grid; how the real detector does on real pages is the user's on-screen check.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReaderScreenPanelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_panel_test_{Guid.NewGuid():N}.db");
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_panel_test_{Guid.NewGuid():N}.cbz");
    private readonly int _issueId;
    private readonly int _bigIssueId;
    private readonly string _bigCbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_panel_test_big_{Guid.NewGuid():N}.cbz");

    /// <summary>A 2x2 grid of panels with a gutter around 0.5 in both directions.</summary>
    private static readonly PagePanels Grid = new(
        [new PanelRect(0.05, 0.05, 0.42, 0.42), new PanelRect(0.53, 0.05, 0.42, 0.42), new PanelRect(0.05, 0.53, 0.42, 0.42), new PanelRect(0.53, 0.53, 0.42, 0.42)], true);

    public ReaderScreenPanelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        CbzFixture.Create(_cbzPath, pageCount: 3);
        var series = new Series { Name = "Panels" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzPath };
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;

        // Pages bigger than the test window, so a zoomed page really overflows it (the reader never scales a small page up).
        CbzFixture.Create(_bigCbzPath, pageCount: 3, pageSize: _ => new System.Drawing.Size(1200, 1800));
        var big = new Issue { SeriesId = series.Id, Number = "2", FilePath = _bigCbzPath };
        context.Issues.Add(big);
        context.SaveChanges();
        _bigIssueId = big.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (string path in new[] { _dbPath, _cbzPath, _bigCbzPath })
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

    // The headless app does not carry App.axaml's design tokens (see ReaderScreenKeyTests).
    private static ReaderScreen CreateScreen(ReaderScreenViewModel vm)
    {
        var resources = Application.Current!.Resources;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                return new ReaderScreen { DataContext = vm };
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex.InnerException is KeyNotFoundException)
            {
                var match = Regex.Match(ex.ToString(), @"Static resource '([^']+)' not found");
                if (!match.Success)
                {
                    throw;
                }

                string key = match.Groups[1].Value;
                resources[key] = key switch
                {
                    _ when key.Contains("Ease") => new Avalonia.Animation.Easings.CubicEaseOut(),
                    _ when key.Contains("Motion") || key.Contains("Duration") => TimeSpan.FromMilliseconds(150),
                    _ when key.Contains("Radius") => new CornerRadius(4),
                    _ when key.Contains("Brush") => new SolidColorBrush(Colors.Gray),
                    _ when key.Contains("Thickness") || key.Contains("Padding") => new Thickness(4),
                    _ => 12.0,
                };
            }
        }

        throw new InvalidOperationException("Too many missing resources.");
    }

    private (Window Window, ReaderScreenViewModel Vm, PageCanvas Canvas) Open(bool smartDoubleClick = true, bool bigPages = false)
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }, ReaderTestInput.Create()) { PanelAnalyzer = (_, _) => Grid };
        var screen = CreateScreen(vm);
        var window = new Window { Width = 900, Height = 700, Content = screen };
        InputHost.Attach(window, vm.Input);   // what MainWindow does for the app
        window.Show();
        vm.LoadIssue(bigPages ? _bigIssueId : _issueId);
        vm.SmartDoubleClickZoom = smartDoubleClick;
        screen.PageCanvasControl.Focus();
        Pump(window, () => screen.PageCanvasControl.Page is not null && screen.PageCanvasControl.Bounds.Width > 0);
        return (window, vm, screen.PageCanvasControl);
    }

    private static void Pump(Window window, Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            TestDispatcher.Drain();
            window.UpdateLayout();
            Thread.Sleep(10);
        }

        TestDispatcher.Drain();
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        string physical = key is Key.Left or Key.Right or Key.Up or Key.Down ? $"Arrow{key}" : key.ToString();
        window.KeyPress(key, modifiers, (PhysicalKey)Enum.Parse(typeof(PhysicalKey), physical), null);
    }

    private static void Settle(PageCanvas canvas)
    {
        TestDispatcher.Drain();
        canvas.CompleteTweenForTest();
        TestDispatcher.Drain();
    }

    [Fact]
    public void G_FramesTheFirstPanel_AndLabelsIt()
    {
        var (window, vm, canvas) = Open();
        try
        {
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);

            Assert.True(vm.IsGuidedView);
            Assert.Equal("Panel 1/4", canvas.PartLabel);
            Assert.True(canvas.ZoomLevel > 1.5);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void NextPage_StepsThroughThePanelsInOrder_ThenTurnsThePage_LandingOnItsFirstPanel()
    {
        var (window, vm, canvas) = Open();
        try
        {
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);

            for (int expected = 2; expected <= 4; expected++)
            {
                Press(window, Key.PageDown);
                Settle(canvas);
                Assert.Equal($"Panel {expected}/4", canvas.PartLabel);
                Assert.Equal("PAGE 1 / 3", vm.PageLabel);          // still on page 1
            }

            Press(window, Key.PageDown);                            // past the last panel: a real page turn
            Pump(window, () => vm.PageLabel == "PAGE 2 / 3");
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);

            Assert.Equal("PAGE 2 / 3", vm.PageLabel);
            Assert.Equal("Panel 1/4", canvas.PartLabel);            // lands on the new page's first panel
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void PreviousPage_FromTheFirstPanel_GoesBackAPage_LandingOnItsLastPanel()
    {
        var (window, vm, canvas) = Open();
        try
        {
            vm.GoToPage(1);
            Pump(window, () => canvas.Page is not null && vm.PageLabel == "PAGE 2 / 3");
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);
            Assert.Equal("Panel 1/4", canvas.PartLabel);

            Press(window, Key.PageUp);
            Pump(window, () => vm.PageLabel == "PAGE 1 / 3");
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);

            Assert.Equal("PAGE 1 / 3", vm.PageLabel);
            Assert.Equal("Panel 4/4", canvas.PartLabel);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ManualZoom_ThenNext_ContinuesFromTheNearestPanel_NotFromTheStaleOne()
    {
        var (window, vm, canvas) = Open();
        try
        {
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);
            Assert.Equal("Panel 1/4", canvas.PartLabel);

            vm.ZoomLevel = 1.0;                                     // the user resets the view by hand (slider)
            Settle(canvas);
            Press(window, Key.PageDown);
            Settle(canvas);

            Assert.Equal("Panel 1/4", canvas.PartLabel);            // from the whole page, "next" goes to the first panel
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void TurningGuidedViewOff_ReturnsToTheWholePage_AndDropsTheLabel()
    {
        var (window, vm, canvas) = Open();
        try
        {
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);
            Assert.True(canvas.ZoomLevel > 1.5);

            Press(window, Key.G);
            Settle(canvas);

            Assert.False(vm.IsGuidedView);
            Assert.Equal(1.0, canvas.ZoomLevel, 6);
            Assert.True(string.IsNullOrEmpty(canvas.PartLabel));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void GuidedView_WithAnUnconfidentPage_StepsPastItInOnePress()
    {
        var (window, vm, canvas) = Open();
        try
        {
            vm.PanelAnalyzer = (_, _) => PagePanels.Whole;
            vm.GoToPage(1);
            Pump(window, () => vm.PageLabel == "PAGE 2 / 3");
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);
            Assert.Equal("No panels found", canvas.PartLabel);      // no "Panel 1/1" for a page the detector gave up on, but the label says why nothing was framed
            Assert.True(Math.Abs(canvas.ZoomLevel - 1.0) < 1e-6, $"zoom {canvas.ZoomLevel}, label '{canvas.PartLabel}', panels {vm.CurrentPagePanels?.Count}, guided {vm.IsGuidedView}, fit {canvas.FitMode}");

            Press(window, Key.PageDown);
            Pump(window, () => vm.PageLabel == "PAGE 3 / 3");

            Assert.Equal("PAGE 3 / 3", vm.PageLabel);
        }
        finally
        {
            window.Close();
        }
    }

    // ===== Smart double-click =====

    private static Point PagePointInWindow(Window window, PageCanvas canvas, double fx, double fy)
    {
        var rect = canvas.GetPageScreenRect();
        var inCanvas = new Point(rect.X + (rect.Width * fx), rect.Y + (rect.Height * fy));
        return canvas.TranslatePoint(inCanvas, window) ?? inCanvas;
    }

    private static void DoubleClick(Window window, Point point)
    {
        Thread.Sleep(600);   // past the double-click interval, so this pair of presses starts a fresh click count
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    [Fact]
    public void DoubleClick_OnAPanel_ZoomsToIt_AndASecondDoubleClickReturns()
    {
        var (window, vm, canvas) = Open();
        try
        {
            DoubleClick(window, PagePointInWindow(window, canvas, 0.25, 0.25));
            Settle(canvas);

            Assert.True(canvas.ZoomLevel > 1.5, $"zoom {canvas.ZoomLevel}");
            Assert.NotNull(vm.CurrentPagePanels);                    // detected on demand, guided view was off

            DoubleClick(window, PagePointInWindow(window, canvas, 0.25, 0.25));
            Settle(canvas);

            Assert.Equal(1.0, canvas.ZoomLevel, 6);
            Assert.Equal(0, canvas.PanOffsetX, 6);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void DoubleClick_InAGutter_FallsBackToThePlainTwoHundredPercent()
    {
        var (window, _, canvas) = Open();
        try
        {
            DoubleClick(window, PagePointInWindow(window, canvas, 0.5, 0.5));
            Settle(canvas);

            Assert.Equal(ZoomPanMath.DoubleClickZoom, canvas.ZoomLevel, 6);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void DoubleClick_WithSmartZoomOff_IsTheOldTwoHundredPercent_EvenOnAPanel()
    {
        var (window, vm, canvas) = Open(smartDoubleClick: false);
        try
        {
            DoubleClick(window, PagePointInWindow(window, canvas, 0.25, 0.25));
            Settle(canvas);

            Assert.Equal(ZoomPanMath.DoubleClickZoom, canvas.ZoomLevel, 6);
            Assert.Null(vm.CurrentPagePanels);                       // and nothing was analysed
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void DoubleClick_InGuidedView_TogglesBetweenThePanelAndTheWholePage()
    {
        var (window, vm, canvas) = Open();
        try
        {
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);
            Assert.True(canvas.ZoomLevel > 1.5, $"after G: zoom {canvas.ZoomLevel}");

            DoubleClick(window, PagePointInWindow(window, canvas, 0.5, 0.5));
            Settle(canvas);
            Assert.True(Math.Abs(canvas.ZoomLevel - 1.0) < 1e-6, $"after first double-click: zoom {canvas.ZoomLevel}, label '{canvas.PartLabel}'");   // panel -> whole page

            DoubleClick(window, PagePointInWindow(window, canvas, 0.75, 0.75));
            Settle(canvas);
            Assert.True(canvas.ZoomLevel > 1.5, $"after second double-click: zoom {canvas.ZoomLevel}, label '{canvas.PartLabel}'");   // whole page -> the panel under the pointer
            Assert.Equal("Panel 4/4", canvas.PartLabel);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void DoubleClick_OnTheRightHalf_DoesNotLeaveThePageTurned()
    {
        // The first click of a double-click lands on the right-half tap zone and turns the page at once; the double-click must put that back.
        var (window, vm, canvas) = Open();
        try
        {
            DoubleClick(window, PagePointInWindow(window, canvas, 0.75, 0.25));
            Settle(canvas);

            Assert.Equal("PAGE 1 / 3", vm.PageLabel);
            Assert.True(canvas.ZoomLevel > 1.5, $"zoom {canvas.ZoomLevel}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void DoubleClick_WithSmartZoomOff_AlsoNoLongerLeavesThePageTurned()
    {
        var (window, vm, canvas) = Open(smartDoubleClick: false);
        try
        {
            DoubleClick(window, PagePointInWindow(window, canvas, 0.75, 0.25));
            Settle(canvas);

            Assert.Equal("PAGE 1 / 3", vm.PageLabel);
            Assert.Equal(ZoomPanMath.DoubleClickZoom, canvas.ZoomLevel, 6);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SingleClick_OnTheRightHalf_StillTurnsThePageAtOnce()
    {
        var (window, vm, canvas) = Open();
        try
        {
            var point = PagePointInWindow(window, canvas, 0.75, 0.25);
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump(window, () => vm.PageLabel == "PAGE 2 / 3");

            Assert.Equal("PAGE 2 / 3", vm.PageLabel);   // page turns are not delayed waiting to see whether a second click follows
        }
        finally
        {
            window.Close();
        }
    }

    // ===== Show detected panels (tuning overlay) =====

    [Fact]
    public void ShowDetectedPanels_FlashesTheNumberedRectangles_OverThePage_AndExpires()
    {
        var (window, vm, canvas) = Open();
        try
        {
            var screen = (ReaderScreen)window.Content!;
            var overlay = screen.GetVisualDescendants().OfType<Paperbunkr.App.Controls.PanelDebugOverlay>().Single();

            vm.ShowDetectedPanelsCommand.Execute(null);
            Pump(window, () => vm.IsPanelFlashVisible);

            Assert.True(vm.IsPanelFlashVisible);
            Assert.True(overlay.IsEffectivelyVisible);
            Assert.Equal(4, overlay.Panels!.Count);
            Assert.True(overlay.Confident);
            Assert.Equal(canvas.GetPageScreenRect(), overlay.PageRect);

            vm.OnPanelFlashExpired(null, EventArgs.Empty);

            Assert.False(vm.IsPanelFlashVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ShowDetectedPanels_OnAPageTheDetectorGaveUpOn_ShowsTheUnconfidentCaption()
    {
        var (window, vm, canvas) = Open();
        try
        {
            vm.PanelAnalyzer = (_, _) => PagePanels.Whole;
            var screen = (ReaderScreen)window.Content!;
            var overlay = screen.GetVisualDescendants().OfType<Paperbunkr.App.Controls.PanelDebugOverlay>().Single();

            vm.ShowDetectedPanelsCommand.Execute(null);
            Pump(window, () => vm.IsPanelFlashVisible);

            Assert.False(overlay.Confident);
            Assert.Single(overlay.Panels!);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ShowDetectedPanels_InContinuousMode_SaysSoInsteadOfShowingNothing()
    {
        var (window, vm, _) = Open();
        try
        {
            var toasts = new List<Paperbunkr.App.Models.ToastRequest>();
            vm.ToastRequested += toasts.Add;
            vm.SetReadingModeCommand.Execute(ReadingMode.VerticalContinuous);

            vm.ShowDetectedPanelsCommand.Execute(null);

            Assert.False(vm.IsPanelFlashVisible);
            Assert.Equal("Panels are detected in paged reading", Assert.Single(toasts).Title);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Overlay_ScalesPanelFractionsOntoThePageRectangle()
    {
        var rects = Paperbunkr.App.Controls.PanelDebugOverlay.ToScreenRects(new Rect(100, 50, 400, 600), [new PanelRect(0.25, 0.5, 0.5, 0.25)]);

        Assert.Equal(new Rect(200, 350, 200, 150), Assert.Single(rects));
    }

    [Fact]
    public void ShowDetectedPanels_IsInThePalette()
    {
        var (window, vm, _) = Open();
        try
        {
            Assert.Contains(vm.BuildPaletteEntries(), e => e.Title == "Show detected panels");
        }
        finally
        {
            window.Close();
        }
    }

    // ===== Report bad panel detection =====

    [Fact]
    public void ReportBadPanels_SavesThePageAndTheDetection_AndIsInThePalette()
    {
        var (window, vm, _) = Open();
        string root = Path.Combine(Path.GetTempPath(), $"panel-report-test-{Guid.NewGuid():N}");
        try
        {
            vm.PanelReportFolder = () => root;
            var toasts = new List<Paperbunkr.App.Models.ToastRequest>();
            vm.ToastRequested += toasts.Add;
            Assert.Contains(vm.BuildPaletteEntries(), e => e.Title.StartsWith("Report bad panel detection"));

            vm.ReportBadPanelsCommand.Execute(null);

            var folder = Assert.Single(Directory.GetDirectories(root));
            Assert.True(File.Exists(Path.Combine(folder, "page.png")));
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "detection.json")));
            Assert.Equal(4, json.RootElement.GetProperty("panels").GetArrayLength());
            Assert.Equal("Page saved for panel tuning", Assert.Single(toasts).Title);
        }
        finally
        {
            window.Close();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // ===== Arrow keys and the wheel in guided view; turning the page from a zoomed page; the zoom glide =====

    [Fact]
    public void RightArrow_InGuidedView_StepsToTheNextPanel_InsteadOfPanning()
    {
        var (window, vm, canvas) = Open();
        try
        {
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);
            Assert.Equal("Panel 1/4", canvas.PartLabel);
            double zoomBefore = canvas.ZoomLevel;

            Press(window, Key.Right);
            Settle(canvas);

            Assert.Equal("Panel 2/4", canvas.PartLabel);
            Assert.Equal(zoomBefore, canvas.ZoomLevel, 6);          // stepped to the next panel, at the same zoom
            Press(window, Key.Down);
            Settle(canvas);
            Assert.Equal("Panel 3/4", canvas.PartLabel);
            Press(window, Key.Left);
            Settle(canvas);
            Assert.Equal("Panel 2/4", canvas.PartLabel);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void GuidedView_OnA_LongStrip_StepsThroughScreenSizedSlicesOfEachPanel()
    {
        var (window, vm, canvas) = Open(bigPages: true);
        try
        {
            Assert.Equal(ImageFitMode.FitWidth, canvas.FitMode);    // the default: a 2:3 page is then 1.5 screens tall in a 700 px window
            vm.PanelAnalyzer = (_, _) => new PagePanels([PanelRect.WholePage], true);
            vm.GoToPage(1);
            Pump(window, () => vm.PageLabel == "PAGE 2 / 3");
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);

            Assert.Matches(@"^Panel 1/[2-9]\d*$", canvas.PartLabel);   // the whole-page "panel" is bigger than the screen, so it is more than one step
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ZoomButtonsAndKeys_GlideToTheirGoal_AndAccumulate()
    {
        var (window, vm, canvas) = Open();
        try
        {
            canvas.SmoothZoomBy(1.5);
            Assert.Equal(1.0, canvas.ZoomLevel, 6);                 // nothing jumped
            Assert.Equal(1.5, canvas.ZoomGoal!.Value, 6);

            canvas.SmoothZoomBy(1.5);
            Assert.Equal(2.25, canvas.ZoomGoal!.Value, 6);          // a second notch adds to the goal instead of restarting

            canvas.CompleteZoomForTest();
            Assert.Equal(2.25, canvas.ZoomLevel, 6);
            Assert.Null(canvas.ZoomGoal);
            Assert.Equal(2.25, vm.ZoomLevel, 6);                    // the view model followed
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ZoomGlide_IsClampedToTheRange()
    {
        var (window, _, canvas) = Open();
        try
        {
            canvas.SmoothZoomBy(100);
            Assert.Equal(ZoomPanMath.MaxZoom, canvas.ZoomGoal!.Value, 6);

            canvas.SmoothZoomTo(0.001);
            Assert.Equal(ZoomPanMath.MinZoom, canvas.ZoomGoal!.Value, 6);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void AZoomChangeFromElsewhere_EndsTheGlide()
    {
        var (window, vm, canvas) = Open();
        try
        {
            canvas.SmoothZoomBy(2.0);
            vm.ZoomLevel = 1.7;                                     // the slider

            Assert.Null(canvas.ZoomGoal);
            Assert.Equal(1.7, canvas.ZoomLevel, 6);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ZoomInCommand_GlidesWhenTheScreenIsListening_AndJumpsWhenNobodyIs()
    {
        var (window, vm, canvas) = Open();
        try
        {
            vm.ZoomInCommand.Execute(null);
            Assert.True(canvas.ZoomGoal is > 1.05);                 // routed to the canvas, which glides
            Assert.Equal(1.0, vm.ZoomLevel, 6);

            canvas.CompleteZoomForTest();
            Assert.Equal(ZoomPanMath.KeyZoomFactor, vm.ZoomLevel, 6);

            vm.ResetZoomCommand.Execute(null);
            Settle(canvas);
            Assert.Equal(1.0, vm.ZoomLevel, 6);
        }
        finally
        {
            window.Close();
        }

        var alone = new ReaderScreenViewModel(goBack: () => { });    // no screen: nothing to glide with
        alone.ZoomInCommand.Execute(null);
        Assert.Equal(ZoomPanMath.KeyZoomFactor, alone.ZoomLevel, 6);
    }

    /// <summary>Zooms the page and puts the view against the given edges of it, so the next key or scroll that way finds nothing left to pan.</summary>
    private static void ZoomTo(ReaderScreenViewModel vm, PageCanvas canvas, double zoom, double towardsX, double towardsY)
    {
        vm.ZoomLevel = zoom;
        TestDispatcher.Drain();
        var (x, y) = ZoomPanMath.ClampPan(canvas.Bounds.Size, canvas.Page!.PixelSize, zoom, towardsX * 1e6, towardsY * 1e6, canvas.FitMode, fitOnlyIfOversized: true);
        canvas.PanOffsetX = x;
        canvas.PanOffsetY = y;
        Settle(canvas);
    }

    [Fact]
    public void ArrowKey_AtTheEdgeOfAZoomedPage_TurnsThePage()
    {
        var (window, vm, canvas) = Open(bigPages: true);
        try
        {
            ZoomTo(vm, canvas, 2.0, towardsX: -1, towardsY: -1);     // bottom-right corner: the last part of the page
            Assert.Equal("PAGE 1 / 3", vm.PageLabel);

            Thread.Sleep(150);                                      // a fresh press, not a key repeat
            Press(window, Key.Right);
            Pump(window, () => vm.PageLabel == "PAGE 2 / 3");

            Assert.Equal("PAGE 2 / 3", vm.PageLabel);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ArrowKey_WhileStillPanning_PansAndDoesNotTurnThePage()
    {
        var (window, vm, canvas) = Open(bigPages: true);
        try
        {
            ZoomTo(vm, canvas, 3.0, towardsX: 0, towardsY: 0);
            double before = canvas.PanOffsetX;

            Thread.Sleep(150);
            Press(window, Key.Right);
            TestDispatcher.Drain();

            Assert.Equal("PAGE 1 / 3", vm.PageLabel);
            Assert.True(canvas.PanOffsetX < before, $"pan {before} -> {canvas.PanOffsetX}");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ArrowKeys_HeldDownAtTheEdge_TurnOnlyOnePage()
    {
        var (window, vm, canvas) = Open(bigPages: true);
        try
        {
            ZoomTo(vm, canvas, 2.0, towardsX: -1, towardsY: -1);
            Thread.Sleep(150);
            for (int i = 0; i < 10; i++)
            {
                Press(window, Key.Right);                           // a key repeat: presses a few milliseconds apart
                TestDispatcher.Drain();
            }

            Assert.Equal("PAGE 2 / 3", vm.PageLabel);               // the first press turned; the repeats did not race on through the book
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Wheel_AtTheEdge_TurnsThePage_OnlyForAFreshScroll()
    {
        var (window, vm, canvas) = Open(bigPages: true);
        try
        {
            ZoomTo(vm, canvas, 2.0, towardsX: 0, towardsY: -1);
            canvas.PanOffsetY += 100;                               // 100 px above the bottom edge
            Settle(canvas);
            var at = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;

            // A long run of scrolling reaches the bottom and carries on: that run must not spill into the next page.
            for (int i = 0; i < 200; i++)
            {
                window.MouseWheel(at, new Vector(0, -1));
            }

            TestDispatcher.Drain();
            Assert.True(vm.PageLabel == "PAGE 1 / 3", $"{vm.PageLabel}: pan {canvas.PanOffsetX},{canvas.PanOffsetY} step {canvas.WheelPanStep}");

            // After a pause, one more scroll is a fresh gesture: it turns the page (through the part grid first, which reads on to the next part).
            for (int i = 0; i < 6 && vm.PageLabel == "PAGE 1 / 3"; i++)
            {
                Thread.Sleep(400);
                window.MouseWheel(at, new Vector(0, -1));
                TestDispatcher.Drain();
            }

            Assert.Equal("PAGE 2 / 3", vm.PageLabel);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheBackButtonAppearsAndLeavesTheReader_WithOrWithoutGuidedView(bool guided)
    {
        int left = 0;
        var vm = new ReaderScreenViewModel(goBack: () => left++, ReaderTestInput.Create()) { PanelAnalyzer = (_, _) => Grid };
        var screen = CreateScreen(vm);
        var window = new Window { Width = 900, Height = 700, Content = screen };
        InputHost.Attach(window, vm.Input);   // what MainWindow does for the app
        window.Show();
        try
        {
            vm.LoadIssue(_bigIssueId);
            var canvas = screen.PageCanvasControl;
            canvas.Focus();
            Pump(window, () => canvas.Page is not null && canvas.Bounds.Width > 0);
            if (guided)
            {
                Press(window, Key.G);
                Pump(window, () => vm.CurrentPagePanels is not null);
                Settle(canvas);
                Assert.True(canvas.ZoomLevel > 1.0);
            }

            window.MouseMove(new Point(60, 30));
            TestDispatcher.Drain();
            Assert.True(vm.IsNavigateClusterVisible, "the back cluster did not appear when the pointer went to the top-left");

            var back = screen.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, vm.GoBackCommand) && b.Content is not string);   // the top-left breadcrumb, not the end card's pill
            var centre = back.TranslatePoint(new Point(back.Bounds.Width / 2, back.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre);
            TestDispatcher.Drain();
            Assert.True(vm.IsNavigateClusterVisible, $"the cluster hid again when the pointer moved onto the button at {centre}");
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            TestDispatcher.Drain();

            Assert.Equal(1, left);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ClickingInGuidedView_StepsPanels_LikeTheKeys_AndADragStillPans()
    {
        var (window, vm, canvas) = Open(bigPages: true);
        try
        {
            Press(window, Key.G);
            Pump(window, () => vm.CurrentPagePanels is not null);
            Settle(canvas);
            Assert.Equal("Panel 1/4", canvas.PartLabel);

            var right = new Point(canvas.Bounds.Width * 0.8, canvas.Bounds.Height * 0.5);
            var rightInWindow = canvas.TranslatePoint(right, window)!.Value;
            window.MouseMove(rightInWindow);
            window.MouseDown(rightInWindow, MouseButton.Left);
            window.MouseUp(rightInWindow, MouseButton.Left);
            Settle(canvas);
            Assert.Equal("Panel 2/4", canvas.PartLabel);              // a click on the right half steps forward

            // A drag is still a pan, not a step.
            Thread.Sleep(600);
            double before = canvas.PanOffsetX;
            window.MouseDown(rightInWindow, MouseButton.Left);
            window.MouseMove(rightInWindow + new Point(30, 0));
            window.MouseUp(rightInWindow + new Point(30, 0), MouseButton.Left);
            Settle(canvas);
            Assert.Equal("Panel 2/4", canvas.PartLabel);
            Assert.NotEqual(before, canvas.PanOffsetX);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void TurningGuidedViewOn_ExplainsItself_WithAToast()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { });
        var toasts = new List<Paperbunkr.App.Models.ToastRequest>();
        vm.ToastRequested += toasts.Add;

        vm.ToggleGuidedViewCommand.Execute(null);

        var toast = Assert.Single(toasts);
        Assert.Equal("Guided view on", toast.Title);
        Assert.Contains("panels", toast.Message);

        vm.ToggleGuidedViewCommand.Execute(null);
        Assert.Single(toasts);                                      // turning it off says nothing
    }
}
