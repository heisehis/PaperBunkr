using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library sidebar's Collections block in the real main window (docs/superpowers/specs/2026-11-06-collection-identity-design.md): glyph, pin,
/// divider, drag grip, progress ring, squircle marker. With PB_RENDER_OUT set it also writes a PNG of the window for a human look.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CollectionSidebarRenderTests : IDisposable
{
    private readonly string? _original = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pb_colsidebar_{Guid.NewGuid():N}.db");
    private readonly CoverCacheTestRedirect _cover = new();

    public CollectionSidebarRenderTests()
    {
        PaperbunkrDbContext.DatabasePathOverride = _db;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();

        var series = new Series { Name = "Reading", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        series.Issues.Add(new Issue { Number = "1", FilePath = "C:\\x\\1.cbz", LastPageRead = 4 });
        series.Issues.Add(new Issue { Number = "2", FilePath = "C:\\x\\2.cbz" });
        context.Series.Add(series);
        context.SaveChanges();

        var pinned = CollectionService.Create(context, "Event books");
        CollectionService.SetAppearance(context, pinned.Id, null, "#C97C9E", null, true);
        CollectionService.SetIdentity(context, pinned.Id, "Rocket", CollectionCoverStyle.Auto, null, isPinned: true);
        CollectionService.AddItems(context, pinned.Id, seriesIds: new[] { series.Id });
        var plain = CollectionService.Create(context, "Plain collection");
        CollectionService.SetAppearance(context, plain.Id, null, "#7C93C9", null, true);
        CollectionService.AddItems(context, plain.Id, seriesIds: new[] { series.Id });
        var smart = CollectionService.Create(context, "Another one");

        var settings = context.GetOrCreateAppSettings();
        settings.ShowCollectionProgress = true;
        settings.CollectionDotShape = CollectionDotShape.Squircle;
        context.SaveChanges();
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

    [Fact]
    public void Sidebar_ShowsGlyphPinDividerGripRingAndSquircle()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var window = Open(out var vm);

            Assert.Equal(new[] { "Event books", "Plain collection", "Another one" }, vm.Library.Collections.Select(c => c.Name).ToArray());
            Assert.True(vm.Library.Collections[0].IsPinned);
            Assert.True(vm.Library.Collections[1].IsFirstUnpinned);

            var rings = window.GetVisualDescendants().OfType<ProgressRing>().Where(r => r.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(rings);
            var grips = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("colGrip")).ToList();
            Assert.Equal(3, grips.Count);
            Assert.All(grips, g => Assert.True(CollectionSidebarRowDrag.GetHandle(g)));

            string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                var rtb = new RenderTargetBitmap(new PixelSize(1366, 768));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, "library-sidebar-collections.png"));
            }

            window.Close();
        });
    }

    [Fact]
    public void SidebarStyle_PutsItsClassOnTheSidebarHost()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            try
            {
                PersonalizationRuntime.Instance.SidebarStyle = Paperbunkr.Data.Entities.SidebarStyle.Pill;
                var window = Open(out var vm);
                var host = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ContextualSidebar");
                Assert.Contains("sidebarPill", host.Classes);
                Assert.DoesNotContain("sidebarTint", host.Classes);

                PersonalizationRuntime.Instance.SidebarStyle = Paperbunkr.Data.Entities.SidebarStyle.Tint;
                RunLayout(window);
                Assert.Contains("sidebarTint", host.Classes);

                string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
                if (!string.IsNullOrEmpty(outDir))
                {
                    PersonalizationRuntime.Instance.SidebarStyle = Paperbunkr.Data.Entities.SidebarStyle.Pill;
                    RunLayout(window);
                    Directory.CreateDirectory(outDir);
                    var rtb = new RenderTargetBitmap(new PixelSize(1366, 768));
                    rtb.Render(window);
                    rtb.Save(Path.Combine(outDir, "library-sidebar-pill.png"));
                }

                window.Close();
            }
            finally
            {
                PersonalizationRuntime.Instance.SidebarStyle = Paperbunkr.Data.Entities.SidebarStyle.Standard;
            }
        });
    }
}
