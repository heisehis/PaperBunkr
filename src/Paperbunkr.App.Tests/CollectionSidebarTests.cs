using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library sidebar's Collections section after docs/superpowers/specs/2026-11-06-collection-identity-design.md: glyph, pin, divider, progress, and
/// reordering that stays inside the pinned / unpinned group. Same temp-database pattern as <see cref="LibraryScreenViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CollectionSidebarTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public CollectionSidebarTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_sidebar_{Guid.NewGuid():N}.db");
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

    private static LibraryScreenViewModel NewVm() =>
        new(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

    private static int AddCollection(string name, string? glyph = null, bool pinned = false)
    {
        using var context = PaperbunkrDb.CreateContext();
        var collection = CollectionService.Create(context, name);
        CollectionService.SetIdentity(context, collection.Id, glyph, CollectionCoverStyle.Auto, null, pinned);
        return collection.Id;
    }

    private static int AddSeriesWithIssues(string name, params int?[] lastPageRead)
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

    [Fact]
    public void Summaries_CarryGlyphPinAndTheDividerOnTheFirstUnpinnedRow()
    {
        AddCollection("Plain");
        AddCollection("Dressed", glyph: "Rocket", pinned: true);
        AddCollection("Other", pinned: false);

        var vm = NewVm();

        Assert.Equal(new[] { "Dressed", "Plain", "Other" }, vm.Collections.Select(c => c.Name));
        var dressed = vm.Collections[0];
        Assert.True(dressed.IsPinned);
        Assert.True(dressed.HasGlyph);
        Assert.False(dressed.ShowDot);
        Assert.Equal("Rocket", dressed.IconGlyph);
        Assert.False(dressed.IsFirstUnpinned);
        Assert.True(vm.Collections[1].IsFirstUnpinned);
        Assert.False(vm.Collections[2].IsFirstUnpinned);
    }

    [Fact]
    public void NoPinnedCollections_MeansNoDivider()
    {
        AddCollection("A");
        AddCollection("B");

        var vm = NewVm();

        Assert.All(vm.Collections, c => Assert.False(c.IsFirstUnpinned));
    }

    [Fact]
    public void UnknownGlyphName_FallsBackToTheDot()
    {
        AddCollection("Odd", glyph: "NotARealSymbol");

        var vm = NewVm();

        var row = Assert.Single(vm.Collections);
        Assert.False(row.HasGlyph);
        Assert.True(row.ShowDot);
    }

    [Fact]
    public void EveryCuratedGlyphIsARealSymbol()
    {
        foreach (var option in CollectionGlyphs.All)
        {
            Assert.True(CollectionGlyphs.TryParse(option.Name) is not null, $"'{option.Name}' is not a FluentIcons Symbol");
        }

        Assert.Equal(CollectionGlyphs.All.Count, CollectionGlyphs.All.Select(g => g.Name).Distinct().Count());
    }

    [Fact]
    public void TogglePin_MovesTheRowToTheTopGroup()
    {
        AddCollection("A");
        AddCollection("B");
        var vm = NewVm();

        vm.TogglePinCollectionCommand.Execute(vm.Collections.Single(c => c.Name == "B"));

        Assert.Equal(new[] { "B", "A" }, vm.Collections.Select(c => c.Name));
        Assert.True(vm.Collections[0].IsPinned);
    }

    [Fact]
    public void MoveUpAndDown_NeverCrossTheGroupBoundary()
    {
        AddCollection("P", pinned: true);
        AddCollection("U1");
        AddCollection("U2");
        var vm = NewVm();

        vm.MoveCollectionUpCommand.Execute(vm.Collections.Single(c => c.Name == "U1")); // would step over P
        Assert.Equal(new[] { "P", "U1", "U2" }, vm.Collections.Select(c => c.Name));

        vm.MoveCollectionDownCommand.Execute(vm.Collections.Single(c => c.Name == "P")); // would step over U1
        Assert.Equal(new[] { "P", "U1", "U2" }, vm.Collections.Select(c => c.Name));

        vm.MoveCollectionDownCommand.Execute(vm.Collections.Single(c => c.Name == "U1"));
        Assert.Equal(new[] { "P", "U2", "U1" }, vm.Collections.Select(c => c.Name));
    }

    [Fact]
    public void DragTo_ReordersInsideAGroup_AndIgnoresTheOtherGroup()
    {
        AddCollection("P1", pinned: true);
        AddCollection("P2", pinned: true);
        AddCollection("U1");
        AddCollection("U2");
        AddCollection("U3");
        var vm = NewVm();

        vm.MoveCollectionTo(vm.Collections.Single(c => c.Name == "U3"), vm.Collections.Single(c => c.Name == "U1"));
        Assert.Equal(new[] { "P1", "P2", "U3", "U1", "U2" }, vm.Collections.Select(c => c.Name));

        vm.MoveCollectionTo(vm.Collections.Single(c => c.Name == "U1"), vm.Collections.Single(c => c.Name == "P1"));
        Assert.Equal(new[] { "P1", "P2", "U3", "U1", "U2" }, vm.Collections.Select(c => c.Name));
    }

    [Fact]
    public void Progress_IsOffUnlessTheSettingIsOn()
    {
        int series = AddSeriesWithIssues("S", 3, null);
        int id = AddCollection("Reading");
        using (var context = PaperbunkrDb.CreateContext())
        {
            CollectionService.AddItems(context, id, seriesIds: new[] { series });
        }

        var off = NewVm();
        Assert.False(off.Collections.Single().HasProgress);

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().ShowCollectionProgress = true;
            context.SaveChanges();
        }

        var on = NewVm();
        var row = on.Collections.Single();
        Assert.True(row.HasProgress);
        Assert.Equal(0.5, row.ProgressValue, 6);
        Assert.Contains("50%", row.ProgressText);
    }

    [Fact]
    public void DensityAndShapeSettings_ReachTheViewModel()
    {
        AddCollection("A");
        using (var context = PaperbunkrDb.CreateContext())
        {
            var settings = context.GetOrCreateAppSettings();
            settings.CollectionRowDensity = CollectionRowDensity.Compact;
            settings.CollectionDotShape = CollectionDotShape.Squircle;
            context.SaveChanges();
        }

        var vm = NewVm();

        Assert.True(vm.IsCompactCollections);
        Assert.True(vm.IsSquircleCollectionDots);
    }
}
