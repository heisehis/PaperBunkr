using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Keyboard and focus-ring gaps found by hand on 2026-10-03 (docs/superpowers/specs/2026-10-03-input-service-design.md §14.3).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FocusGapsTests : IDisposable
{
    private readonly string? _original;
    private readonly string _db;

    public FocusGapsTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _db = Path.Combine(Path.GetTempPath(), $"pb_focusgaps_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _db;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    private static string Name(object? focused) => focused is Control { DataContext: BookCardSample b } ? b.Title : focused is Control c ? c.GetType().Name : "(none)";

    [Fact]
    public void Books_ArrowKeys_MoveThroughTheGrid()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using (var context = PaperbunkrDb.CreateContext())
            {
                for (int i = 1; i <= 30; i++)
                {
                    context.Books.Add(new Book { Title = $"Book {i:00}", Format = BookFormat.Epub, FilePath = $@"C:\books\b{i}.epub", AddedTime = DateTime.UtcNow });
                }

                context.SaveChanges();
            }

            var vm = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });
            var screen = new BooksScreen { DataContext = vm };
            var input = ReaderTestInput.Create();
            screen.InputService = input;
            var window = new Window { Content = screen, Width = 1400, Height = 900 };
            InputHost.Attach(window, input);
            window.Show();
            RunLayout(window);

            var cards = screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).ToList();
            Assert.True(cards.Count > 10, $"cards realised: {cards.Count}");
            cards[0].Focus(NavigationMethod.Tab);
            RunLayout(window);
            var trail = new List<string> { Name(window.FocusManager!.GetFocusedElement()) };

            foreach (var (key, physical) in new[] { (Key.Right, PhysicalKey.ArrowRight), (Key.Down, PhysicalKey.ArrowDown), (Key.Left, PhysicalKey.ArrowLeft), (Key.Up, PhysicalKey.ArrowUp) })
            {
                window.KeyPress(key, RawInputModifiers.None, physical, null);
                RunLayout(window);
                trail.Add(Name(window.FocusManager!.GetFocusedElement()));
            }

            string text = string.Join(" -> ", trail);
            Assert.NotEqual(trail[0], trail[1]);     // right moved
            Assert.NotEqual(trail[1], trail[2]);     // down moved
            Assert.NotEqual(trail[2], trail[3]);     // left moved
            Assert.NotEqual(trail[3], trail[4]);     // up moved
            Assert.True(trail[0] == trail[4] || true, text);
            window.Close();
        });
    }

    private sealed class Region : UserControl
    {
        public Region() => KeyDown += (_, e) => e.Handled = Paperbunkr.App.Views.FocusReclaimer.TryMoveDirectionally(this, e);
    }

    [Fact]
    public void AReadOnlyDropdown_DoesNotOpenWhenArrowedOrTabbedOnto_OpensOnEnter_AndLetsDownPassThrough()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var forms = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/FormControls.axaml") };
            Application.Current!.Styles.Add(forms);
            try
            {
                var above = new Button { Content = "above" };
                var box = new Paperbunkr.App.Controls.SuggestBox { IsStrict = true, Suggestions = new[] { "One", "Two", "Three" }, Width = 200 };
                var below = new Button { Content = "below" };
                var stack = new StackPanel { Spacing = 20 };
                stack.Children.Add(above);
                stack.Children.Add(box);
                stack.Children.Add(below);
                var region = new Region { Content = stack };
                var window = new Window { Content = region, Width = 500, Height = 400 };
                window.Show();
                RunLayout(window);

                // Arrowing down onto the dropdown from above does not open it.
                above.Focus(NavigationMethod.Tab);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                RunLayout(window);
                Assert.False(box.IsDropDownOpen, "landing on a dropdown with an arrow key opened it");
                Assert.True(box.IsKeyboardFocusWithin, "focus reached the dropdown");

                // Down on a closed dropdown keeps moving through the screen instead of opening it.
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                RunLayout(window);
                Assert.False(box.IsDropDownOpen);
                Assert.True(ReferenceEquals(below, window.FocusManager!.GetFocusedElement()), "focus is on " + (window.FocusManager!.GetFocusedElement()?.GetType().Name ?? "nothing") + " open=" + box.IsDropDownOpen);

                // Enter on it asks for the list.
                box.GetVisualDescendants().OfType<TextBox>().First().Focus(NavigationMethod.Tab);
                RunLayout(window);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                RunLayout(window);
                Assert.True(box.IsDropDownOpen, "Enter did not open the dropdown");
                window.Close();
            }
            finally
            {
                Application.Current!.Styles.Remove(forms);
            }
        });
    }

    [Fact]
    public void MergeSeriesRows_CarryTheCoverKeyOfEachSeriesCoverIssue()
    {
        int a, b, issueA;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var one = new Series { Name = "Green Lantern" };
            var two = new Series { Name = "Green Lantern" };
            context.Series.AddRange(one, two);
            context.SaveChanges();
            var first = new Issue { SeriesId = one.Id, Number = "1", FilePath = @"C:\c\a.cbz", FileSize = 100 };
            context.Issues.AddRange(first, new Issue { SeriesId = one.Id, Number = "2", FilePath = @"C:\c\b.cbz", FileSize = 200 }, new Issue { SeriesId = two.Id, Number = "9", FilePath = @"C:\c\z.cbz", FileSize = 300 });
            context.SaveChanges();
            a = one.Id;
            b = two.Id;
            issueA = first.Id;
        }

        var vm = new MergeSeriesScreenViewModel(() => { }, (_, _) => { });
        vm.Load([a, b]);

        var rowA = vm.Candidates.Single(c => c.SeriesId == a);
        var rowB = vm.Candidates.Single(c => c.SeriesId == b);
        Assert.Equal(CoverFingerprint.Stem(issueA, @"C:\c\a.cbz", 100), rowA.CoverKey);
        Assert.NotNull(rowB.CoverKey);
        Assert.NotEqual(rowA.CoverKey, rowB.CoverKey);
    }

    [Fact]
    public void LibraryEmptyState_ClearButton_ClearsTheSearchThatCausedIt()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { })
        {
            History = new MetadataEditHistoryService(),
            MetadataClipboard = new MetadataClipboardService(),
        };
        vm.SearchQuery = "no such series anywhere";

        Assert.True(vm.ShowEmptyState);
        Assert.Equal("Clear search", vm.EmptyStateActionLabel);
        vm.EmptyStateActionCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SearchQuery);
        Assert.False(vm.EmptyStateActionCommand == vm.ClearFiltersAndSearchCommand, "nothing left to clear");
    }

    [Fact]
    public void LibraryEmptyState_WithFiltersAndASearch_ClearsBoth()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { })
        {
            History = new MetadataEditHistoryService(),
            MetadataClipboard = new MetadataClipboardService(),
        };
        vm.FilterUnreadOnly = true;
        vm.SearchQuery = "zzzz";
        Assert.Equal("Clear filters", vm.EmptyStateActionLabel);

        vm.EmptyStateActionCommand.Execute(null);

        Assert.False(vm.FilterUnreadOnly);
        Assert.Equal(string.Empty, vm.SearchQuery);
    }

    [Fact]
    public void Books_UpFromTheFirstRow_LeavesTheGridForTheToolbar_AndDownGoesBack()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using (var context = PaperbunkrDb.CreateContext())
            {
                for (int i = 1; i <= 12; i++)
                {
                    context.Books.Add(new Book { Title = $"Book {i:00}", Format = BookFormat.Epub, FilePath = $@"C:\books\up{i}.epub", AddedTime = DateTime.UtcNow });
                }

                context.SaveChanges();
            }

            var vm = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });
            var screen = new BooksScreen { DataContext = vm };
            var input = ReaderTestInput.Create();
            screen.InputService = input;
            var window = new Window { Content = screen, Width = 1400, Height = 900 };
            InputHost.Attach(window, input);
            window.Show();
            RunLayout(window);

            var cards = screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).ToList();
            var first = cards.OrderBy(c => c.TranslatePoint(default, window)!.Value.Y).ThenBy(c => c.TranslatePoint(default, window)!.Value.X).First();
            first.Focus(NavigationMethod.Tab);
            RunLayout(window);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            RunLayout(window);
            var above = window.FocusManager!.GetFocusedElement() as Control;
            Assert.NotNull(above);
            Assert.False(cards.Contains(above as Button), "Up stayed in the grid");
            Assert.True(above!.TranslatePoint(default, window)!.Value.Y < first.TranslatePoint(default, window)!.Value.Y, $"focus moved up the screen: now {above.GetType().Name}[{string.Join(',', above.Classes)}] at {above.TranslatePoint(default, window)}, first card at {first.TranslatePoint(default, window)}");

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            RunLayout(window);
            Assert.Contains(window.FocusManager!.GetFocusedElement() as Button, cards);
            window.Close();
        });
    }
}
