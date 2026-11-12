using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The Library's collection banner, empty state and membership badges (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CollectionBannerTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public CollectionBannerTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_banner_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
        CollectionBadgeStore.Clear();
        CollectionBadgeStore.Suppressed = false;
    }

    public void Dispose()
    {
        CollectionBadgeStore.Clear();
        CollectionBadgeStore.Suppressed = false;
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private static LibraryScreenViewModel NewVm() =>
        new(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

    private static int AddSeries(string name, params int?[] lastPageRead)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        int n = 1;
        foreach (int? page in lastPageRead)
        {
            series.Issues.Add(new Issue { Number = (n++).ToString(), FilePath = $"C:\\x\\{name}{n}.cbz", LastPageRead = page });
        }

        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static int AddCollection(string name, string? description = null, params int[] seriesIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        var collection = CollectionService.Create(context, name);
        CollectionService.SetAppearance(context, collection.Id, description, "#C97C9E", null, true);
        if (seriesIds.Length > 0)
        {
            CollectionService.AddItems(context, collection.Id, seriesIds: seriesIds);
        }

        return collection.Id;
    }

    [Fact]
    public void NoActiveCollection_MeansNoBanner()
    {
        AddCollection("A");
        var vm = NewVm();

        Assert.Null(vm.ActiveCollectionBanner);
        Assert.False(vm.IsBannerVisible);
        Assert.False(vm.IsActiveCollectionEmpty);
    }

    [Fact]
    public void SelectingACollection_ShowsABannerWithStatsAndReadPercent_EvenWithTheSidebarRingOff()
    {
        int series = AddSeries("S", 3, null);
        AddCollection("Mine", "Some **bold** text", series);
        var vm = NewVm();

        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        var banner = vm.ActiveCollectionBanner!;
        Assert.Equal("Mine", banner.Name);
        Assert.Equal(new[] { "1 series" }, banner.StatChips);
        Assert.Equal("50% read", banner.ReadText);
        Assert.True(banner.HasDescription);
        Assert.NotEmpty(banner.DescriptionBlocks);
        Assert.False(vm.Collections.Single().HasProgress); // the sidebar ring setting stays off
        Assert.False(vm.IsActiveCollectionEmpty);
    }

    [Fact]
    public void AnEmptyCollection_FlagsTheEmptyState_ButAFilledOneDoesNot()
    {
        AddCollection("Empty");
        int series = AddSeries("S", 1);
        AddCollection("Filled", null, series);
        var vm = NewVm();

        vm.SelectCollectionCommand.Execute(vm.Collections.Single(c => c.Name == "Empty"));
        Assert.True(vm.IsActiveCollectionEmpty);
        Assert.Empty(vm.ActiveCollectionBanner!.StatChips);
        Assert.Null(vm.ActiveCollectionBanner.ReadText);

        vm.SelectCollectionCommand.Execute(vm.Collections.Single(c => c.Name == "Filled"));
        Assert.False(vm.IsActiveCollectionEmpty);
    }

    [Fact]
    public void LeavingTheCollection_ClearsTheBanner()
    {
        AddCollection("A");
        var vm = NewVm();
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());
        Assert.True(vm.IsBannerVisible);

        vm.SelectAllSeriesCommand.Execute(null);

        Assert.False(vm.IsBannerVisible);
        Assert.False(vm.IsActiveCollectionEmpty);
    }

    [Fact]
    public void PinFromTheBanner_UpdatesIt()
    {
        AddCollection("A");
        var vm = NewVm();
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());
        Assert.False(vm.ActiveCollectionBanner!.IsPinned);

        vm.TogglePinActiveCollectionCommand.Execute(null);

        Assert.True(vm.ActiveCollectionBanner!.IsPinned);
        Assert.True(vm.Collections.Single().IsPinned);
    }

    [Fact]
    public void EditFromTheBanner_OpensTheDialogForTheActiveCollection()
    {
        int id = AddCollection("A");
        int opened = 0;
        var vm = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { }, openCollectionProperties: i => opened = i);
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        vm.EditActiveCollectionCommand.Execute(null);

        Assert.Equal(id, opened);
    }

    [Fact]
    public void DescriptionToggle_FlipsTheCap()
    {
        var vm = NewVm();
        Assert.False(vm.IsBannerDescriptionExpanded);
        vm.ToggleBannerDescriptionCommand.Execute(null);
        Assert.True(vm.IsBannerDescriptionExpanded);
    }

    [Fact]
    public void Badges_AreOffByDefault_OnWhenEnabled_AndHiddenInsideACollection()
    {
        int series = AddSeries("S", 1);
        AddCollection("Events", null, series);

        var off = NewVm();
        Assert.Empty(CollectionBadgeStore.ForSeries(series));

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().ShowCollectionBadges = true;
            context.SaveChanges();
        }

        var on = NewVm();
        Assert.Equal(new[] { "Events" }, CollectionBadgeStore.ForSeries(series).Select(b => b.Name));

        on.SelectCollectionCommand.Execute(on.Collections.Single());
        Assert.Empty(CollectionBadgeStore.ForSeries(series)); // suppressed inside a collection

        on.SelectAllSeriesCommand.Execute(null);
        Assert.Equal(new[] { "Events" }, CollectionBadgeStore.ForSeries(series).Select(b => b.Name));
    }

    [Fact]
    public void Badges_ToggleFromTheLibraryViewMenu_PersistAndRefill()
    {
        int series = AddSeries("S", 1);
        AddCollection("Events", null, series);

        var vm = NewVm();
        Assert.False(vm.ShowCollectionBadges);
        Assert.Empty(CollectionBadgeStore.ForSeries(series));

        vm.ShowCollectionBadges = true;
        Assert.Equal(new[] { "Events" }, CollectionBadgeStore.ForSeries(series).Select(b => b.Name));
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.True(context.GetOrCreateAppSettings().ShowCollectionBadges);
        }

        Assert.True(NewVm().ShowCollectionBadges);

        vm.ShowCollectionBadges = false;
        Assert.Empty(CollectionBadgeStore.ForSeries(series));
    }

    [Fact]
    public void Banner_GoesSlimPastTheThreshold_AndFullOnlyBackAtTheTop()
    {
        int series = AddSeries("S", 1);
        AddCollection("Events", null, series);
        var vm = NewVm();
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());
        Assert.False(vm.IsBannerSlim);

        vm.ReportListScroll(10);
        Assert.False(vm.IsBannerSlim); // not yet past the threshold

        vm.ReportListScroll(LibraryScreenViewModel.BannerSlimScrollThreshold + 1);
        Assert.True(vm.IsBannerSlim);

        vm.ReportListScroll(10);
        Assert.True(vm.IsBannerSlim); // stays slim until the very top

        vm.ReportListScroll(0);
        Assert.False(vm.IsBannerSlim);
    }

    [Fact]
    public void Banner_PinnedSlim_StaysSlimAtTheTop_AndIsRemembered()
    {
        int series = AddSeries("S", 1);
        AddCollection("Events", null, series);
        var vm = NewVm();
        vm.SelectCollectionCommand.Execute(vm.Collections.Single());

        vm.ToggleBannerSlimCommand.Execute(null);
        vm.ReportListScroll(0);
        Assert.True(vm.IsBannerSlim);
        using (var context = PaperbunkrDb.CreateContext())
        {
            Assert.True(context.GetOrCreateAppSettings().CollectionBannerSlim);
        }

        var again = NewVm();
        Assert.True(again.IsBannerSlimPinned);

        again.ToggleBannerSlimCommand.Execute(null);
        Assert.False(again.IsBannerSlim);
    }

    [Fact]
    public void Banner_AnotherCollection_OpensFull()
    {
        int a = AddSeries("A", 1);
        int b = AddSeries("B", 1);
        AddCollection("First", null, a);
        AddCollection("Second", null, b);
        var vm = NewVm();
        vm.SelectCollectionCommand.Execute(vm.Collections.First(c => c.Name == "First"));
        vm.ReportListScroll(400);
        Assert.True(vm.IsBannerSlim);

        vm.SelectCollectionCommand.Execute(vm.Collections.First(c => c.Name == "Second"));

        Assert.False(vm.IsBannerSlim);
    }
}
