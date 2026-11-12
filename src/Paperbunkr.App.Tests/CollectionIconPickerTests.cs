using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The Edit Collection dialog's four icon sections (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class CollectionIconPickerTests : IDisposable
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><rect width=\"10\" height=\"10\" fill=\"#c00\"/></svg>";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_iconpicker_{Guid.NewGuid():N}.db");
    private readonly string _iconsFolder = Path.Combine(Path.GetTempPath(), $"pb-iconpicker-{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _collectionId;

    public CollectionIconPickerTests()
    {
        CollectionPropertiesScreenViewModel.ResetSectionState();
        Directory.CreateDirectory(_iconsFolder);
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var members = new List<int>();
        foreach (var (name, publisher) in new[] { ("A", "Marvel"), ("B", "Marvel"), ("C", "DC Comics"), ("D", "Nobody Press") })
        {
            var series = new Series { Name = name, Publisher = publisher, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
            context.Series.Add(series);
            context.SaveChanges();
            members.Add(series.Id);
        }

        context.Series.Add(new Series { Name = "Outside", Publisher = "Image", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown });
        context.SaveChanges();

        var collection = CollectionService.Create(context, "Picker");
        CollectionService.AddItems(context, collection.Id, seriesIds: members);
        _collectionId = collection.Id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
        try { Directory.Delete(_iconsFolder, recursive: true); } catch (IOException) { }
    }

    private async Task<CollectionPropertiesScreenViewModel> NewVmAsync()
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions)) { IconsFolder = _iconsFolder };
        vm.Load(_collectionId);
        await vm.RefreshMyIconsCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Heroes_AreAllOffered_AndPickingOneStoresItsPrefix()
    {
        var vm = await NewVmAsync();
        Assert.Equal(24, vm.HeroChoices.Count);

        vm.SetGlyphCommand.Execute("hero:cape");

        Assert.Equal("hero:cape", vm.IconGlyph);
        Assert.True(vm.HasGlyph);
        Assert.True(vm.HeroChoices.Single(c => c.Name == "hero:cape").IsSelected);
        Assert.DoesNotContain(vm.GlyphChoices, c => c.IsSelected);
    }

    [Fact]
    public async Task Publishers_OnlyWithALogo_MostCommonMemberPublisherFirst()
    {
        var vm = await NewVmAsync();
        var names = vm.PublisherChoices.Select(c => c.Name).ToList();

        Assert.Equal("publisher:Marvel", names[0]);
        Assert.True(vm.PublisherChoices[0].IsSuggested);
        Assert.Contains("publisher:DC Comics", names);
        Assert.Contains("publisher:Image", names); // in the library, not in the collection
        Assert.DoesNotContain("publisher:Nobody Press", names); // no logo
        Assert.Single(vm.PublisherChoices, c => c.IsSuggested);
        Assert.Equal(names.Skip(1).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), names.Skip(1));
    }

    [Fact]
    public async Task MyIcons_ListTheFolder_ReportSkippedFiles_AndSelectAFile()
    {
        File.WriteAllText(Path.Combine(_iconsFolder, "Batman.svg"), Svg);
        File.WriteAllText(Path.Combine(_iconsFolder, "Broken.svg"), "nope");
        var vm = await NewVmAsync();

        Assert.Equal(new[] { "Batman" }, vm.MyIconChoices.Select(c => c.Label));
        Assert.Equal("Batman image", vm.MyIconChoices[0].AutomationName);
        Assert.NotNull(vm.MyIconsSkippedText);

        vm.SetGlyphCommand.Execute("file:Batman.svg");
        Assert.True(vm.MyIconChoices[0].IsSelected);
    }

    [Fact]
    public async Task AValueThatNoLongerDraws_SelectsNothing_AndCanStillBeCleared()
    {
        var vm = await NewVmAsync();
        vm.IconGlyph = "file:Deleted.png";

        Assert.False(vm.HasGlyph);
        Assert.True(vm.HasIconValue);
        Assert.DoesNotContain(vm.GlyphChoices.Concat(vm.HeroChoices).Concat(vm.PublisherChoices).Concat(vm.MyIconChoices), c => c.IsSelected);

        vm.ClearGlyphCommand.Execute(null);
        Assert.False(vm.HasIconValue);
    }

    [Fact]
    public async Task Refresh_PicksUpANewFile()
    {
        var vm = await NewVmAsync();
        Assert.Empty(vm.MyIconChoices);

        File.WriteAllText(Path.Combine(_iconsFolder, "Wolverine.svg"), Svg);
        await vm.RefreshMyIconsCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Wolverine" }, vm.MyIconChoices.Select(c => c.Label));
    }

    [Fact]
    public async Task OpenFolder_CreatesItAndHandsItToTheShell()
    {
        var vm = await NewVmAsync();
        string? opened = null;
        vm.OpenFolderInShell = folder => opened = folder;
        Directory.Delete(_iconsFolder, recursive: true);

        vm.OpenMyIconsFolderCommand.Execute(null);

        Assert.Equal(_iconsFolder, opened);
        Assert.True(Directory.Exists(_iconsFolder));
    }
}
