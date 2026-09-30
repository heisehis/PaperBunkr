using Avalonia.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Keyboard focus reclaim for the Books screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md): grouping,
/// search and sort all reset the flat or grouped card collections in place while the screen stays attached, and the cards' own glow ring must
/// not stack with the default keyboard-focus adorner.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BooksScreenFocusTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BooksScreenFocusTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_books_focus_test_{Guid.NewGuid():N}.db");
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

    private static void SeedBook(string title, string? series = null)
    {
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

        context.Books.Add(new Book
        {
            Title = title,
            BookSeriesId = seriesId,
            Format = BookFormat.Epub,
            FilePath = $@"C:\books\{title}.epub",
            AddedTime = DateTime.UtcNow,
        });
        context.SaveChanges();
    }

    private static (BooksScreenViewModel Vm, Window Window, BooksScreen Screen, Button? Sibling) Show(bool withSibling = false)
    {
        var vm = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });
        var screen = new BooksScreen { DataContext = vm };
        Button? sibling = withSibling ? new Button { Content = "Rail" } : null;
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        if (sibling is not null)
        {
            grid.Children.Add(sibling);
        }

        Grid.SetRow(screen, 1);
        grid.Children.Add(screen);
        var window = new Window { Content = grid, Width = 1400, Height = 900 };
        window.Show();
        RunLayout(window);
        return (vm, window, screen, sibling);
    }

    private static bool IsCard(Avalonia.Visual? visual) => visual is Button b && b.Classes.Contains("card");

    [Fact]
    public void FocusRings_AreNotClipped()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            SeedBook("Dune");
            SeedBook("Emma");
            var (_, window, screen, _) = Show();
            RunLayout(window);
            var clipped = ClippedFocusRings(window, screen);
            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    [Fact]
    public void NoPriorClick_FocusLandsOnACard()
    {
        WithThemeAndTokens(() =>
        {
            SeedBook("Dune");
            SeedBook("Emma");
            var (_, window, _, _) = Show();

            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    [Fact]
    public void CardsSuppressTheDefaultKeyboardFocusAdorner()
    {
        WithThemeAndTokens(() =>
        {
            SeedBook("Dune");
            var (_, window, _, _) = Show();

            var card = Assert.IsType<Button>(Focused(window));
            Assert.Null(card.FocusAdorner);
            window.Close();
        });
    }

    [Fact]
    public void TogglingGrouping_KeepsFocusOnACard_AcrossMultipleGroups()
    {
        WithThemeAndTokens(() =>
        {
            SeedBook("Dune", "Dune Chronicles");
            SeedBook("Dune Messiah", "Dune Chronicles");
            SeedBook("Emma", "Austen");
            var (vm, window, _, _) = Show();
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)}");

            vm.GroupField = BooksGroupField.Series;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after grouping by series");

            vm.GroupField = BooksGroupField.None;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after ungrouping");
            window.Close();
        });
    }

    [Fact]
    public void ASearchThatResetsTheCollection_KeepsFocusOnACard()
    {
        WithThemeAndTokens(() =>
        {
            SeedBook("Dune");
            SeedBook("Emma");
            var (vm, window, _, _) = Show();

            vm.SearchQuery = "Dune";
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after searching");

            vm.SearchQuery = string.Empty;
            RunLayout(window);
            Assert.True(IsCard(Focused(window)), $"focus on {Focused(window)} after clearing the search");
            window.Close();
        });
    }

    [Fact]
    public void ACollectionReset_DoesNotStealFocusFromASiblingRegion()
    {
        WithThemeAndTokens(() =>
        {
            SeedBook("Dune");
            SeedBook("Emma");
            var (vm, window, _, sibling) = Show(withSibling: true);
            sibling!.Focus();
            RunLayout(window);
            Assert.Same(sibling, Focused(window));

            vm.SearchQuery = "Dune";
            RunLayout(window);

            Assert.Same(sibling, Focused(window));
            window.Close();
        });
    }
}
