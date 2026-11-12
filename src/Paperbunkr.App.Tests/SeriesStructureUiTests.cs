using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.ContinuityScreen;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

[Collection(nameof(AvaloniaTestCollection))]
public class SeriesStructureUiTests : IDisposable
{
    private readonly string? _oldPath = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _oldImages = EntityImageCache.CacheDirectory;
    private readonly string _oldEvents = StoryEventCoverPaths.CacheDirectory;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pb_structure_{Guid.NewGuid():N}");
    private readonly CoverCacheTestRedirect _covers = new();
    private readonly int _character, _team, _creator, _continuity, _series;
    private PaperbunkrDbContext Context() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>()
        .UseSqlite($"Data Source={Path.Combine(_folder, "library.db")}").Options);
    public SeriesStructureUiTests()
    {
        Directory.CreateDirectory(_folder);
        PaperbunkrDbContext.DatabasePathOverride = Path.Combine(_folder, "library.db");
        EntityImageCache.CacheDirectory = Path.Combine(_folder, "entity-images");
        StoryEventCoverPaths.CacheDirectory = Path.Combine(_folder, "event-covers");
        using var context = Context(); context.Database.EnsureCreated();
        var a = new Series { Name = "Uncanny X-Men", ContentType = ContentType.Comic };
        var b = new Series { Name = "Ultimate X-Men", ContentType = ContentType.Comic };
        for (int n = 1; n <= 8; n++)
        {
            var issue = new Issue { Series = n <= 6 ? a : b, Number = n.ToString(), Year = 1980 + n, PageCount = 20, LastPageRead = n == 1 ? 19 : 0,
                Characters = "Storm", Teams = "X-Men", Locations = "Westchester", Writer = n <= 6 ? "Claremont" : "Millar",
                Title = $"Phoenix Part {n}", FilePath = $"C:\\fixtures\\issue{n}.cbz", StoryArc = n <= 3 ? "Phoenix" : null };
            context.Issues.Add(issue);
        }
        var first = new Continuity { Name = "Earth-616" }; var second = new Continuity { Name = "Ultimate" };
        context.ContinuityMemberships.AddRange(new ContinuityMembership { Series = a, Continuity = first }, new ContinuityMembership { Series = b, Continuity = second });
        context.SaveChanges(); EntityIndexSync.RebuildAll(context);
        context.EventMemberships.Add(new EventMembership { StoryEvent = new StoryEvent { Name = "Phoenix Saga" }, IssueId = a.Issues.First().Id });
        context.SaveChanges();
        _character = context.Characters.Single().Id; _team = context.Teams.Single().Id;
        _creator = context.Creators.Single(c => c.Name == "Claremont").Id; _continuity = first.Id; _series = a.Id;
    }
    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _oldPath; EntityImageCache.CacheDirectory = _oldImages; StoryEventCoverPaths.CacheDirectory = _oldEvents; _covers.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }
    private MetadataEntityDetailScreenViewModel Page() => new(() => { }, _ => { }, _ => { }, Context,
        (_, _, _) => Task.FromResult(EntityProfileOutcome.NotDue), action => { action(); return Task.CompletedTask; });
    internal static void Pump(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(20)) { TestDispatcher.Drain(); Thread.Sleep(2); }
        Assert.True(task.IsCompleted, "Asynchronous page did not finish"); task.GetAwaiter().GetResult(); TestDispatcher.Drain();
    }
    [Fact] public void Page_NarrowsCards_ReadsFirstUnread_AndSavesTheSameOrder()
    {
        var page = Page(); ReadingQueue? queue = null; int read = 0, saved = 0;
        page.ReadQueue = (id, order) => { read = id; queue = order; }; page.OpenReadingList = id => saved = id;
        page.Load(ComicMetadataEntityKind.Character, _character); Pump(page.LoadingTask);
        Assert.Contains("8 appearances", page.Counts); Assert.Equal(2, page.Continuities.Count); Assert.Single(page.Events);
        page.ReadInOrderCommand.Execute(null); Assert.Equal(8, queue!.IssueIds.Count);
        using (var context = Context()) Assert.Equal("2", context.Issues.Find(read)!.Number);
        page.SelectContinuity(_continuity); Pump(page.LoadingTask);
        Assert.Contains("6 appearances", page.Counts); Assert.Single(page.Series); Assert.Single(page.Continuities);
        Assert.Equal(3, page.ContinuityChoices.Count); Assert.Equal(5, page.Unread.Count);
        page.ReadInOrderCommand.Execute(null); page.SaveAsReadingListCommand.Execute(null);
        using var db = Context();
        Assert.Equal(queue!.IssueIds, db.ReadingListItems.Where(i => i.ReadingListId == saved).OrderBy(i => i.SortOrder).Select(i => i.IssueId));
        Assert.Null(db.ReadingLists.Find(saved)!.Source);
    }
    [Fact] public void CreatorPage_ShowsRolesAndRuns_AndButtonsOpenTheCorrectTargets()
    {
        var page = Page(); int continuity = 0, series = 0, storyEvent = 0;
        page = new(() => { }, id => series = id, _ => { }, Context, (_, _, _) => Task.FromResult(EntityProfileOutcome.NotDue), a => { a(); return Task.CompletedTask; });
        page.OpenContinuity = id => continuity = id; page.OpenEvent = id => storyEvent = id;
        page.Load(ComicMetadataEntityKind.Creator, _creator); Pump(page.LoadingTask);
        Assert.Single(page.Roles); Assert.Single(page.CareerRuns); Assert.Equal("1 of 6 read", page.CareerRuns[0].ReadLabel);
        page.Continuities[0].OpenCommand.Execute(null); page.Series[0].OpenCommand.Execute(null); page.Events[0].OpenCommand.Execute(null);
        Assert.Equal(_continuity, continuity); Assert.Equal(_series, series); Assert.True(storyEvent > 0);
    }
    [Fact] public void PublisherPage_UsesItsPublishedSeriesList_AndLargeContinuitySetsUseThePicker()
    {
        int publisher;
        using (var db = Context())
        {
            var p = new Publisher { Name = "Marvel" }; db.Series.Find(_series)!.PublisherEntity = p;
            for (int n = 1; n <= 4; n++) db.ContinuityMemberships.Add(new ContinuityMembership { SeriesId = _series, Continuity = new Continuity { Name = $"Other {n}" } });
            db.SaveChanges(); publisher = p.Id;
        }
        var page = Page(); page.Load(ComicMetadataEntityKind.Character, _character); Pump(page.LoadingTask);
        Assert.True(page.UseContinuityPicker); Assert.False(page.UseContinuitySegments);
        page.Load(ComicMetadataEntityKind.Publisher, publisher); Pump(page.LoadingTask);
        Assert.StartsWith("1 series · 6 issues", page.Counts); Assert.Single(page.Series); Assert.False(page.HasAppearances); Assert.False(page.CanChangeImage);
    }
    [Fact] public void FailedProfileLookup_ShowsANote_AndTheNextLoadClearsIt()
    {
        var outcome = EntityProfileOutcome.Failed;
        var page = new MetadataEntityDetailScreenViewModel(() => { }, _ => { }, _ => { }, Context,
            (_, _, _) => Task.FromResult(outcome), action => { action(); return Task.CompletedTask; });

        page.Load(ComicMetadataEntityKind.Character, _character); Pump(page.LoadingTask); Pump(page.ProfileTask);
        Assert.True(page.HasProfileNote);
        Assert.Contains("tries again", page.ProfileNote);

        outcome = EntityProfileOutcome.NotDue;
        page.Refresh(); Pump(page.LoadingTask); Pump(page.ProfileTask);
        Assert.False(page.HasProfileNote);
    }
    [Fact] public void LateProfileRefresh_CannotReplaceAnotherEntity()
    {
        var pending = new TaskCompletionSource<EntityProfileOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new MetadataEntityDetailScreenViewModel(() => { }, _ => { }, _ => { }, Context,
            (kind, _, _) => kind == ComicMetadataEntityKind.Character ? pending.Task : Task.FromResult(EntityProfileOutcome.NotDue),
            a => { a(); return Task.CompletedTask; });
        page.Load(ComicMetadataEntityKind.Character, _character); Pump(page.LoadingTask); var old = page.ProfileTask;
        page.Load(ComicMetadataEntityKind.Team, _team); Pump(page.LoadingTask);
        pending.SetResult(EntityProfileOutcome.Checked); Pump(old);
        Assert.Equal("X-Men", page.Name); Assert.Equal(ComicMetadataEntityKind.Team, page.Kind);
    }
    [Fact] public void ProfileRefreshUpdatesTheHeader_AndCustomImageCanBeSetAndReset()
    {
        var page = new MetadataEntityDetailScreenViewModel(() => { }, _ => { }, _ => { }, Context,
            (_, id, _) =>
            {
                using var db = Context(); var entity = db.Characters.Find(id)!; entity.Description = "Controls the weather."; entity.Aliases = "Ororo"; db.SaveChanges();
                return Task.FromResult(EntityProfileOutcome.Checked);
            }, action => { action(); return Task.CompletedTask; });
        page.Load(ComicMetadataEntityKind.Character, _character); Pump(page.LoadingTask); Pump(page.ProfileTask);
        Assert.Equal("Controls the weather.", page.Description); Assert.Equal("Also known as Ororo", page.AliasLabel);
        string image = Path.Combine(_folder, "custom.png");
        using (var bitmap = new System.Drawing.Bitmap(16, 20)) bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Png);
        Assert.True(page.SetCustomImage(ComicMetadataEntityKind.Character, _character, image));
        page.Refresh(); Pump(page.LoadingTask); Pump(page.ProfileTask); Assert.True(page.HasCustomImage); Assert.True(File.Exists(page.ImagePath));
        string custom = page.ImagePath!;
        page.ResetImageCommand.Execute(null); TestDispatcher.Drain(); Pump(page.LoadingTask); Pump(page.ProfileTask);
        Assert.False(page.HasCustomImage); Assert.False(File.Exists(custom)); Assert.True(page.ShowInitials);
    }
    [Fact] public void BandAndReaderChips_NavigateByKind_AndTagsKeepSearching()
    {
        var opened = new List<(ComicMetadataEntityKind, string)>(); var searched = new List<string>();
        using var db = Context(); var issue = db.Issues.Include(i => i.Series).First();
        issue.Tags.Add(new IssueTag { Field = IssueTagField.Tags, Value = "Favorite" });
        var band = new DetailBandViewModel(searched.Add) { OpenEntity = (kind, name) => opened.Add((kind, name)) };
        band.LoadIssue(issue);
        foreach (var group in band.Groups) foreach (var pill in group.Chips.Concat(group.Writers ?? new()).Concat(group.Artists ?? new())) pill.SearchCommand.Execute(null);
        Assert.Contains((ComicMetadataEntityKind.Creator, "Claremont"), opened); Assert.Contains((ComicMetadataEntityKind.Character, "Storm"), opened);
        Assert.Contains((ComicMetadataEntityKind.Team, "X-Men"), opened); Assert.Contains((ComicMetadataEntityKind.Location, "Westchester"), opened);
        Assert.Equal(new[] { "Favorite" }, searched);
        var reader = new ReaderInfoPanelViewModel { OpenEntity = (kind, name) => opened.Add((kind, name)) };
        reader.Set(ReaderInfoBuilder.Build(issue, "Uncanny", null), false);
        reader.CharacterLinks[0].SearchCommand.Execute(null); reader.CreditLinks[0].People[0].SearchCommand.Execute(null);
        Assert.Equal((ComicMetadataEntityKind.Creator, "Claremont"), opened[^1]);
    }
    [Fact] public void OverviewStrips_CountOnlyMembers_AndOpenWithContinuitySelected()
    {
        using var db = Context();
        var characters = ContinuityOverviewLoader.LoadEntities(db, new[] { _series }, ComicMetadataEntityKind.Character);
        var entity = Assert.Single(characters); Assert.Equal(6, entity.Count); Assert.Equal(_character, entity.Id);
        Assert.Single(ContinuityOverviewLoader.LoadEntities(db, new[] { _series }, ComicMetadataEntityKind.Team));
        Assert.Empty(ContinuityOverviewLoader.LoadEntities(db, Array.Empty<int>(), ComicMetadataEntityKind.Team));
        (ComicMetadataEntityKind, int, int?)? opened = null;
        var page = new ContinuityPageViewModel(new ContinuityScreenNavigation { OpenEntity = (kind, id, continuity) => opened = (kind, id, continuity), Runner = _ => Task.FromResult<object?>(null) });
        page.Load(_continuity); page.OpenEntityCommand.Execute(entity);
        Assert.Equal((ComicMetadataEntityKind.Character, _character, (int?)_continuity), opened);
    }
    [Theory] [InlineData("default", 1366)] [InlineData("daylight", 1366)] [InlineData("matrix", 1366)]
    [InlineData("default", 1000)] [InlineData("daylight", 1000)] [InlineData("matrix", 1000)]
    public void EntityPage_RendersInThreeThemes_AndReflows(string theme, int width)
    {
        var page = Page(); page.Load(ComicMetadataEntityKind.Character, _character); Pump(page.LoadingTask);
        Theming.FoundationStyleTests.WithFoundation(theme, () =>
        {
            var view = new MetadataEntityDetailScreenView { DataContext = page };
            var window = new Window { Width = width, Height = 768, Content = view }; window.Show(); RunLayout(window);
            // Entity pages redesign §3: the page scroller is the only scroller, and a character's main action is Continue.
            foreach (string panel in new[] { "OverviewPanel", "CareerPanel", "AppearancesPanel", "SeriesPanel", "EventsPanel", "ContinuitiesPanel", "CharactersPanel" })
                Assert.Empty(view.FindControl<Control>(panel)!.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.StartsWith("Continue · ", (string)view.FindControl<Button>("PrimaryAction")!.Content!);
            string? folder = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
            if (folder is not null) { Directory.CreateDirectory(folder); using var bitmap = new RenderTargetBitmap(new PixelSize(width, 768)); bitmap.Render(window); bitmap.Save(Path.Combine(folder, $"entity-{theme}-{width}.png")); }
            window.Close();
        });
    }
    [Fact]
    public void CreatorPage_CareerTabShowsItsRuns_WhenThePageWasAlreadyOnScreen()
    {
        // The app's own dispatch (results applied on the UI thread), since a live view is attached.
        var page = new MetadataEntityDetailScreenViewModel(() => { }, _ => { }, _ => { }, Context, (_, _, _) => Task.FromResult(EntityProfileOutcome.NotDue));
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            // As in the app (screenshot 2026-11-11): the page is already on screen when one entity, then a creator, loads. The old Runs card's
            // inherited-DataContext binding never saw the page there and showed empty; the Career tab must not.
            var view = new MetadataEntityDetailScreenView { DataContext = page };
            var window = new Window { Width = 1366, Height = 768, Content = view }; window.Show(); RunLayout(window);
            page.Load(ComicMetadataEntityKind.Character, _character); Pump(page.LoadingTask); RunLayout(window);
            page.Load(ComicMetadataEntityKind.Creator, _creator); Pump(page.LoadingTask); RunLayout(window);
            Assert.True(page.ShowCareerTab);
            Assert.Single(page.CareerRuns);
            var bars = view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("careerBar")).ToList();
            Assert.Single(bars);
            Assert.True(bars[0].IsEffectivelyVisible && bars[0].Bounds.Width >= 8, $"Run bar is {bars[0].Bounds.Width} px wide; chain: " +
                string.Join(" < ", bars[0].GetVisualAncestors().OfType<Control>().Take(8).Select(c => $"{c.GetType().Name}{(c.Classes.Count > 0 ? "." + string.Join(".", c.Classes) : "")}={c.Bounds.Width:F0}")) +
                $"; start={Paperbunkr.App.Controls.FractionPanel.GetStart(bars[0])} end={Paperbunkr.App.Controls.FractionPanel.GetEnd(bars[0])}");

            string? folder = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
            if (folder is not null) { Directory.CreateDirectory(folder); using var bitmap = new RenderTargetBitmap(new PixelSize(1366, 768)); bitmap.Render(window); bitmap.Save(Path.Combine(folder, "creator-page.png")); }
            window.Close();
        });
    }

    [Theory] [InlineData("default", false)] [InlineData("daylight", false)] [InlineData("matrix", false)]
    [InlineData("default", true)] [InlineData("daylight", true)] [InlineData("matrix", true)]
    public void StructureSectionsAndRecommendationRail_RenderInThreeThemes(string theme, bool creatorRuns)
    {
        using (var db = Context()) { db.Issues.Add(new Issue { SeriesId = _series, Number = "7", Writer = "Other" }); db.SaveChanges(); }
        Series series;
        using (var db = Context()) series = db.Series.Include(s => s.Issues).ThenInclude(i => i.Tags).Single(s => s.Id == _series);
        var tabs = new DetailTabsViewModel(_ => { }, _ => { }, null, Context);
        tabs.InitializeListLayouts(new ListLayoutService(Context)); tabs.LoadSeries(series);
        tabs.ApplyLayoutJson(DetailListLayoutStateJson.Serialize(DetailListLayoutState.ComicDefault with { GroupBy = creatorRuns ? DetailGrouping.CreatorRuns : DetailGrouping.Arcs }));
        tabs.GoIssuesCommand.Execute(null);
        var timer = Stopwatch.StartNew();
        while (!tabs.HasMoreLikeThis && timer.Elapsed < TimeSpan.FromSeconds(15)) { TestDispatcher.Drain(); Thread.Sleep(2); }
        Assert.True(tabs.HasMoreLikeThis);
        Theming.FoundationStyleTests.WithFoundation(theme, () =>
        {
            var view = new DetailTabs { DataContext = tabs };
            var window = new Window { Width = 1366, Height = 768, Content = new ScrollViewer { Content = view }, Background = (Avalonia.Media.IBrush)Application.Current!.FindResource("PbBgBrush")! };
            window.Show(); RunLayout(window);
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, creatorRuns ? "Read this run" : "Read this arc") && b.IsEffectivelyVisible);
            string? folder = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
            void Snap(string name) { if (folder is null) return; Directory.CreateDirectory(folder); using var bitmap = new RenderTargetBitmap(new PixelSize(1366, 768)); bitmap.Render(window); bitmap.Save(Path.Combine(folder, name)); }
            Snap($"structure-{theme}-{(creatorRuns ? "runs" : "arcs")}.png");
            tabs.GoRelatedCommand.Execute(null); RunLayout(window);
            Assert.Contains(tabs.MoreLikeThisRail, r => !string.IsNullOrEmpty(r.SubLabel));
            var rail = view.GetVisualDescendants().OfType<PosterRail>().Single(r => ReferenceEquals(r.ItemsSource, tabs.MoreLikeThisRail));
            rail.BringIntoView(); RunLayout(window);
            Snap($"recommendations-{theme}.png");
            var continuity = new ContinuityPageViewModel(new ContinuityScreenNavigation { Runner = work => Task.FromResult(work()), OpenGcd = () => null });
            continuity.Load(_continuity); Pump(continuity.OverviewTask);
            var overview = new ContinuityOverviewView { DataContext = continuity };
            window.Content = new ScrollViewer { Content = overview }; RunLayout(window);
            Assert.True(continuity.HasKeyCharacters); Assert.True(continuity.HasKeyTeams);
            // Entity pages redesign §8: the shared tile, with Open on the map in its menu instead of a hover button on the art.
            Assert.Contains(continuity.BuildContextMenu(continuity.KeyCharacters[0].Tile)!, e => e.Header == "Open on the map");
            Assert.DoesNotContain(overview.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("entityTileMap"));
            Assert.Contains(overview.GetVisualDescendants().OfType<Paperbunkr.App.Views.EntityPages.EntityTile>(), t => t.IsEffectivelyVisible);
            Assert.Contains(overview.GetVisualDescendants().OfType<Button>(), b => b.Command == continuity.OpenEntityCommand && b.IsEffectivelyVisible);
            overview.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Key characters").BringIntoView(); RunLayout(window);
            Snap($"continuity-entities-{theme}.png"); window.Close();
        });
    }
    [Fact] public void MangaArcs_UseOnlyExplicitFields_AndPreserveTheReadingAndSavedOrder()
    {
        using (var db = Context()) { db.Series.Find(_series)!.ContentType = ContentType.Manga; db.SaveChanges(); }
        var page = new MangaDetailScreenViewModel(() => { }, _ => { }, _ => { }, _ => { });
        page.InitializeListLayouts(new ListLayoutService(Context));
        ReadingQueue? queue = null; int saved = 0;
        page.Tabs.ReadQueue = (_, order) => queue = order; page.Tabs.OpenReadingList = id => saved = id;
        page.LoadSeries(_series);
        page.ApplyLayoutJson(DetailListLayoutStateJson.Serialize(DetailListLayoutState.MangaDefault with { ChapterGroupBy = DetailGrouping.Arcs }));
        var arc = Assert.Single(page.ChapterGroups.Where(g => g.Structure is not null));
        Assert.Equal(3, arc.Structure!.TotalCount);
        Assert.Equal(3, page.ChapterGroups.Single(g => g.Structure is null).Chapters.Count);
        arc.Structure.ReadCommand!.Execute(null); arc.Structure.SaveCommand!.Execute(null);
        Assert.Equal(arc.Chapters.Select(c => c.Id), queue!.IssueIds);
        using var context = Context();
        Assert.Equal(queue.IssueIds, context.ReadingListItems.Where(i => i.ReadingListId == saved).OrderBy(i => i.SortOrder).Select(i => i.IssueId));
        Assert.Null(context.ReadingLists.Find(saved)!.Source);
    }
    [Fact] public void MangaCreatorRuns_AreHiddenForAWholeSeriesCreator_AndOfferedWithOutsideIssues()
    {
        using (var db = Context()) { db.Series.Find(_series)!.ContentType = ContentType.Manga; db.SaveChanges(); }
        var page = new MangaDetailScreenViewModel(() => { }, _ => { }, _ => { }, _ => { });
        page.InitializeListLayouts(new ListLayoutService(Context)); page.LoadSeries(_series);
        Assert.DoesNotContain("Creator runs", page.CreateDetailListOptions(() => { }).GroupChoices);
        using (var db = Context()) { db.Issues.Add(new Issue { SeriesId = _series, Number = "7", Writer = "Other" }); db.SaveChanges(); EntityIndexSync.RebuildAll(db); }
        page.LoadSeries(_series);
        Assert.Contains("Creator runs", page.CreateDetailListOptions(() => { }).GroupChoices);
        page.ApplyLayoutJson(DetailListLayoutStateJson.Serialize(DetailListLayoutState.MangaDefault with { ChapterGroupBy = DetailGrouping.CreatorRuns }));
        Assert.Equal(new[] { "Claremont", "Other issues" }, page.ChapterGroups.Select(g => g.VolumeLabel));
        Assert.Equal(6, page.ChapterGroups[0].Structure!.TotalCount);
        Assert.Equal("Read this run", page.ChapterGroups[0].Structure!.ReadLabel);
    }
    [Fact] public void RealMainWindow_EntityPageSupportsKeyboardRefresh_AndHistoryBack()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            using var styles = AppStyles(); using var tokens = AppResources();
            var previous = InputServiceLocator.Current;
            InputServiceLocator.Current = new InputService(InputActionCatalog.CreateWithCoreActions(), new MemoryKeymapStore());
            try
            {
                var vm = new MainViewModel(); var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 }; window.Show();
                vm.GoMetadataEntityByName(ComicMetadataEntityKind.Character, "storm"); Pump(vm.MetadataEntityDetail.LoadingTask); Pump(vm.MetadataEntityDetail.ProfileTask); RunLayout(window);
                Assert.Equal("metadataEntity", vm.CurrentScreen);
                var view = window.GetVisualDescendants().OfType<MetadataEntityDetailScreenView>().Single(v => v.IsEffectivelyVisible);
                var button = view.FindControl<Button>("PrimaryAction")!;
                Assert.True(button.Focus(NavigationMethod.Tab)); Press(window, Key.F5); TestDispatcher.Drain(); Pump(vm.MetadataEntityDetail.LoadingTask); Pump(vm.MetadataEntityDetail.ProfileTask);
                Assert.Equal("Storm", vm.MetadataEntityDetail.Name);
                // Entity pages redesign §9: the tab strip is driven by Ctrl+PageDown/PageUp with no screen code (Button.tab + TabStrip.Step).
                Assert.True(vm.MetadataEntityDetail.ShowOverviewTab);
                window.KeyPress(Key.PageDown, RawInputModifiers.Control, PhysicalKey.PageDown, null); RunLayout(window); TestDispatcher.Drain();
                Assert.True(vm.MetadataEntityDetail.ShowAppearancesTab, $"Ctrl+PageDown left the tab on {vm.MetadataEntityDetail.SelectedTabId}");
                window.KeyPress(Key.PageUp, RawInputModifiers.Control, PhysicalKey.PageUp, null); RunLayout(window); TestDispatcher.Drain();
                Assert.True(vm.MetadataEntityDetail.ShowOverviewTab);
                vm.NavigateBackCommand.Execute(null); TestDispatcher.Drain(); Assert.NotEqual("metadataEntity", vm.CurrentScreen); window.Close();
            }
            finally { InputServiceLocator.Current = previous; }
        });
    }
}
