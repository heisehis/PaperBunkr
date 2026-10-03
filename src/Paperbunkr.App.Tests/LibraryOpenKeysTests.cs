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
}
