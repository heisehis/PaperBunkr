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

/// <summary>Enter and Space open the focused tile in the Library: a series in series mode, an issue in issue mode.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryOpenKeysTests : IDisposable
{
    private readonly string? _original;
    private readonly string _db;

    public LibraryOpenKeysTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _db = Path.Combine(Path.GetTempPath(), $"pb_openkeys_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _db;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
        for (int s = 1; s <= 3; s++)
        {
            var series = new Series { Name = $"Series {s}" };
            ctx.Series.Add(series);
            ctx.SaveChanges();
            for (int i = 1; i <= 3; i++)
            {
                ctx.Issues.Add(new Issue { SeriesId = series.Id, Number = i.ToString() });
            }

            ctx.SaveChanges();
        }
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    [Theory]
    [InlineData(LibraryContentGranularity.Series, Key.Enter, PhysicalKey.Enter)]
    [InlineData(LibraryContentGranularity.Series, Key.Space, PhysicalKey.Space)]
    [InlineData(LibraryContentGranularity.Issue, Key.Enter, PhysicalKey.Enter)]
    [InlineData(LibraryContentGranularity.Issue, Key.Space, PhysicalKey.Space)]
    public void EnterAndSpace_OpenTheFocusedTile(LibraryContentGranularity granularity, Key key, PhysicalKey physical)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            int? seriesOpened = null;
            int? issueOpened = null;
            var vm = new LibraryScreenViewModel(goDetail: id => seriesOpened = id, goReaderForIssue: id => issueOpened = id, goToNewIssueProperties: (_, _, _) => { })
            {
                History = new MetadataEditHistoryService(),
                MetadataClipboard = new MetadataClipboardService(),
            };
            vm.Granularity = granularity;
            var screen = new LibraryScreen { DataContext = vm };
            var input = ReaderTestInput.Create();
            screen.InputService = input;
            var window = new Window { Content = screen, Width = 1400, Height = 900 };
            InputHost.Attach(window, input);
            window.Show();
            RunLayout(window);

            var tile = screen.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("card") && b.IsEffectivelyVisible && b.DataContext is SeriesCardSample or IssueListRow);
            Assert.NotNull(tile);
            tile!.Focus(NavigationMethod.Tab);
            RunLayout(window);
            Assert.True(tile.IsFocused);

            window.KeyPress(key, RawInputModifiers.None, physical, null);
            RunLayout(window);
            TestDispatcher.Drain();

            if (granularity == LibraryContentGranularity.Series)
            {
                Assert.NotNull(seriesOpened);
            }
            else
            {
                Assert.NotNull(issueOpened);
            }

            window.Close();
        });
    }

    private static (LibraryScreenViewModel Vm, LibraryScreen Screen, Window Window) ShowSeriesLibrary()
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { })
        {
            History = new MetadataEditHistoryService(),
            MetadataClipboard = new MetadataClipboardService(),
        };
        vm.Granularity = LibraryContentGranularity.Series;
        var screen = new LibraryScreen { DataContext = vm };
        var input = ReaderTestInput.Create();
        screen.InputService = input;
        var window = new Window { Content = screen, Width = 1400, Height = 900 };
        InputHost.Attach(window, input);
        window.Show();
        RunLayout(window);
        return (vm, screen, window);
    }

    /// <summary>docs/superpowers/specs/2026-10-04-library-redesign-design.md, Slice 1: the lens tabs are the Library's tab strip, so Ctrl+PageDown / Ctrl+PageUp step them.</summary>
    [Fact]
    public void CtrlPageDownAndUp_StepTheLensTabs()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var (vm, screen, window) = ShowSeriesLibrary();
            screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).Focus(NavigationMethod.Tab);
            RunLayout(window);

            window.KeyPress(Key.PageDown, RawInputModifiers.Control, PhysicalKey.PageDown, null);
            RunLayout(window);
            TestDispatcher.Drain();
            Assert.Equal(LibraryLens.Reading, vm.ActiveLens);

            window.KeyPress(Key.PageUp, RawInputModifiers.Control, PhysicalKey.PageUp, null);
            RunLayout(window);
            TestDispatcher.Drain();
            Assert.Equal(LibraryLens.All, vm.ActiveLens);

            window.Close();
        });
    }

    private static void MakeFirstSeriesMidRead()
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var issue = ctx.Issues.OrderBy(i => i.Id).First();
        issue.PageCount = 20;
        issue.LastPageRead = 8;
        issue.OpenedTime = DateTime.UtcNow;
        ctx.SaveChanges();
    }

    /// <summary>
    /// docs/superpowers/specs/2026-10-04-library-redesign-design.md, Keyboard: the Continue reading strip's cards are ordinary focus stops above
    /// the grid - Up from the first row of tiles reaches the strip, Down goes back, and a focused strip card drives the inspector.
    /// </summary>
    [Fact]
    public void UpFromTheFirstRow_ReachesTheContinueStrip_AndDownReturnsToTheGrid()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            MakeFirstSeriesMidRead();
            var (vm, screen, window) = ShowSeriesLibrary();
            Assert.True(vm.ShowContinueStrip);

            var stripCard = screen.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("continueCard") && b.IsEffectivelyVisible);
            var tile = screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card") && b.IsEffectivelyVisible);
            Assert.True(stripCard.TranslatePoint(default, screen)!.Value.Y < tile.TranslatePoint(default, screen)!.Value.Y, "the strip sits above the grid");

            tile.Focus(NavigationMethod.Tab);
            RunLayout(window);

            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            RunLayout(window);
            TestDispatcher.Drain();
            Assert.True(stripCard.IsFocused, $"Up from the first row: focus on {window.FocusManager?.GetFocusedElement()}");
            Assert.Equal(((SeriesCardSample)stripCard.DataContext!).SeriesId, vm.PreviewSeries!.SeriesId);

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            RunLayout(window);
            TestDispatcher.Drain();
            var focused = window.FocusManager?.GetFocusedElement() as Button;
            Assert.True(focused is not null && focused.Classes.Contains("card"), $"Down from the strip: focus on {window.FocusManager?.GetFocusedElement()}");

            window.Close();
        });
    }

    /// <summary>The strip's cards sit in a sideways scroller; their focus ring must not be cut by it.</summary>
    [Fact]
    public void ContinueStrip_FocusRings_AreNotClipped()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            MakeFirstSeriesMidRead();
            var (_, screen, window) = ShowSeriesLibrary();

            var clipped = FocusTestHarness.ClippedFocusRings(window, screen);
            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    /// <summary>docs/superpowers/specs/2026-10-04-library-redesign-design.md, Keyboard: Esc inside the inspector returns focus to the grid.</summary>
    [Fact]
    public void Escape_InTheInspector_ReturnsFocusToTheGrid()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var (vm, screen, window) = ShowSeriesLibrary();
            screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).Focus(NavigationMethod.Tab);
            RunLayout(window);
            Assert.NotNull(vm.PreviewSeries);

            var chip = screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("issueChip") && b.IsEffectivelyVisible);
            chip.Focus(NavigationMethod.Tab);
            RunLayout(window);
            Assert.True(chip.IsFocused);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            RunLayout(window);
            TestDispatcher.Drain();

            var focused = window.FocusManager?.GetFocusedElement() as Button;
            Assert.True(focused is not null && focused.Classes.Contains("card"), $"focus on {window.FocusManager?.GetFocusedElement()}");

            window.Close();
        });
    }
}
