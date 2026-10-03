using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Headless checks for the icon-only Library selection bar and the bulk-action shortcuts (docs/superpowers/specs/2026-09-29-library-bulk-
/// actions-design.md §1/§5): one button per bar action, each with a tooltip and an accessible name; Alt+Shift+3 rates the selection; Ctrl+C
/// typed in the search box stays a text copy. Harness as in <c>LibraryScreenViewTests</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SelectionActionBarViewTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public SelectionActionBarViewTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_selbar_view_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
        var series = new Series { Name = "Incredible Hulk", ContentType = ContentType.Comic };
        context.Series.Add(series);
        context.SaveChanges();
        for (int n = 1; n <= 2; n++)
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = n.ToString() });
        }

        context.SaveChanges();
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

    private static void WithThemeAndTokens(Action body)
    {
        TestAppBuilder.EnsureInitialized();
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        var resources = Application.Current.Resources;
        var tokens = new Dictionary<string, object>
        {
            ["PbMotionEase"] = new Avalonia.Animation.Easings.CubicEaseOut(),
            ["PbIconSizeXs"] = 14d,
            ["PbIconSizeSm"] = 16d,
            ["PbIconSizeMd"] = 18d,
            ["PbIconSizeLg"] = 24d,
            ["PbRadiusChip"] = new CornerRadius(6),
            ["PbElevationShadow"] = Avalonia.Media.BoxShadows.Parse("0 2 8 0 #40000000"),
            ["PbDisplayFontFamily"] = new Avalonia.Media.FontFamily("avares://Paperbunkr.App/Assets/Fonts/#Bebas Neue"),
        };
        var added = tokens.Keys.Where(k => !resources.ContainsKey(k)).ToList();
        foreach (var key in added)
        {
            resources[key] = tokens[key];
        }

        try
        {
            body();
        }
        finally
        {
            foreach (var key in added)
            {
                resources.Remove(key);
            }

            Application.Current!.Styles.Remove(theme);
        }
    }

    private static void RunLayout(Window window)
    {
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
    }

    private static (LibraryScreenViewModel Vm, Window Window) ShowWithSelection(Action<IReadOnlyList<int>>? goBulkIssueProperties = null)
    {
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { }, goBulkIssueProperties: goBulkIssueProperties)
        {
            History = new MetadataEditHistoryService(),
            MetadataClipboard = new MetadataClipboardService(),
        };
        // Keys reach the Library through the input service, so attach a real one the way MainWindow does.
        var input = ReaderTestInput.Create();
        var window = new Window { Content = new LibraryScreen { DataContext = vm, InputService = input }, Width = 1400, Height = 900 };
        Services.Input.InputHost.Attach(window, input);
        window.Show();
        RunLayout(window);
        vm.SelectAllVisibleIssuesCommand.Execute(null);
        RunLayout(window);
        return (vm, window);
    }

    private static List<Button> BarButtons(Window window) =>
        window.GetVisualDescendants().OfType<SelectionActionBar>().Single()
            .GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("barAction") && b.IsVisible).ToList();

    [Fact]
    public void Bar_OneLabelledButtonPerAction()
    {
        WithThemeAndTokens(() =>
        {
            var (vm, window) = ShowWithSelection();

            var buttons = BarButtons(window);
            int expected = vm.BarLeadingItems.Concat(vm.BarTrailingItems).Count(i => !i.IsSeparator);
            Assert.Equal(expected, buttons.Count);
            Assert.All(buttons, b =>
            {
                Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(b)));
                Assert.False(string.IsNullOrEmpty(ToolTip.GetTip(b) as string));
                Assert.True(b.Bounds.Width >= 36 && b.Bounds.Height >= 36, $"{AutomationProperties.GetName(b)} is {b.Bounds.Size}");
            });
            window.Close();
        });
    }

    [Fact]
    public void AltShift3_OnACard_RatesTheSelection()
    {
        WithThemeAndTokens(() =>
        {
            var (vm, window) = ShowWithSelection();
            var card = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card"));
            card.Focus();

            window.KeyPress(Key.D3, RawInputModifiers.Alt | RawInputModifiers.Shift, PhysicalKey.Digit3, "3");
            RunLayout(window);

            using var context = PaperbunkrDb.CreateContext();
            Assert.All(context.Issues.ToList(), i => Assert.Equal(3f, i.Rating));
            window.Close();
        });
    }

    [Fact]
    public void CtrlI_OnACard_OpensTheBulkEditorForTheSelection()
    {
        WithThemeAndTokens(() =>
        {
            IReadOnlyList<int>? opened = null;
            var (vm, window) = ShowWithSelection(ids => opened = ids);
            var card = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card"));
            card.Focus();

            window.KeyPress(Key.I, RawInputModifiers.Control, PhysicalKey.I, "i");
            RunLayout(window);

            Assert.NotNull(opened);
            Assert.NotEmpty(opened!);
            window.Close();
        });
    }

    [Fact]
    public void CtrlClick_OnATile_TogglesItsSelection()
    {
        WithThemeAndTokens(() =>
        {
            var (vm, window) = ShowWithSelection();
            vm.ClearSelectionCommand.Execute(null);
            RunLayout(window);
            Assert.Empty(vm.Selection.SelectedIds);

            // A Button marks a left press handled before instance handlers run, so this only works because the screen tunnels the press first.
            var cards = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card") && b.IsVisible).Take(2).ToList();
            foreach (var card in cards)
            {
                var centre = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
                window.MouseMove(centre);
                window.MouseDown(centre, MouseButton.Left, RawInputModifiers.Control);
                window.MouseUp(centre, MouseButton.Left, RawInputModifiers.Control);
                RunLayout(window);
            }

            Assert.Equal(cards.Count, vm.Selection.SelectedIds.Count);
            Assert.True(cards.Count >= 2);

            // Ctrl+click on a selected tile takes it out again.
            var first = cards[0].TranslatePoint(new Point(cards[0].Bounds.Width / 2, cards[0].Bounds.Height / 2), window)!.Value;
            window.MouseDown(first, MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(first, MouseButton.Left, RawInputModifiers.Control);
            RunLayout(window);
            Assert.Equal(cards.Count - 1, vm.Selection.SelectedIds.Count);
            window.Close();
        });
    }

    [Fact]
    public void CtrlC_InTheSearchBox_DoesNotCopyData()
    {
        WithThemeAndTokens(() =>
        {
            var (vm, window) = ShowWithSelection();
            var search = window.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible);
            search.Focus();

            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, "c");
            RunLayout(window);

            Assert.False(vm.HasMetadataClipboard);
            window.Close();
        });
    }
}
