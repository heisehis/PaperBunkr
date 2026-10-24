using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Models;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.Tests.ReadingLists;

/// <summary>Not an assertion: with PB_RENDER_OUT set, writes the gallery and an open list as PNGs in each theme for a human look
/// (docs/superpowers/specs/2026-10-20-reading-lists-visual-identity-design.md §5).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingListsRenderTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ReadingListsRenderTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_render_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private static int Seed(string name, int total, int read, string? description, ReadingListType type, string? source, IEnumerable<string>? tags = null, int? folder = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name };
        var list = new ReadingList { Name = name, Description = description, Type = type, Source = source };
        for (int n = 1; n <= total; n++)
        {
            list.Items.Add(new ReadingListItem
            {
                Issue = new Issue { Series = series, Number = n.ToString(), FilePath = $"c:/{name}{n}.cbz", PageCount = 10, LastPageRead = n <= read ? 9 : null },
                SortOrder = n,
            });
        }

        foreach (string tag in tags ?? [])
        {
            list.Tags.Add(new ReadingListTag { Value = tag });
        }

        context.ReadingLists.Add(list);
        ReadingListFolders.PlaceNewList(context, list, folder);
        context.SaveChanges();
        return list.Id;
    }

    [Fact]
    public void RenderGalleryAndList()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        int folder;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var f = ReadingListFolders.Create(context, "Crisis Events", null);
            ReadingListFolders.Create(context, "Green Lantern", null);
            ReadingListFolders.Create(context, "Variant Covers", null);
            context.SaveChanges();
            folder = f.Id;
        }

        Seed("Infinite Crisis", 6, 0, null, ReadingListType.User, null, folder: folder);
        string synopsis = "Cletus Kasady is back, and he's deadlier than ever. On a hunt for every Super Hero in the Marvel Universe who has worn a symbiote, Carnage has his eyes on his first victims, starting with some of our favorite Marvel heroes. Everyone is a target!";
        int carnage = Seed("Absolute Carnage", 29, 0, synopsis, ReadingListType.Community, "metron", ["Community", "Metron", "Super-Hero", "Superhero", "Variant Cover", "Homage Covers", "Variant Cover: Action Figure", "A Fresh Start"]);
        Seed("World War Hulk", 38, 13, "The Hulk returns to Earth to take revenge on the Illuminati who exiled him.", ReadingListType.Event, "metron", ["Hulk", "Metron"]);
        Seed("Age of Revelation", 57, 0, null, ReadingListType.Community, null, ["Metron"]);
        Seed("Dark Nights Death Metal", 6, 6, "The Dark Multiverse fights back.", ReadingListType.Event, "metron", ["Flash"]);
        Seed("Saga", 8, 3, "A star-crossed pair of parents flee across the galaxy with their newborn daughter.", ReadingListType.User, null);

        foreach (string theme in new[] { "default", "daylight", "matrix" })
        {
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                using var variant = UseVariant(theme);
                var vm = new ReadingListsScreenViewModel(new NoPicker(), (_, _) => { });
                var window = new Window
                {
                    Content = new ReadingListsScreen { DataContext = vm }, Width = 1296, Height = 720,
                    Background = (IBrush)Application.Current!.FindResource("PbBgBrush")!,
                };
                window.Show();
                Settle(window);
                Save(window, Path.Combine(outDir, $"reading-gallery-{theme}.png"));

                vm.LoadReadingList(carnage);
                Settle(window);
                Save(window, Path.Combine(outDir, $"reading-list-{theme}.png"));
                window.Close();
            });
        }
    }

    /// <summary>Daylight is a light skin: ThemeService sets the application's theme variant from the skin's mode, so the render does too.</summary>
    private static IDisposable UseVariant(string theme)
    {
        var app = Application.Current!;
        var before = app.RequestedThemeVariant;
        bool light = theme == "daylight";
        app.RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
        // The skin's hero fade colours (theme.json heroGradientStart/End), which the foundation harness does not apply: Daylight fades to its cream, not black.
        object? start = null, end = null;
        bool hadStart = app.Resources.TryGetResource("PbHeroGradientStartColor", null, out start);
        bool hadEnd = app.Resources.TryGetResource("PbHeroGradientEndColor", null, out end);
        if (light)
        {
            app.Resources["PbHeroGradientStartColor"] = Color.Parse("#00F7F3EC");
            app.Resources["PbHeroGradientEndColor"] = Color.Parse("#FFF7F3EC");
        }

        return new Restore(() =>
        {
            app.RequestedThemeVariant = before;
            if (light)
            {
                if (hadStart) app.Resources["PbHeroGradientStartColor"] = start!; else app.Resources.Remove("PbHeroGradientStartColor");
                if (hadEnd) app.Resources["PbHeroGradientEndColor"] = end!; else app.Resources.Remove("PbHeroGradientEndColor");
            }
        });
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private sealed class FakeHeader : ViewModelBase, IDetailHeaderSource
    {
        public IBrush CoverBrush => Brushes.DarkSlateBlue;
        public Bitmap? CoverImage => null;
        public Bitmap? BackdropImage { get; init; }
        public string HeaderTitle => "Absolute Carnage";
        public string? SecondaryTitle => "Marvel Comics";
        public string MetaLine => "29 issues · 4 unread";
        public string? IssueSummaryLine => "29 issues · 4 unread";
        public string? Synopsis => "Cletus Kasady is back, and he's deadlier than ever. On a hunt for every Super Hero who has worn a symbiote.";
        public IReadOnlyList<DetailHeroAction> Actions => [];
        public DetailHeroProgress? TrackerProgress => null;
    }

    [Fact]
    public void RenderDetailHero()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        foreach (string theme in new[] { "default", "daylight" })
        {
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                using var variant = UseVariant(theme);
                // A dark, busy backdrop like a blurred cover.
                var art = new Border
                {
                    Width = 1000, Height = 500,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.Parse("#7A1F2B"), 0), new GradientStop(Color.Parse("#1B2A55"), 0.55), new GradientStop(Color.Parse("#2A1030"), 1) },
                    },
                };
                art.Measure(new Size(1000, 500));
                art.Arrange(new Rect(0, 0, 1000, 500));
                var backdrop = new RenderTargetBitmap(new PixelSize(1000, 500));
                backdrop.Render(art);

                var window = new Window
                {
                    Content = new DetailHero { DataContext = new FakeHeader { BackdropImage = backdrop }, HeroHeight = 340 },
                    Width = 1100, Height = 340, Background = (IBrush)Application.Current!.FindResource("PbBgBrush")!,
                };
                window.Show();
                Settle(window);
                Save(window, Path.Combine(outDir, $"detail-hero-{theme}.png"));
                window.Close();
            });
        }
    }

    private static void Settle(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            window.UpdateLayout();
            TestDispatcher.Drain();
        }
    }

    private static void Save(Window window, string path)
    {
        var rtb = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        rtb.Render(window);
        rtb.Save(path);
    }
}
