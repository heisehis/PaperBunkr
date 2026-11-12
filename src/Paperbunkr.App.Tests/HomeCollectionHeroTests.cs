using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The Home Collections shelf's Continue hero (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomeCollectionHeroTests : IDisposable
{
    private readonly string? _original = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_homehero_{Guid.NewGuid():N}.db");
    private static readonly DateTime Now = DateTime.UtcNow;

    public HomeCollectionHeroTests()
    {
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private static int AddCollection(string name, string seriesName, params (string Number, int? Page, DateTime? Opened)[] issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = seriesName, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        foreach (var (number, page, opened) in issues)
        {
            series.Issues.Add(new Issue { Number = number, FilePath = $"C:\\x\\{seriesName}{number}.cbz", LastPageRead = page, OpenedTime = opened });
        }

        context.Series.Add(series);
        context.SaveChanges();
        var collection = CollectionService.Create(context, name);
        CollectionService.AddItems(context, collection.Id, seriesIds: new[] { series.Id });
        return collection.Id;
    }

    private static HomeScreenViewModel LoadHome(Action<int>? readIssue = null)
    {
        var vm = new HomeScreenViewModel(_ => { }, readIssue ?? (_ => { }), _ => { }, (_, _) => { }, (_, _) => { });
        vm.LoadFromDatabase();
        return vm;
    }

    [Fact]
    public void NoReadingActivity_MeansNoHero_AndTheShelfIsUnchanged()
    {
        AddCollection("A", "SA", ("1", null, null));
        AddCollection("B", "SB", ("1", null, null));

        var vm = LoadHome();

        Assert.Null(vm.CollectionHero);
        Assert.Equal(2, vm.ShelfCollections.Count);
        Assert.Equal(2, vm.Collections.Count);
    }

    [Fact]
    public void TheRecentlyReadCollection_BecomesTheHeroAndLeavesTheShelf()
    {
        AddCollection("Old", "SOld", ("1", 3, Now.AddDays(-20)), ("2", null, null));
        int recent = AddCollection("Recent", "SRecent", ("1", 4, Now.AddHours(-1)), ("2", null, null));
        AddCollection("Untouched", "SU", ("1", null, null));

        var vm = LoadHome();

        var hero = vm.CollectionHero!;
        Assert.Equal(recent, hero.Id);
        Assert.True(hero.IsHero);
        Assert.True(hero.HasContinue);
        Assert.Equal("SRecent #2", hero.ContinueLabel);
        Assert.Equal(hero, vm.Collections.First()); // hero first
        Assert.DoesNotContain(vm.ShelfCollections, c => c.IsHero);
        Assert.Equal(2, vm.ShelfCollections.Count);
    }

    [Fact]
    public void ContinueOpensTheNextUnreadIssueInTheReader()
    {
        AddCollection("Recent", "SRecent", ("1", 4, Now.AddHours(-1)), ("2", null, null));
        int? opened = null;
        var vm = LoadHome(id => opened = id);

        vm.ContinueCollectionCommand.Execute(vm.CollectionHero);

        Assert.NotNull(opened);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("2", context.Issues.Single(i => i.Id == opened).Number);
    }

    [Fact]
    public void AFullyReadHero_HasNoContinueChip()
    {
        AddCollection("Done", "SD", ("1", 9, Now.AddHours(-2)));
        int? opened = null;
        var vm = LoadHome(id => opened = id);

        var hero = vm.CollectionHero!;
        Assert.False(hero.HasContinue);
        vm.ContinueCollectionCommand.Execute(hero);
        Assert.Null(opened);
    }
}
