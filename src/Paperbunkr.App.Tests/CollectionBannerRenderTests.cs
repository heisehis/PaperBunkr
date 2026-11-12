using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The collection banner and empty-state card in the real Library (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md). With PB_RENDER_OUT set it
/// also writes PNGs of the window for a human look.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CollectionBannerRenderTests : IDisposable
{
    private readonly string? _original = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pb_colbanner_{Guid.NewGuid():N}.db");
    private readonly CoverCacheTestRedirect _cover = new();

    public CollectionBannerRenderTests()
    {
        PaperbunkrDbContext.DatabasePathOverride = _db;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();

        var series = new Series { Name = "Reading", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        series.Issues.Add(new Issue { Number = "1", FilePath = "C:\\x\\1.cbz", LastPageRead = 4 });
        series.Issues.Add(new Issue { Number = "2", FilePath = "C:\\x\\2.cbz" });
        context.Series.Add(series);
        context.SaveChanges();

        var filled = CollectionService.Create(context, "Event books");
        CollectionService.SetAppearance(context, filled.Id, "The crossovers I want to read **in order**. See [the guide](https://example.com/guide).", "#C97C9E", null, true);
        CollectionService.SetIdentity(context, filled.Id, "Rocket", CollectionCoverStyle.Auto, null, isPinned: true);
        CollectionService.AddItems(context, filled.Id, seriesIds: new[] { series.Id });

        var empty = CollectionService.Create(context, "Waiting list");
        CollectionService.SetAppearance(context, empty.Id, null, "#7C93C9", null, true);
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        _cover.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    private static MainWindow Open(out MainViewModel vm)
    {
        vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();
        vm.GoLibraryCommand.Execute(null);
        vm.Library.LoadFromDatabase();
        RunLayout(window);
        return window;
    }

    private static void Snap(Window window, string name)
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        var rtb = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        rtb.Render(window);
        rtb.Save(Path.Combine(outDir, name));
    }

    [Fact]
    public void Banner_ShowsForAnActiveCollection_AndNotForAllSeries()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var window = Open(out var vm);

            var banner = window.GetVisualDescendants().OfType<CollectionBanner>().Single();
            Assert.False(banner.FindControl<Border>("BannerRoot")!.IsEffectivelyVisible);

            vm.Library.SelectCollectionCommand.Execute(vm.Library.Collections.Single(c => c.Name == "Event books"));
            RunLayout(window);

            Assert.True(banner.FindControl<Border>("BannerRoot")!.IsEffectivelyVisible);
            Assert.False(window.GetVisualDescendants().OfType<CollectionEmptyState>().Single().IsEffectivelyVisible
                && window.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name is null && p.IsEffectivelyVisible && p.GetVisualParent() is CollectionEmptyState && false));
            Snap(window, "library-collection-banner.png");

            vm.Library.SelectAllSeriesCommand.Execute(null);
            RunLayout(window);
            Assert.False(banner.FindControl<Border>("BannerRoot")!.IsEffectivelyVisible);
            window.Close();
        });
    }


    [Fact]
    public void ATallCover_DoesNotMakeTheBannerGrow()
    {
        // Regression: the blurred cover used to drive the banner's height, so a real (tall) cover pushed the grid out of view.
        string png = Path.Combine(Path.GetTempPath(), $"pb_banner_cover_{Guid.NewGuid():N}.png");
        using (var bmp = new WriteableBitmap(new PixelSize(800, 1200), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque))
        {
            bmp.Save(png);
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            var id = context.Collections.Single(c => c.Name == "Event books").Id;
            CollectionService.SetAppearance(context, id, null, "#C97C9E", png, false);
        }

        try
        {
            WithThemeAndTokens(() =>
            {
                using var styles = AppStyles();
                using var tokens = AppResources();
                var window = Open(out var vm);
                vm.Library.SelectCollectionCommand.Execute(vm.Library.Collections.Single(c => c.Name == "Event books"));
                RunLayout(window);

                var root = window.GetVisualDescendants().OfType<CollectionBanner>().Single().FindControl<Border>("BannerRoot")!;
                Assert.True(root.IsEffectivelyVisible);
                Assert.InRange(root.Bounds.Height, 40, 260);
                Snap(window, "library-collection-banner-cover.png");
                window.Close();
            });
        }
        finally
        {
            try { File.Delete(png); } catch (IOException) { }
        }
    }
    [Fact]
    public void EmptyCollection_ShowsTheEmptyCard_WithItsButtons()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var window = Open(out var vm);

            vm.Library.SelectCollectionCommand.Execute(vm.Library.Collections.Single(c => c.Name == "Waiting list"));
            RunLayout(window);

            Assert.True(vm.Library.ShowCollectionEmptyState);
            Assert.False(vm.Library.ShowEmptyState); // the generic message yields
            var edit = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Edit collection…");
            Assert.True(edit.IsEffectivelyVisible);
            var all = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Go to All Series");
            Assert.True(all.IsEffectivelyVisible);
            Snap(window, "library-collection-empty.png");
            window.Close();
        });
    }

    [Fact]
    public void ScrollingTheGrid_MakesTheBannerSlim_AndBackAtTheTopFull()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            var collection = context.Collections.Single(c => c.Name == "Event books");
            var ids = new List<int>();
            for (int i = 0; i < 40; i++)
            {
                var series = new Series { Name = $"Filler {i:00}", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
                series.Issues.Add(new Issue { Number = "1", FilePath = $@"C:\x\filler{i}.cbz" });
                context.Series.Add(series);
                context.SaveChanges();
                ids.Add(series.Id);
            }

            CollectionService.AddItems(context, collection.Id, seriesIds: ids);
            CollectionService.SetIdentity(context, collection.Id, "hero:domino-mask", CollectionCoverStyle.Auto, null, isPinned: true);
        }

        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var window = Open(out var vm);
            vm.Library.SelectCollectionCommand.Execute(vm.Library.Collections.Single(c => c.Name == "Event books"));
            RunLayout(window);

            var banner = window.GetVisualDescendants().OfType<CollectionBanner>().Single();
            double full = banner.Bounds.Height;
            Snap(window, "library-collection-banner-full.png");

            var scrollers = window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible).ToList();
            var scroller = scrollers.FirstOrDefault(s => s.Extent.Height > s.Viewport.Height + 100);
            Assert.True(scroller is not null, string.Join("; ", scrollers.Select(s => $"{s.Name ?? s.GetType().Name} extent {s.Extent.Height:0} viewport {s.Viewport.Height:0}"))
                + $" | collections: {string.Join(",", vm.Library.Collections.Select(c => c.Name))} | members shown: {vm.Library.IsCollectionView}");
            scroller!.Offset = new Vector(0, 200);
            RunLayout(window);

            Assert.True(vm.Library.IsBannerSlim);
            var detail = banner.GetVisualDescendants().OfType<StackPanel>().First(p => p.Classes.Contains("bannerDetail"));
            Assert.True(banner.Bounds.Height < full, $"slim {banner.Bounds.Height} should be shorter than full {full}; classes [{string.Join(",", banner.Classes)}] detail visible {detail.IsVisible}");
            Assert.True(banner.Bounds.Height <= 64, $"slim banner is {banner.Bounds.Height} px");
            Snap(window, "library-collection-banner-slim.png");

            scroller.Offset = new Vector(0, 0);
            RunLayout(window);
            Assert.False(vm.Library.IsBannerSlim);
            window.Close();
        });
    }
}
