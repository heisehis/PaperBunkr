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

/// <summary>Not an assertion: with PB_RENDER_OUT set, writes Book Detail (book and series mode) per theme for a human look (books redesign plan Steps 13-15).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookDetailRenderTests : IDisposable
{
    private readonly string? _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_detail_render_{Guid.NewGuid():N}.db");
    private readonly int _seriesId;
    private readonly int _bookId;

    public BookDetailRenderTests()
    {
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
        var series = new BookSeries { Name = "The Orchard Cycle", Author = "Maarja Ilves" };
        context.BookSeries.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        Book B(string title, float index, BookReadingStatus status, double? fraction = null, float? rating = null) => new()
        {
            Title = title, Author = "Maarja Ilves", Format = BookFormat.Pdf, FilePath = $@"C:\books\{title}.pdf", AddedTime = DateTime.UtcNow,
            BookSeriesId = series.Id, SeriesIndex = index, Status = status, LastOpenedTime = status == BookReadingStatus.Reading ? DateTime.UtcNow : null,
            LastProgressionFraction = fraction, Rating = rating, PublishedDate = new DateTime(2019, 3, 14), Publisher = "Harrow & Finch", LanguageISO = "en",
            ISBN = "978-1-5291-0412-6", CommunityRating = 4.1f, CommunityRatingCount = 2304,
            Summary = "In a valley where the orchards turned to glass one winter, a cartographer returns to map the only tree that still bears fruit, "
                + "and finds her own name carved into its bark. A quiet, strange novel about maps, memory and the things a place keeps of us.",
        };

        var glass = B("The Glass Orchard", 2, BookReadingStatus.Reading, 0.4, 4);
        context.Books.AddRange(B("The Salt Year", 1, BookReadingStatus.Finished, rating: 5), glass, B("An Atlas of Quiet", 4, BookReadingStatus.WantToRead));
        context.SaveChanges();
        _bookId = glass.Id;
        context.BookTags.Add(new BookTag { BookId = glass.Id, Field = BookTagField.Genre, Value = "Literary fiction" });
        context.BookTags.Add(new BookTag { BookId = glass.Id, Field = BookTagField.Genre, Value = "Fantasy" });
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
    public void RenderBookDetail()
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
                    foreach (string mode in new[] { "book", "series" })
                    {
                        var vm = new BookDetailScreenViewModel(() => { }, (_, _, _) => { });
                        if (mode == "book")
                        {
                            vm.LoadBook(_bookId);
                        }
                        else
                        {
                            vm.LoadSeries(_seriesId);
                        }

                        var window = new Window
                        {
                            Content = new BookDetailScreen { DataContext = vm }, Width = 1310, Height = 768,
                            Background = (IBrush)app.FindResource("PbBgBrush")!,
                        };
                        window.Show();
                        RunLayout(window);
                        var rtb = new RenderTargetBitmap(new PixelSize(1310, 768));
                        rtb.Render(window);
                        rtb.Save(Path.Combine(outDir, $"detail-{theme}-{mode}.png"));
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
