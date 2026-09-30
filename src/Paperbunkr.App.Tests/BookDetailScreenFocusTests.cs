using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Keyboard reach on the Book detail screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-detail-home-smart-design.md):
/// book mode and series mode are sibling panels toggled by IsVisible. Focus lands inside with no prior click (not on the back link),
/// survives a mode switch, Up/Down step between chapter rows, arrows walk the series cards, and no focus ring is clipped.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookDetailScreenFocusTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly string _epubPath;

    public BookDetailScreenFocusTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_book_focus_{Guid.NewGuid():N}.db");
        _epubPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_book_focus_{Guid.NewGuid():N}.epub");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _epubPath })
        {
            try
            {
                if (File.Exists(p)) File.Delete(p);
            }
            catch (IOException)
            {
            }
        }
    }

    private int AddBook(string title, string? series = null, bool realEpub = false)
    {
        string filePath = realEpub ? EpubFixture.Create(_epubPath, title: title) : $@"C:\books\{title}.epub";
        using var context = PaperbunkrDb.CreateContext();
        int? seriesId = null;
        if (series is not null)
        {
            var bs = context.BookSeries.FirstOrDefault(s => s.Name == series) ?? new BookSeries { Name = series };
            if (bs.Id == 0)
            {
                context.BookSeries.Add(bs);
                context.SaveChanges();
            }

            seriesId = bs.Id;
        }

        var book = new Book { Title = title, Author = "Ada Author", BookSeriesId = seriesId, Format = BookFormat.Epub, FilePath = filePath, AddedTime = DateTime.UtcNow };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private int SeriesIdOf(int bookId)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Books.Single(b => b.Id == bookId).BookSeriesId!.Value;
    }

    private static (Window Window, BookDetailScreen Screen, BookDetailScreenViewModel Vm, Button Sibling) Show(Action<BookDetailScreenViewModel> load)
    {
        var vm = new BookDetailScreenViewModel(() => { }, (_, _, _) => { });
        load(vm);
        var screen = new BookDetailScreen { DataContext = vm };
        var sibling = new Button { Content = "Rail" };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*") };
        grid.Children.Add(sibling);
        Grid.SetColumn(screen, 1);
        grid.Children.Add(screen);
        var window = new Window { Content = grid, Width = 1300, Height = 900 };
        window.Show();
        RunLayout(window);
        return (window, screen, vm, sibling);
    }

    private static List<Button> Visible(Visual screen, string cls) =>
        screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains(cls) && b.IsEffectivelyVisible).ToList();

    [Fact]
    public void BookMode_NoPriorClick_FocusLandsInsideTheScreen_NotOnTheBackLink()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            int id = AddBook("Dune", realEpub: true);
            var (window, screen, _, _) = Show(vm => vm.LoadBook(id));

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            Assert.False(Focused(window) is Button b && b.Classes.Contains("backLink"), "focus should skip the back link");
            window.Close();
        });
    }

    [Fact]
    public void BookMode_UpDown_StepBetweenChapterRows()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            int id = AddBook("Dune", realEpub: true);
            var (window, screen, _, _) = Show(vm => vm.LoadBook(id));
            var rows = Visible(screen, "listRow");
            Assert.True(rows.Count >= 2, $"expected chapter rows, found {rows.Count}");
            rows[0].Focus(NavigationMethod.Directional);
            RunLayout(window);

            Press(window, Key.Down);
            Assert.Same(rows[1], Focused(window));

            Press(window, Key.Up);
            Assert.Same(rows[0], Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void SeriesMode_NoPriorClick_FocusLandsInsideTheScreen_AndArrowsWalkTheCards()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            int first = AddBook("Dune", series: "Dune Chronicles");
            AddBook("Dune Messiah", series: "Dune Chronicles");
            AddBook("Children of Dune", series: "Dune Chronicles");
            int seriesId = SeriesIdOf(first);
            var (window, screen, _, _) = Show(vm => vm.LoadSeries(seriesId));

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");
            var cards = Visible(screen, "card");
            Assert.True(cards.Count >= 3, $"expected series cards, found {cards.Count}");
            cards[0].Focus(NavigationMethod.Directional);
            RunLayout(window);

            Press(window, Key.Right);
            Assert.Same(cards[1], Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void SwitchingFromABookToItsSeries_KeepsFocusInsideTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            int id = AddBook("Dune", series: "Dune Chronicles", realEpub: true);
            AddBook("Dune Messiah", series: "Dune Chronicles");
            int seriesId = SeriesIdOf(id);
            var (window, screen, vm, _) = Show(v => v.LoadBook(id));
            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)}");

            vm.LoadSeries(seriesId);
            RunLayout(window);

            Assert.True(FocusIsInside(window, screen), $"focus on {Focused(window)} after switching to series mode");
            window.Close();
        });
    }

    [Fact]
    public void AModeSwitch_DoesNotStealFocusFromASiblingRegion()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            int id = AddBook("Dune", series: "Dune Chronicles", realEpub: true);
            int seriesId = SeriesIdOf(id);
            var (window, _, vm, sibling) = Show(v => v.LoadBook(id));
            sibling.Focus();
            RunLayout(window);

            vm.LoadSeries(seriesId);
            RunLayout(window);

            Assert.Same(sibling, Focused(window));
            window.Close();
        });
    }

    [Fact]
    public void FocusRings_AreNotClipped_InBothModes()
    {
        WithThemeAndTokens(() =>
        {
            using var tokens = AppResources();
            using var styles = AppStyles();
            int id = AddBook("Dune", series: "Dune Chronicles", realEpub: true);
            AddBook("Dune Messiah", series: "Dune Chronicles");
            int seriesId = SeriesIdOf(id);
            var (window, screen, vm, _) = Show(v => v.LoadBook(id));
            var clipped = ClippedFocusRings(window, screen).Select(c => "[book] " + c).ToList();

            vm.LoadSeries(seriesId);
            RunLayout(window);
            clipped.AddRange(ClippedFocusRings(window, screen).Select(c => "[series] " + c));
            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }
}
