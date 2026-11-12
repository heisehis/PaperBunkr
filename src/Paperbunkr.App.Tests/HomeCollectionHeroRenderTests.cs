using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.App.Views.Home;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>The Home Collections shelf with its Continue hero in the real main window (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomeCollectionHeroRenderTests : IDisposable
{
    private readonly string? _original = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"pb_homehero_render_{Guid.NewGuid():N}.db");
    private readonly CoverCacheTestRedirect _cover = new();

    public HomeCollectionHeroRenderTests()
    {
        PaperbunkrDbContext.DatabasePathOverride = _db;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();

        var ids = new List<int>();
        foreach (var (name, opened) in new[] { ("Annihilation 2099", (DateTime?)DateTime.UtcNow.AddHours(-2)), ("Conquest 2099", null), ("Spider-Man 2099", null) })
        {
            var series = new Series { Name = name, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
            series.Issues.Add(new Issue { Number = "1", FilePath = $"C:\\x\\{name}1.cbz", LastPageRead = opened is null ? null : 5, OpenedTime = opened });
            series.Issues.Add(new Issue { Number = "2", FilePath = $"C:\\x\\{name}2.cbz" });
            context.Series.Add(series);
            context.SaveChanges();
            ids.Add(series.Id);
        }

        var reading = CollectionService.Create(context, "2099");
        CollectionService.SetAppearance(context, reading.Id, null, "#C97C9E", null, true);
        CollectionService.AddItems(context, reading.Id, seriesIds: ids);
        var other = CollectionService.Create(context, "Waiting");
        CollectionService.AddItems(context, other.Id, seriesIds: new[] { ids[1] });
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        _cover.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    [Fact]
    public void Shelf_ShowsTheHeroWithItsContinueButton_ThenTheOtherCards()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1366, Height = 900 };
            window.Show();
            vm.GoHomeCommand.Execute(null);
            vm.Home.LoadFromDatabase();
            RunLayout(window);

            Assert.NotNull(vm.Home.CollectionHero);
            var hero = window.GetVisualDescendants().OfType<CollectionHeroCard>().Single();
            Assert.True(hero.IsEffectivelyVisible);
            var continueButton = hero.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("primary"));
            Assert.True(continueButton.IsEffectivelyVisible);

            string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                hero.BringIntoView();
                RunLayout(window);
                var rtb = new RenderTargetBitmap(new PixelSize(1366, 900));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, "home-collection-hero.png"));
            }

            window.Close();
        });
    }
}
