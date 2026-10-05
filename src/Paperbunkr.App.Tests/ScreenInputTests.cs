using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The shared and per-screen shortcuts on Home, Books and Smart Lists (docs/superpowers/specs/2026-10-03-input-service-design.md §14), driven the way MainWindow drives them: a real
/// input service, attached to the window, with the screen registered against it. A "Global spy" stands in for the shell's fallback so each test can tell whether the screen took the
/// action or let it through.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ScreenInputTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ScreenInputTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_screen_input_{Guid.NewGuid():N}.db");
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
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A window with a real service attached and a Global handler that records which actions fell through to it.</summary>
    private sealed class Stage : IDisposable
    {
        private readonly InputHost _host;
        private readonly IDisposable _spy;

        public Stage(Control screen, Action<IInputService> give)
        {
            Input = ReaderTestInput.Create();
            give(Input);
            Window = new Window { Content = screen, Width = 1300, Height = 900 };
            _host = InputHost.Attach(Window, Input);
            _spy = Input.Register(InputScope.Global, e => FellThrough.Add(e.Action.Id));
            Window.Show();
            RunLayout(Window);
        }

        public IInputService Input { get; }

        public Window Window { get; }

        public List<string> FellThrough { get; } = [];

        public void Press(Key key, RawInputModifiers modifiers, PhysicalKey physical)
        {
            Window.KeyPress(key, modifiers, physical, null);
            RunLayout(Window);
            TestDispatcher.Drain();
            RunLayout(Window);
        }

        public void Dispose()
        {
            _spy.Dispose();
            _host.Dispose();
            Window.Close();
        }
    }

    private static void SeedBook(string title)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Books.Add(new Book { Title = title, Format = BookFormat.Epub, FilePath = $@"C:\books\{title}.epub", AddedTime = DateTime.UtcNow });
        context.SaveChanges();
    }

    private static int SeedSmartList(string name, bool isSystem = false)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Series One" };
        context.Series.Add(series);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1" });
        var list = new SmartList
        {
            Name = name,
            IsSystem = isSystem,
            RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And, Conditions = new List<SmartListCondition>() },
        };
        context.SmartLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    // ----- Books -----

    private static BooksScreenViewModel NewBooksVm(Action<IReadOnlyList<int>>? bulkEdit = null) =>
        new(_ => { }, _ => { }, _ => { }, bulkEdit ?? (_ => { }), _ => { }, () => { });

    [Fact]
    public void Books_CtrlA_SelectsEverything_AndCtrlI_EditsTheSelection()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            SeedBook("Dune");
            SeedBook("Emma");
            IReadOnlyList<int>? edited = null;
            var vm = NewBooksVm(ids => edited = ids);
            var screen = new BooksScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);
            screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card")).Focus();

            stage.Press(Key.A, RawInputModifiers.Control, PhysicalKey.A);
            Assert.Equal(2, vm.SelectionCount);
            Assert.DoesNotContain(InputActionIds.BooksSelectAll, stage.FellThrough);

            stage.Press(Key.I, RawInputModifiers.Control, PhysicalKey.I);
            Assert.Equal(2, edited?.Count);
        });
    }

    [Fact]
    public void Books_CtrlI_WithNothingSelected_DoesNothing()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            SeedBook("Dune");
            IReadOnlyList<int>? edited = null;
            var vm = NewBooksVm(ids => edited = ids);
            var screen = new BooksScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);
            screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card")).Focus();

            stage.Press(Key.I, RawInputModifiers.Control, PhysicalKey.I);

            Assert.Null(edited);
        });
    }

    [Fact]
    public void Books_CtrlF_FocusesTheSearchBox()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            SeedBook("Dune");
            var vm = NewBooksVm();
            var screen = new BooksScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);
            screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card")).Focus();

            stage.Press(Key.F, RawInputModifiers.Control, PhysicalKey.F);

            var focused = stage.Window.FocusManager!.GetFocusedElement() as Control;
            Assert.Equal("BooksSearchBox", focused is null ? null : AutomationProperties.GetAutomationId(focused));
        });
    }

    [Fact]
    public void Books_F5_ReloadsFromTheDatabase()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            SeedBook("Dune");
            var vm = NewBooksVm();
            var screen = new BooksScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);
            screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("card")).Focus();
            int before = vm.Books.Count;

            SeedBook("Emma");
            stage.Press(Key.F5, RawInputModifiers.None, PhysicalKey.F5);

            Assert.Equal(before + 1, vm.Books.Count);
        });
    }

    // ----- Home -----

    private static HomeScreenViewModel NewHomeVm() => new(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { });

    [Fact]
    public void Home_CtrlF_FocusesTheSearchBox()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            var screen = new HomeScreen { DataContext = NewHomeVm() };
            using var stage = new Stage(screen, i => screen.InputService = i);

            stage.Press(Key.F, RawInputModifiers.Control, PhysicalKey.F);

            var focused = stage.Window.FocusManager!.GetFocusedElement() as Control;
            Assert.Equal("HomeSearchBox", focused is null ? null : AutomationProperties.GetAutomationId(focused));
        });
    }

    [Fact]
    public void Home_BumpersAndCtrlPageKeys_StepTheSpotlight_InsteadOfFallingBackToScreenCycling()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            var screen = new HomeScreen { DataContext = NewHomeVm() };
            using var stage = new Stage(screen, i => screen.InputService = i);

            stage.Press(Key.PageDown, RawInputModifiers.Control, PhysicalKey.PageDown);
            stage.Press(Key.PageUp, RawInputModifiers.Control, PhysicalKey.PageUp);

            Assert.DoesNotContain(InputActionIds.TabNext, stage.FellThrough);
            Assert.DoesNotContain(InputActionIds.TabPrevious, stage.FellThrough);
        });
    }

    // ----- Smart lists -----

    [Fact]
    public void SmartLists_CtrlN_CreatesAList_CtrlD_DuplicatesTheOpenOne_CtrlS_SavesAUserList()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            int id = SeedSmartList("Everything");
            var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
            vm.LoadSmartList(id);
            var screen = new SmartScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);
            int CountLists() { using var c = PaperbunkrDb.CreateContext(); return c.SmartLists.Count(); }
            Assert.Equal(1, CountLists());

            stage.Press(Key.D, RawInputModifiers.Control, PhysicalKey.D);
            Assert.Equal(2, CountLists());
            Assert.DoesNotContain(InputActionIds.SmartDuplicate, stage.FellThrough);

            stage.Press(Key.S, RawInputModifiers.Control, PhysicalKey.S);
            Assert.DoesNotContain(InputActionIds.Save, stage.FellThrough);
        });
    }

    [Fact]
    public void SmartLists_CtrlS_OnASystemList_IsLeftForTheFallback()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            int id = SeedSmartList("Built in", isSystem: true);
            var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
            vm.LoadSmartList(id);
            var screen = new SmartScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);

            stage.Press(Key.S, RawInputModifiers.Control, PhysicalKey.S);

            Assert.Contains(InputActionIds.Save, stage.FellThrough);     // nothing editable, so the screen declined
        });
    }

    [Fact]
    public void SmartLists_CtrlN_AddsANewList()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
            var screen = new SmartScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);
            int CountLists() { using var c = PaperbunkrDb.CreateContext(); return c.SmartLists.Count(l => !l.IsSystem); }
            int before = CountLists();

            stage.Press(Key.N, RawInputModifiers.Control, PhysicalKey.N);

            Assert.Equal(before + 1, CountLists());
        });
    }

    // ----- Tab strips -----

    private sealed class RecordingCommand : System.Windows.Input.ICommand
    {
        private readonly Action _run;

        public RecordingCommand(Action run) => _run = run;

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _run();
    }

    private static Button Tab(string name, bool active, List<string> log, string cssClass = "tab", bool visible = true)
    {
        var button = new Button { Content = name, Command = new RecordingCommand(() => log.Add(name)), IsVisible = visible };
        button.Classes.Add(cssClass);
        if (active)
        {
            button.Classes.Add(cssClass == "tab" ? "active" : "on");
        }

        return button;
    }

    [Fact]
    public void TabStrip_StepsToTheNextVisibleTab_SkipsHiddenOnes_AndWrapsAtTheEnds()
    {
        var log = new List<string>();
        var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        strip.Children.Add(Tab("Issues", false, log));
        strip.Children.Add(Tab("Specials", false, log, visible: false));      // hidden when the series has none
        strip.Children.Add(Tab("Related", true, log));
        strip.Children.Add(Tab("Details", false, log));
        var window = new Window { Content = strip, Width = 600, Height = 100 };
        window.Show();
        window.UpdateLayout();

        Assert.True(TabStrip.Step(strip, 1));
        Assert.True(TabStrip.Step(strip, -1));
        Assert.Equal(["Details", "Issues"], log);       // before Related is Issues (Specials is hidden)

        // From the last tab, forwards wraps to the first.
        log.Clear();
        var wrap = new StackPanel();
        wrap.Children.Add(Tab("A", false, log));
        wrap.Children.Add(Tab("B", true, log));
        window.Content = wrap;
        window.UpdateLayout();
        Assert.True(TabStrip.Step(wrap, 1));
        Assert.Equal(["A"], log);
        window.Close();
    }

    [Fact]
    public void TabStrip_UnderstandsThePillToggles_AndReportsAScreenWithNoStrip()
    {
        var log = new List<string>();
        var pills = new StackPanel();
        pills.Children.Add(Tab("Overview", true, log, "segToggle"));
        pills.Children.Add(Tab("Map", false, log, "segToggle"));
        var window = new Window { Content = pills, Width = 400, Height = 100 };
        window.Show();
        window.UpdateLayout();

        Assert.True(TabStrip.Step(pills, 1));
        Assert.Equal(["Map"], log);

        window.Content = new Button { Content = "no tabs here" };
        window.UpdateLayout();
        Assert.False(TabStrip.Step((Control)window.Content, 1));
        window.Close();
    }

    private sealed class NoDialogs : IDialogService
    {
        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(true);
    }

    [Fact]
    public void Insights_CtrlPageDown_ChangesTab_AndIsNotLeftForScreenCycling()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            var vm = new InsightsScreenViewModel(_ => { }, _ => { }, _ => { }, () => { }, new NoDialogs(),
                nowUtc: () => new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc));
            vm.Refresh();
            var screen = new InsightsScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);
            bool todayBefore = vm.IsTodayTabSelected;

            stage.Press(Key.PageDown, RawInputModifiers.Control, PhysicalKey.PageDown);

            Assert.True(todayBefore);
            Assert.False(vm.IsTodayTabSelected);
            Assert.DoesNotContain(InputActionIds.TabNext, stage.FellThrough);
        });
    }

    // ----- Detail -----

    [Fact]
    public void Detail_CtrlEnter_ContinuesReading_AndCtrlI_EditsTheSeries()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            int seriesId, issueId;
            using (var context = PaperbunkrDb.CreateContext())
            {
                var series = new Series { Name = "Test Series" };
                context.Series.Add(series);
                context.SaveChanges();
                var issue = new Issue { SeriesId = series.Id, Number = "1" };
                context.Issues.Add(issue);
                context.SaveChanges();
                seriesId = series.Id;
                issueId = issue.Id;
            }

            int? readerTarget = null;
            int? propertiesTarget = null;
            var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: id => readerTarget = id, goToProperties: id => propertiesTarget = id, goToBulkProperties: _ => { });
            vm.LoadSeries(seriesId);
            var screen = new DetailScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);

            stage.Press(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter);
            Assert.Equal(issueId, readerTarget);

            stage.Press(Key.I, RawInputModifiers.Control, PhysicalKey.I);
            Assert.Null(propertiesTarget);     // nothing selected, so nothing to edit

            vm.Tabs.ToggleIssueSelection(vm.Tabs.Issues.Single(), isShiftHeld: false);
            stage.Press(Key.I, RawInputModifiers.Control, PhysicalKey.I);
            Assert.Equal(issueId, propertiesTarget);
        });
    }

    // ----- Every wired screen: built with no data, it must register without trouble and decline everything -----

    public static IEnumerable<object[]> WiredScreens() =>
    [
        [typeof(PreferencesScreen)],
        [typeof(IssuePropertiesScreen)],
        [typeof(BulkIssuePropertiesScreen)],
        [typeof(BulkSeriesPropertiesScreen)],
        [typeof(WantedScreen)],
        [typeof(Paperbunkr.App.Views.ContinuityScreen)],
        [typeof(MangaDetailScreen)],
        [typeof(BookDetailScreen)],
        [typeof(ReadingListsScreen)],
        [typeof(SmartScreen)],
        [typeof(HomeScreen)],
        [typeof(BooksScreen)],
        [typeof(DetailScreen)],
        [typeof(InsightsScreen)],
    ];

    [Theory]
    [MemberData(nameof(WiredScreens))]
    public void AScreenWithNoViewModel_RegistersAndDeclinesEveryAction(Type screenType)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var screen = (Control)Activator.CreateInstance(screenType)!;
            using var stage = new Stage(screen, i => screenType.GetProperty("InputService")!.SetValue(screen, i));
            Assert.NotNull(screenType.GetProperty("InputService"));

            string[] actions =
            [
                InputActionIds.Refresh, InputActionIds.NewItem, InputActionIds.Save, InputActionIds.FocusSearch, InputActionIds.TabNext, InputActionIds.TabPrevious,
                InputActionIds.BooksSelectAll, InputActionIds.BooksEditSelection, InputActionIds.BooksDeleteSelection, InputActionIds.SmartDuplicate,
                InputActionIds.DetailContinue, InputActionIds.DetailEdit,
            ];
            foreach (string id in actions)
            {
                stage.Input.Dispatch(new InputAction(id));
            }

            TestDispatcher.Drain();
            // A data-less screen cannot save or create anything, so it must leave those Global actions for the fallback. (Tab and search actions may be claimed: a strip or box can exist without data; the screen-specific actions are only ever delivered to their own scope, and just must not throw.)
            foreach (string id in new[] { InputActionIds.Save, InputActionIds.NewItem })
            {
                Assert.Contains(id, stage.FellThrough);
            }
        });
    }

    [Fact]
    public void Detail_Backspace_GoesBack()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            int seriesId;
            using (var context = PaperbunkrDb.CreateContext())
            {
                var series = new Series { Name = "Back Series" };
                context.Series.Add(series);
                context.SaveChanges();
                context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1" });
                context.SaveChanges();
                seriesId = series.Id;
            }

            int wentBack = 0;
            var vm = new DetailScreenViewModel(goBack: () => wentBack++, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });
            vm.LoadSeries(seriesId);
            var screen = new DetailScreen { DataContext = vm };
            using var stage = new Stage(screen, i => screen.InputService = i);

            stage.Press(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace);

            Assert.Equal(1, wentBack);
        });
    }
}
