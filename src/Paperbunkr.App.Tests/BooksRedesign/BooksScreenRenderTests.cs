using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>Not an assertion: with PB_RENDER_OUT set, writes the redesigned Books screen as PNGs per theme and width for a human look
/// (docs/superpowers/specs/2026-10-27-books-section-redesign-plan.md Step 7). Stand-in books, no cover files, so covers show their gradients.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BooksScreenRenderTests : IDisposable
{
    private readonly string? _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_books_render_{Guid.NewGuid():N}.db");

    public BooksScreenRenderTests()
    {
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();

        var orchard = new BookSeries { Name = "The Orchard Cycle" };
        context.BookSeries.Add(orchard);
        context.SaveChanges();

        var now = DateTime.UtcNow;
        Book B(string title, string author, BookReadingStatus status, float? rating = null, int? series = null, float? index = null,
            DateTime? opened = null, string? excerpt = null, int chapter = 0, int chapters = 0, double? fraction = null) => new()
            {
                Title = title, Author = author, Format = BookFormat.Epub, FilePath = $@"C:\books\{title}.epub", AddedTime = now.AddDays(-title.Length),
                Status = status, Rating = rating, BookSeriesId = series, SeriesIndex = index, LastOpenedTime = opened, LastExcerpt = excerpt,
                LastChapterIndex = chapter, ChapterCount = chapters, LastProgressionFraction = fraction, CharacterCount = 640_000,
                Summary = "In a valley where the orchards turned to glass one winter, a cartographer returns to map the only tree that still bears fruit.",
            };

        context.Books.AddRange(
            B("The Glass Orchard", "Maarja Ilves", BookReadingStatus.Reading, series: orchard.Id, index: 2, opened: now.AddHours(-2),
                excerpt: "By the ninth winter the trees no longer rang when the wind came through; they only remembered how.", chapter: 8, chapters: 24, fraction: 0.4),
            B("The Salt Year", "Maarja Ilves", BookReadingStatus.Finished, rating: 5, series: orchard.Id, index: 1),
            B("An Atlas of Quiet", "Maarja Ilves", BookReadingStatus.WantToRead, series: orchard.Id, index: 4),
            B("Salt and Iron", "Rita Okafor", BookReadingStatus.Reading, opened: now.AddDays(-1), chapter: 14, chapters: 22, fraction: 0.5),
            B("The Lantern Keeper", "João Ferreira", BookReadingStatus.Reading, opened: now.AddDays(-4), chapter: 2, chapters: 30, fraction: 0.2),
            B("Paper Harbour", "Lena Moss", BookReadingStatus.Unread),
            B("Green Hours", "Tom Vane", BookReadingStatus.Abandoned),
            B("A Small Ceremony", "Hana Doyle", BookReadingStatus.Finished, rating: 4.5f),
            B("Night Ferry", "Anja Kurz", BookReadingStatus.WantToRead),
            B("Ember Year", "Seo Ahn", BookReadingStatus.Unread),
            B("The Long Field Behind the Mill House", "Peter Aalto", BookReadingStatus.Unread),
            B("Winter Accounts", "Mia Rand", BookReadingStatus.Finished, rating: 3));
        context.SaveChanges();

        context.ReadingEvents.Add(new ReadingEvent
        {
            ItemType = ReadingItemType.Novel, ItemId = 1, Kind = ReadingEventKind.Opened, TimestampUtc = now.AddHours(-3), ActiveSeconds = 4200, PagesRead = 46,
        });
        context.SaveChanges();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void RenderBooksScreen()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        foreach (string theme in new[] { "default", "daylight", "matrix" })
        {
            Theming.FoundationStyleTests.WithFoundation(theme, () =>
            {
                using var styles = AppStyles();
                var app = Application.Current!;
                var before = app.RequestedThemeVariant;
                app.RequestedThemeVariant = theme == "daylight" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
                try
                {
                    foreach (var (width, group, mode) in new[] { (1310, BooksGroupField.None, BooksViewMode.Grid), (1310, BooksGroupField.Series, BooksViewMode.Grid), (1000, BooksGroupField.None, BooksViewMode.Grid), (1310, BooksGroupField.Series, BooksViewMode.Bookshelf) })
                    {
                        var vm = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }) { GroupField = group, ViewMode = mode };
                        var window = new Window
                        {
                            Content = new BooksScreen { DataContext = vm }, Width = width, Height = 768,
                            Background = (IBrush)app.FindResource("PbBgBrush")!,
                        };
                        window.Show();
                        RunLayout(window);
                        var rtb = new RenderTargetBitmap(new PixelSize(width, 768));
                        rtb.Render(window);
                        rtb.Save(Path.Combine(outDir, $"books-{theme}-{width}-{group}-{mode}.png"));
                        window.Close();
                    }
                }
                finally
                {
                    app.RequestedThemeVariant = before;
                }
            });
        }
    }
}
