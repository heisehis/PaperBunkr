using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Views;
using static Paperbunkr.App.Tests.FocusTestHarness;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.ContinuityScreen;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.ViewModels.EntityPages;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests.EntityPages;

/// <summary>Entity pages redesign (docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md): view model behaviour on a seeded library.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EntityPageViewModelTests : IDisposable
{
    private readonly string? _oldPath = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _oldImages = EntityImageCache.CacheDirectory;
    private readonly string _oldEvents = StoryEventCoverPaths.CacheDirectory;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pb_entitypage_{Guid.NewGuid():N}");
    private readonly CoverCacheTestRedirect _covers = new();
    protected readonly int Storm, XMen, Westchester, Claremont, Earth616, Ultimate;

    protected PaperbunkrDbContext Context() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>()
        .UseSqlite($"Data Source={Path.Combine(_folder, "library.db")}").Options);

    public EntityPageViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        PaperbunkrDbContext.DatabasePathOverride = Path.Combine(_folder, "library.db");
        EntityImageCache.CacheDirectory = Path.Combine(_folder, "entity-images");
        StoryEventCoverPaths.CacheDirectory = Path.Combine(_folder, "event-covers");
        using var context = Context(); context.Database.EnsureCreated();
        var uncanny = new Series { Name = "Uncanny X-Men", ContentType = ContentType.Comic };
        var ultimate = new Series { Name = "Ultimate X-Men", ContentType = ContentType.Comic };
        for (int n = 1; n <= 8; n++)
        {
            context.Issues.Add(new Issue { Series = n <= 6 ? uncanny : ultimate, Number = n.ToString(), Year = 1980 + n, PageCount = 20, LastPageRead = n == 1 ? 19 : 0,
                Characters = n % 2 == 0 ? "Storm, Wolverine" : "Storm", Teams = "X-Men", Locations = n == 8 ? "Westchester" : null,
                Writer = n <= 6 ? "Claremont" : "Millar", Penciller = "Cockrum", FilePath = $"C:\\fixtures\\issue{n}.cbz" });
        }
        var first = new Continuity { Name = "Earth-616" }; var second = new Continuity { Name = "Ultimate" };
        context.ContinuityMemberships.AddRange(new ContinuityMembership { Series = uncanny, Continuity = first }, new ContinuityMembership { Series = ultimate, Continuity = second });
        context.SaveChanges(); EntityIndexSync.RebuildAll(context);
        context.EventMemberships.Add(new EventMembership { StoryEvent = new StoryEvent { Name = "Phoenix Saga" }, IssueId = uncanny.Issues.First().Id });
        context.SaveChanges();
        Storm = context.Characters.Single(c => c.Name == "Storm").Id; XMen = context.Teams.Single().Id; Westchester = context.Locations.Single().Id;
        Claremont = context.Creators.Single(c => c.Name == "Claremont").Id; Earth616 = first.Id; Ultimate = second.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _oldPath; EntityImageCache.CacheDirectory = _oldImages; StoryEventCoverPaths.CacheDirectory = _oldEvents; _covers.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    protected MetadataEntityDetailScreenViewModel Page() => new(() => { }, _ => { }, _ => { }, Context,
        (_, _, _) => Task.FromResult(EntityProfileOutcome.NotDue), action => { action(); return Task.CompletedTask; });

    protected static void Load(MetadataEntityDetailScreenViewModel page, ComicMetadataEntityKind kind, int id)
    {
        page.Load(kind, id); SeriesStructureUiTests.Pump(page.LoadingTask);
    }

    [Fact]
    public void ALocationAfterACreator_HasNoRunsCard()
    {
        var page = Page();
        Load(page, ComicMetadataEntityKind.Creator, Claremont);
        Assert.True(page.HasCareerRuns);

        Load(page, ComicMetadataEntityKind.Location, Westchester);

        Assert.False(page.HasCareerRuns);
        Assert.Empty(page.CareerRuns);
        Assert.False(page.ShowCareerTab);
    }

    [Fact]
    public void CountsAndDetails_ArePluralisedAndHaveNoEmptyParts()
    {
        var page = Page();
        Load(page, ComicMetadataEntityKind.Location, Westchester);
        Assert.Equal("1 appearance · 1 series · 0 read", page.Counts);

        Load(page, ComicMetadataEntityKind.Character, Storm);
        Assert.DoesNotContain(page.Events, e => e.Detail.Contains("·  ·") || e.Detail.Contains("· ·"));
        Assert.Contains(page.Continuities, c => c.Detail == "2 appearances");
    }

    [Fact]
    public void CreatorRoles_AreReadable()
    {
        var page = Page();
        Load(page, ComicMetadataEntityKind.Creator, Claremont);
        Assert.Equal(new[] { "Writer" }, page.Roles.Select(r => r.Role));
    }
    [Fact]
    public void EachLoad_OpensOnTheLeadTab_AndHidesEmptyTabs()
    {
        var page = Page();
        Load(page, ComicMetadataEntityKind.Character, Storm);
        Assert.True(page.ShowOverviewTab);
        Assert.Equal("Appearances 8", page.AppearancesTab.Text);
        page.SelectTab(EntityTabIds.Series);
        Assert.True(page.ShowSeriesTab);

        Load(page, ComicMetadataEntityKind.Creator, Claremont);
        Assert.True(page.ShowCareerTab);
        Assert.False(page.OverviewTab.IsVisible);

        Load(page, ComicMetadataEntityKind.Location, Westchester);
        Assert.True(page.ShowOverviewTab);
        Assert.False(page.EventsTab.IsVisible);
        page.SelectTab(EntityTabIds.Events);
        Assert.True(page.ShowOverviewTab);
    }

    [Fact]
    public void Continue_StartsAtTheFirstUnreadIssueInTheFilter()
    {
        var page = Page(); int read = 0;
        page.ReadQueue = (id, _) => read = id;
        Load(page, ComicMetadataEntityKind.Character, Storm);
        Assert.Equal("Continue · Uncanny X-Men #2", page.PrimaryLabel);

        page.SelectContinuity(Ultimate); SeriesStructureUiTests.Pump(page.LoadingTask);
        Assert.Equal("Continue · Ultimate X-Men #7", page.PrimaryLabel);
        page.PrimaryCommand.Execute(null);
        using var context = Context();
        Assert.Equal("7", context.Issues.Find(read)!.Number);
    }

    [Fact]
    public void Appearances_SortAndFilterToUnread()
    {
        var page = Page();
        Load(page, ComicMetadataEntityKind.Character, Storm);
        Assert.Equal(8, page.VisibleAppearances.Count);
        Assert.Equal("Uncanny X-Men #1", page.VisibleAppearances[0].Title);

        page.UnreadOnly = true;
        Assert.Equal(7, page.VisibleAppearances.Count);
        page.SortAppearancesCommand.Execute(AppearanceSort.Newest);
        Assert.Equal("Ultimate X-Men #8", page.VisibleAppearances[0].Title);
        Assert.True(page.SortsByNewest);
        page.SortAppearancesCommand.Execute(AppearanceSort.Series);
        Assert.Equal("Ultimate X-Men #7", page.VisibleAppearances[0].Title);
    }

    [Fact]
    public void RoleChips_NarrowTheCreatorPage_AndKeepTheirCounts()
    {
        using (var context = Context())
        {
            var issue = new Issue { Series = new Series { Name = "Solo", ContentType = ContentType.Comic }, Number = "1", Year = 1990, Writer = "Someone", Penciller = "Claremont", PageCount = 20 };
            context.Issues.Add(issue); context.SaveChanges(); EntityIndexSync.SyncIssue(context, issue.Id);
        }
        var page = Page();
        Load(page, ComicMetadataEntityKind.Creator, Claremont);
        Assert.True(page.ShowRoleChips);
        Assert.Equal(new[] { "All", "Writer 6", "Penciller 1" }, page.RoleChips.Select(c => c.Label));

        page.RoleChips.Single(c => c.Value == "Penciller").ChooseCommand.Execute(null); SeriesStructureUiTests.Pump(page.LoadingTask);

        Assert.Equal("Solo #1", Assert.Single(page.VisibleAppearances).Title);
        Assert.Equal(new[] { "All", "Writer 6", "Penciller 1" }, page.RoleChips.Select(c => c.Label));
        Assert.True(page.RoleChips.Single(c => c.Value == "Penciller").IsSelected);
    }

    [Fact]
    public void RelatedTiles_RankByTogether_OpenWithTheContinuity_AndHaveAMenu()
    {
        var page = Page(); (ComicMetadataEntityKind, int, int?)? opened = null; string? library = null;
        page.OpenEntity = (kind, id, continuity) => opened = (kind, id, continuity);
        page.ShowInLibrary = name => library = name;
        Load(page, ComicMetadataEntityKind.Character, Storm);

        Assert.Equal(new[] { ("X-Men", "8 together"), ("Wolverine", "4 together") }, page.Related.Select(t => (t.Name, t.Caption)));
        page.SelectContinuity(Earth616); SeriesStructureUiTests.Pump(page.LoadingTask);
        var wolverine = page.Related.Single(t => t.Name == "Wolverine");
        page.OpenTileCommand.Execute(wolverine);
        Assert.Equal((ComicMetadataEntityKind.Character, wolverine.Id, (int?)Earth616), opened);

        var menu = page.BuildContextMenu(wolverine)!;
        Assert.Equal(new[] { "Open Wolverine", "Open on the map", "Show in Library" }, menu.Select(e => e.Header));
        menu[2].Command!.Execute(null);
        Assert.Equal("Wolverine", library);
        Assert.Contains(page.BuildContextMenu(page.Events[0])!, e => e.Header == "Open on the map");
        Assert.Null(page.BuildContextMenu("something else"));
    }

    [Fact]
    public void ALocationLooksLikeAPlace()
    {
        var page = Page();
        Load(page, ComicMetadataEntityKind.Location, Westchester);
        Assert.True(page.IsWideImage);
        Assert.Equal("Read in order", page.PrimaryLabel);
        Assert.Equal("Who's been here", page.RelatedTitle);
        Assert.Contains(page.Related, t => t.Name == "Storm");
        Assert.False(page.ShowMapInManage);
    }
    [Theory]
    [InlineData("default")]
    [InlineData("daylight")]
    [InlineData("matrix")]
    public void EveryKind_RendersAt1366_WithItsTabsAndOneScroller(string theme)
    {
        int publisher;
        using (var context = Context())
        {
            var marvel = new Publisher { Name = "Marvel" };
            foreach (var series in context.Series) { series.PublisherEntity = marvel; series.Imprint = series.Name.StartsWith("Ultimate") ? "Ultimate" : "Marvel"; }
            context.SaveChanges(); publisher = marvel.Id;
        }
        var page = new MetadataEntityDetailScreenViewModel(() => { }, _ => { }, _ => { }, Context, (_, _, _) => Task.FromResult(EntityProfileOutcome.NotDue));
        var kinds = new (ComicMetadataEntityKind Kind, int Id, string[] Tabs)[]
        {
            (ComicMetadataEntityKind.Character, Storm, new[] { "Overview", "Appearances 8", "Series 2", "Events 1" }),
            (ComicMetadataEntityKind.Team, XMen, new[] { "Overview", "Appearances 8", "Series 2", "Events 1" }),
            (ComicMetadataEntityKind.Location, Westchester, new[] { "Overview", "Appearances 1", "Series 1" }),
            (ComicMetadataEntityKind.Creator, Claremont, new[] { "Career", "Credits 6", "Series 1" }),
            (ComicMetadataEntityKind.Publisher, publisher, new[] { "Series 2", "Continuities 2", "Characters 2" }),
        };
        Theming.FoundationStyleTests.WithFoundation(theme, () =>
        {
            var view = new MetadataEntityDetailScreenView { DataContext = page };
            var window = new Window { Width = 1366, Height = 768, Content = view }; window.Show(); RunLayout(window);
            foreach (var (kind, id, tabs) in kinds)
            {
                page.Load(kind, id); SeriesStructureUiTests.Pump(page.LoadingTask); RunLayout(window);
                var strip = view.FindControl<StackPanel>("TabStripPanel")!;
                Assert.Equal(tabs, strip.Children.OfType<Button>().Where(b => b.IsVisible).Select(b => (string)b.Content!));
                Assert.True(view.FindControl<Button>("PrimaryAction")!.IsEffectivelyVisible);
                foreach (string panel in new[] { "OverviewPanel", "CareerPanel", "AppearancesPanel", "SeriesPanel", "EventsPanel", "ContinuitiesPanel", "CharactersPanel" })
                    Assert.Empty(view.FindControl<Control>(panel)!.GetVisualDescendants().OfType<ScrollViewer>());
                string? folder = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
                if (folder is null) continue;
                Directory.CreateDirectory(folder);
                using var bitmap = new RenderTargetBitmap(new PixelSize(1366, 768)); bitmap.Render(window);
                bitmap.Save(Path.Combine(folder, $"entity-{kind.ToString().ToLowerInvariant()}-{theme}.png"));
                if (kind == ComicMetadataEntityKind.Character)
                {
                    // The ⋯ Manage button is a plain menu; the 460 px image picker opens in its own flyout (it overflowed the menu, 2026-11-12 screenshot).
                    var manage = view.FindControl<Button>("ManageButton")!;
                    var menu = Assert.IsType<MenuFlyout>(manage.Flyout);
                    Assert.Contains(menu.Items.OfType<MenuItem>(), m => (string?)m.Header == "Change image…");
                    Assert.DoesNotContain(menu.Items, m => m is Paperbunkr.App.Views.CoverPickerView);
                    var picker = Assert.IsType<Flyout>(Avalonia.Controls.Primitives.FlyoutBase.GetAttachedFlyout(manage));
                    Assert.IsType<Paperbunkr.App.Views.CoverPickerView>(picker.Content);
                    Avalonia.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(manage); RunLayout(window);
                    Assert.True(picker.IsOpen);
                    picker.Hide(); RunLayout(window);
                    page.SelectTab(EntityTabIds.Appearances); RunLayout(window);
                    using var grid = new RenderTargetBitmap(new PixelSize(1366, 768)); grid.Render(window);
                    grid.Save(Path.Combine(folder, $"entity-character-appearances-{theme}.png"));
                }
            }
            window.Close();
        });
    }
}
