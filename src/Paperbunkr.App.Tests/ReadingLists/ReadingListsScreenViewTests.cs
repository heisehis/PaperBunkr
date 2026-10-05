using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.Tests.ReadingLists;

/// <summary>
/// Headless checks that the redesigned Reading Lists views load and show real data (docs/superpowers/specs/2026-09-28-reading-lists-
/// redesign-design.md, "Testing"): the gallery, the list page in reading mode, the cover wall, Edit mode and the drawer, and that a long
/// list stays virtualized. A build alone doesn't prove XAML loads (CLAUDE.md).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingListsScreenViewTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ReadingListsScreenViewTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_view_test_{Guid.NewGuid():N}.db");
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

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    /// <summary>The Fluent theme and the app tokens the views' StaticResources need (App.axaml isn't loaded headless).</summary>
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
            ["PbIconSizeLg"] = 24d,
            ["PbRadiusChip"] = new CornerRadius(6),
            ["PbElevationShadow"] = Avalonia.Media.BoxShadows.Parse("0 2 8 0 #40000000"),
            ["PbAccentBrush"] = Brushes.OrangeRed,
            ["PbGlowRing"] = Avalonia.Media.BoxShadows.Parse("0 0 0 4 #99E0995A"),
        };
        // Always override: an earlier test in the full suite can leave a real PbAccentBrush behind, which the ring assertions would then see.
        var previous = tokens.Keys.ToDictionary(k => k, k => resources.TryGetResource(k, null, out var v) ? v : null);
        var hadKey = tokens.Keys.ToDictionary(k => k, k => resources.ContainsKey(k));
        foreach (var key in tokens.Keys)
        {
            resources[key] = tokens[key];
        }

        try
        {
            body();
        }
        finally
        {
            foreach (var key in tokens.Keys)
            {
                if (hadKey[key])
                {
                    resources[key] = previous[key]!;
                }
                else
                {
                    resources.Remove(key);
                }
            }

            Application.Current!.Styles.Remove(theme);
        }
    }

    private static List<string?> VisibleTexts(Window window)
    {
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
        TestDispatcher.Drain();
        return window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
    }

    private static int SeedList(string name, int count, int read, Func<int, string?>? label = null, int? folder = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name };
        var list = new ReadingList { Name = name };
        for (int n = 1; n <= count; n++)
        {
            list.Items.Add(new ReadingListItem
            {
                Issue = new Issue { Series = series, Number = n.ToString(), FilePath = $"c:/{name}{n}.cbz", PageCount = 10, LastPageRead = n <= read ? 9 : null },
                SortOrder = n - 1,
                GroupLabel = label?.Invoke(n),
            });
        }

        context.ReadingLists.Add(list);
        ReadingListFolders.PlaceNewList(context, list, folder);
        context.SaveChanges();
        return list.Id;
    }

    private static (ReadingListsScreenViewModel Vm, Window Window) Show(Action<int, int>? goReader = null)
    {
        var vm = new ReadingListsScreenViewModel(new FakeFilePicker(), goReader ?? ((_, _) => { }));
        var window = new Window { Content = new ReadingListsScreen { DataContext = vm }, Width = 1200, Height = 800 };
        window.Show();
        return (vm, window);
    }

    [Fact]
    public void Gallery_ShowsTilesAndTheContinueRow()
    {
        WithThemeAndTokens(() =>
        {
            int folder;
            using (var context = PaperbunkrDb.CreateContext())
            {
                var f = ReadingListFolders.Create(context, "Crisis Events", null);
                context.SaveChanges();
                folder = f.Id;
            }

            SeedList("Saga", 4, 1);
            SeedList("Infinite Crisis", 6, 0, folder: folder);
            var (vm, window) = Show();
            vm.Gallery.Refresh();

            var texts = VisibleTexts(window);
            Assert.True(texts.Contains("Reading Lists") && texts.Contains("Crisis Events") && texts.Contains("Saga") && texts.Contains("1 / 4 read"),
                string.Join(" | ", texts));
            window.Close();
        });
    }

    /// <summary>
    /// The gallery is one of two views that stay attached side by side in <see cref="ReadingListsScreen"/> (only IsVisible/opacity toggles),
    /// so - like the list page before its own fix - nothing ever put focus back inside it when shown, and arrow keys silently went nowhere
    /// without a prior click. Also checks the tile's ring shows (the same BorderBrush-resolution technique as the row test), since the ring
    /// selector direction bug that hit rows could just as easily hit tiles.
    /// </summary>
    [Fact]
    public void Gallery_FocusesATile_WithNoPriorClick_AndArrowsMoveWithARing()
    {
        WithThemeAndTokens(() =>
        {
            SeedList("Saga", 4, 1);
            SeedList("Annihilation", 6, 0);
            var (vm, window) = Show();
            vm.Gallery.Refresh();
            VisibleTexts(window);

            var tiles = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("rlTile")).ToList();
            Assert.Contains(tiles, t => ReferenceEquals(t, Focused(window)));

            var focused = (Button)Focused(window)!;
            var cover = focused.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("rlTileCover"));
            Assert.Equal(Brushes.OrangeRed, cover.BorderBrush);

            Press(window, Key.Right);
            Assert.NotSame(focused, Focused(window));
            Assert.Contains(tiles, t => ReferenceEquals(t, Focused(window)));
            window.Close();
        });
    }

    [Fact]
    public void Gallery_UpFromTheFirstRow_ReachesTheContinueReadingCard_AndDownComesBack()
    {
        WithThemeAndTokens(() =>
        {
            SeedList("Saga", 4, 1);              // in progress, so the Continue Reading card shows
            SeedList("Annihilation", 6, 0);
            var (vm, window) = Show();
            vm.Gallery.Refresh();
            VisibleTexts(window);

            var tiles = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("rlTile")).ToList();
            var first = tiles.OrderBy(t => t.TranslatePoint(default, window)!.Value.Y).ThenBy(t => t.TranslatePoint(default, window)!.Value.X).First();
            first.Focus(NavigationMethod.Tab);
            VisibleTexts(window);
            Assert.Same(first, Focused(window));

            Press(window, Key.Up);
            var above = Focused(window);
            Assert.NotNull(above);
            Assert.False(tiles.Contains(above as Button), $"Up left the tile grid; focus is on {above?.GetType().Name}");
            Assert.True(((Visual)above!).TranslatePoint(default, window)!.Value.Y < first.TranslatePoint(default, window)!.Value.Y, "focus moved up the screen");

            Press(window, Key.Down);
            Assert.Contains(tiles, t => ReferenceEquals(t, Focused(window)));
            window.Close();
        });
    }

    /// <summary>The reported gap: opening a folder resets Tiles in place while the gallery stays visible the whole time, so the folder tile
    /// that had focus is detached along with the old content - and without <c>OnTilesChanged</c> wiring it into <see cref="FocusReclaimer"/>,
    /// nothing takes its place (focus fell back to whatever the window considers next, observed as the app's nav rail).</summary>
    [Fact]
    public void OpeningAFolder_KeepsFocusInsideTheGallery()
    {
        WithThemeAndTokens(() =>
        {
            using (var context = PaperbunkrDb.CreateContext())
            {
                var f = ReadingListFolders.Create(context, "Crisis Events", null);
                context.SaveChanges();
                SeedList("Infinite Crisis", 6, 0, folder: f.Id);
            }

            var (vm, window) = Show();
            vm.Gallery.Refresh();
            VisibleTexts(window);

            var folderTile = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("rlTile"));
            Assert.Same(folderTile, Focused(window));

            Press(window, Key.Enter);
            var tilesInFolder = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("rlTile")).ToList();
            Assert.Single(tilesInFolder);
            Assert.Contains(tilesInFolder, t => ReferenceEquals(t, Focused(window)));
            window.Close();
        });
    }

    [Fact]
    public void ListPage_ShowsHeroChaptersUpNext_ThenCoversEditAndDrawer()
    {
        WithThemeAndTokens(() =>
        {
            int list = SeedList("Crisis", 8, 3, n => n <= 2 ? "Prelude" : "Main event");
            var (vm, window) = Show();
            vm.LoadReadingList(list, triggerEntrance: true);

            var texts = VisibleTexts(window);
            Assert.True(texts.Contains("Crisis") && texts.Contains("8 issues · 3 read"), string.Join(" | ", texts));
            Assert.Contains("▸ Prelude · 2 · all read", texts);
            Assert.Contains("Up next · 4 of 8", texts);

            vm.List.SetViewModeCommand.Execute(ReadingListPageViewModel.CoversMode);
            texts = VisibleTexts(window);
            Assert.Contains("4 ▶ next", texts);
            vm.List.SetViewModeCommand.Execute(ReadingListPageViewModel.PathMode);

            vm.List.BeginEditCommand.Execute(null);
            texts = VisibleTexts(window);
            Assert.Contains("Editing Crisis", texts);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "⠿" && t.IsEffectivelyVisible);

            vm.List.EndEditCommand.Execute(null);
            vm.List.ToggleAddIssuesCommand.Execute(null);
            texts = VisibleTexts(window);
            Assert.Contains("Add issues", texts);
            window.Close();
        });
    }

    private static void Press(Window window, Key key)
    {
        string physical = key is Key.Left or Key.Right or Key.Up or Key.Down ? $"Arrow{key}" : key == Key.Enter ? "Enter" : key.ToString();
        window.KeyPress(key, RawInputModifiers.None, (PhysicalKey)Enum.Parse(typeof(PhysicalKey), physical), null);
        window.UpdateLayout();
        TestDispatcher.Drain();
        window.UpdateLayout();
        TestDispatcher.Drain();
    }

    private static Visual? Focused(Window window) => window.FocusManager?.GetFocusedElement() as Visual;

    private static bool IsInside(Visual? visual, string name) =>
        visual?.GetSelfAndVisualAncestors().OfType<Control>().Any(c => c.Name == name) == true;

    private static Button RowCard(Window window, int position) =>
        window.GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("rlCard") && b.DataContext is PathRowItem item && item.Row.Position == position);

    [Fact]
    public void ARowClick_OnlyFocuses_DoubleClickAndEnterRead()
    {
        WithThemeAndTokens(() =>
        {
            int list = SeedList("Hulk", 6, 1);
            var opened = new List<int>();
            var (vm, window) = Show((issueId, _) => opened.Add(issueId));
            vm.LoadReadingList(list, triggerEntrance: true);
            VisibleTexts(window);

            var card = RowCard(window, 4);
            PointerPressedEventArgs? press = null;
            card.AddHandler(InputElement.PointerPressedEvent, (_, e) => press ??= e, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            var centre = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre);
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            VisibleTexts(window);
            Assert.Empty(opened);
            Assert.Same(card, Focused(window));

            Press(window, Key.Enter);
            Assert.Single(opened);

            // Headless double-click detection is timing-sensitive, so raise the gesture itself from where the click landed.
            Assert.NotNull(press);
            ((Interactive)press!.Source!).RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, press));
            Assert.Equal(2, opened.Count);

            // ...but not from a button inside the card (the ⋯ here): that only opens its own thing.
            var more = card.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("rlMore"));
            more.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, press));
            Assert.Equal(2, opened.Count);
            window.Close();
        });
    }

    [Fact]
    public void ArrowKeys_StayInsideThePage()
    {
        WithThemeAndTokens(() =>
        {
            int list = SeedList("Hulk", 6, 0);
            var (vm, window) = Show();
            vm.LoadReadingList(list, triggerEntrance: true);
            VisibleTexts(window);

            // Opening lands on Up next (the first item here); Up from the top of the path goes to the hero's main action.
            Assert.True(IsInside(Focused(window), "PathList"), $"focus on {Focused(window)}");
            Press(window, Key.Up);
            Assert.True(IsInside(Focused(window), "HeroActions"), $"focus on {Focused(window)}");

            // Up walks the hero's rows; at the top row more Up / PageUp stays put rather than escaping.
            for (int i = 0; i < 6; i++)
            {
                Press(window, Key.Up);
            }

            var top = Focused(window);
            Assert.True(IsInside(top, "MainArea"), $"focus on {top}");
            Press(window, Key.Up);
            Press(window, Key.PageUp);
            Assert.Same(top, Focused(window));

            // Left off the start of a row goes to the cover rail; Up there stays in the rail; Right comes back into the path.
            Press(window, Key.Left);
            Assert.True(IsInside(Focused(window), "CoverRail"), $"focus on {Focused(window)}");
            Press(window, Key.Up);
            Press(window, Key.Up);
            Assert.True(IsInside(Focused(window), "CoverRail"), $"focus on {Focused(window)}");
            Press(window, Key.Right);
            Assert.True(IsInside(Focused(window), "PathList"), $"focus on {Focused(window)}");

            // Left from a row goes to the rail too - never anywhere outside the page.
            Press(window, Key.Down);
            Press(window, Key.Left);
            Assert.True(IsInside(Focused(window), "CoverRail"), $"focus on {Focused(window)}");
            window.Close();
        });
    }

    /// <summary>
    /// A row is one Button.rlCard wrapping Border.rlRow - Border.rlRow is the button's CHILD, not its container, so a style rule written as
    /// "Border.rlRow:focus-within" can never match (self-or-descendant only) and was a real regression: focus moved correctly but the ring
    /// never showed. This checks the resolved BorderBrush, not just which element holds focus, so it would have caught that.
    /// </summary>
    [Fact]
    public void FocusRings_AreNotClipped_InTheGalleryAndAList()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = FocusTestHarness.AppStyles();
            int list = SeedList("Hulk", 6, 0);
            SeedList("Spider-Man", 3, 1);
            var (vm, window) = Show();
            FocusTestHarness.RunLayout(window);
            var clipped = FocusTestHarness.ClippedFocusRings(window, (Visual)window.Content!).Select(c => "[gallery] " + c).ToList();

            vm.LoadReadingList(list, triggerEntrance: false);
            FocusTestHarness.RunLayout(window);
            clipped.AddRange(FocusTestHarness.ClippedFocusRings(window, (Visual)window.Content!).Select(c => "[list] " + c));
            window.Close();
            Assert.True(clipped.Count == 0, string.Join(Environment.NewLine, clipped));
        });
    }

    [Fact]
    public void ARow_ShowsTheAccentRing_WhenItsCardIsKeyboardFocused()
    {
        WithThemeAndTokens(() =>
        {
            int list = SeedList("Hulk", 6, 0);
            var (vm, window) = Show();
            vm.LoadReadingList(list, triggerEntrance: true);
            VisibleTexts(window);

            var card = RowCard(window, 2);
            var row = card.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("rlRow"));
            Assert.Equal(Brushes.Transparent, row.BorderBrush);

            card.Focus();
            VisibleTexts(window);
            Assert.Equal(Brushes.OrangeRed, row.BorderBrush);
            window.Close();
        });
    }

    [Fact]
    public void Hero_ShowsTheSynopsisBody_AndTagsCanBeAddedAndRemoved()
    {
        WithThemeAndTokens(() =>
        {
            int list = SeedList("Planet Hulk", 3, 0);
            using (var context = PaperbunkrDb.CreateContext())
            {
                context.ReadingLists.Find(list)!.Description = "Plot Summary\nThe Illuminati meet to discuss the recent destruction of Hulk and how to deal with him.";
                context.SaveChanges();
            }

            var (vm, window) = Show();
            vm.LoadReadingList(list, triggerEntrance: true);
            var texts = VisibleTexts(window);
            Assert.Contains(texts, t => t?.StartsWith("The Illuminati meet") == true);
            Assert.DoesNotContain(texts, t => t?.StartsWith("Plot Summary") == true);

            vm.List.BeginAddTagCommand.Execute(null);
            vm.List.NewTagText = "Cosmic, Gladiators, cosmic";
            vm.List.CommitAddTagCommand.Execute(null);
            Assert.Equal(new[] { "Cosmic", "Gladiators" }, vm.List.Tags.Select(t => t.Value));
            texts = VisibleTexts(window);
            Assert.Contains("Gladiators", texts);

            vm.List.RemoveTagCommand.Execute(vm.List.Tags[0]);
            TestDispatcher.Drain();
            Assert.Equal(new[] { "Gladiators" }, vm.List.Tags.Select(t => t.Value));
            using (var context = PaperbunkrDb.CreateContext())
            {
                Assert.Equal(new[] { "Gladiators" }, context.ReadingListTags.Where(t => t.ReadingListId == list).Select(t => t.Value).ToArray());
            }

            window.Close();
        });
    }

    [Theory]
    [InlineData("Plot Summary\nAfter Secret Invasion, Black Bolt, King of the Inhumans, left Earth with his people.", "After Secret Invasion, Black Bolt, King of the Inhumans, left Earth with his people.")]
    [InlineData("Background\nOne World Under Doom\nDuring her recent tenure as chairperson of the Avengers, Carol Danvers entered a partnership.",
        "During her recent tenure as chairperson of the Avengers, Carol Danvers entered a partnership.")]
    [InlineData("Non-U.S. Editions\nBatman: Metal (German)\nBatman Metal #1 (German)", "Non-U.S. Editions\n\nBatman: Metal (German)\n\nBatman Metal #1 (German)")]
    [InlineData("A plain one-paragraph description.", "A plain one-paragraph description.")]
    public void CleanSynopsis_DropsSectionHeadings(string raw, string expected) =>
        Assert.Equal(expected, ReadingListPageViewModel.CleanSynopsis(raw));

    [Fact]
    public void ALongList_StaysVirtualized()
    {
        WithThemeAndTokens(() =>
        {
            int list = SeedList("Long", 300, 0);
            var (vm, window) = Show();
            vm.LoadReadingList(list);
            VisibleTexts(window);

            var path = window.GetVisualDescendants().OfType<ItemsControl>().First(i => i.Name == "PathList");
            int realized = path.GetRealizedContainers().Count();
            var scroller = path.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            var panel = path.GetVisualDescendants().OfType<VirtualizingStackPanel>().FirstOrDefault();
            Assert.True(realized is >= 1 and <= 60,
                $"realized={realized} pathBounds={path.Bounds} viewport={scroller?.Viewport} extent={scroller?.Extent} panel={panel?.GetType().Name} panelBounds={panel?.Bounds}");
            window.Close();
        });
    }
}
