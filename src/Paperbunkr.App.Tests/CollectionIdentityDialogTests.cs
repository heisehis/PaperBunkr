using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Edit Collection dialog identity fields (docs/superpowers/specs/2026-11-06-collection-identity-design.md): glyph, pin, cover style, cover member.</summary>
public class CollectionIdentityDialogTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collectionidentitydialog_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _collectionId;

    public CollectionIdentityDialogTests()
    {
        CollectionPropertiesScreenViewModel.ResetSectionState();
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var ids = new List<int>();
        foreach (string name in new[] { "Alpha", "Bravo", "Charlie" })
        {
            var series = new Series { Name = name, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
            context.Series.Add(series);
            context.SaveChanges();
            ids.Add(series.Id);
        }

        var collection = CollectionService.Create(context, "Dialog");
        CollectionService.AddItems(context, collection.Id, seriesIds: ids);
        _collectionId = collection.Id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private CollectionPropertiesScreenViewModel NewVm()
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        return vm;
    }

    private Collection Saved()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Collections.AsNoTracking().Single(c => c.Id == _collectionId);
    }

    [Fact]
    public void Glyph_SelectingMarksItAndPickingAgainClearsIt()
    {
        var vm = NewVm();
        Assert.False(vm.HasGlyph);

        vm.SetGlyphCommand.Execute("Rocket");
        Assert.True(vm.HasGlyph);
        Assert.Equal("Rocket", vm.IconGlyph);
        Assert.True(vm.GlyphChoices.Single(g => g.Name == "Rocket").IsSelected);
        Assert.Single(vm.GlyphChoices, g => g.IsSelected);

        vm.SetGlyphCommand.Execute("Rocket");
        Assert.False(vm.HasGlyph);
        Assert.DoesNotContain(vm.GlyphChoices, g => g.IsSelected);
    }

    [Fact]
    public void PinStyleAndGlyph_AreBufferedUntilSave()
    {
        var vm = NewVm();
        vm.SetGlyphCommand.Execute("Star");
        vm.IsPinned = true;
        vm.SetCoverStyleCommand.Execute("Fan");
        vm.CancelCommand.Execute(null);

        var untouched = Saved();
        Assert.Null(untouched.IconGlyph);
        Assert.False(untouched.IsPinned);
        Assert.Equal(CollectionCoverStyle.Auto, untouched.CoverStyle);

        vm.SaveCommand.Execute(null);
        var saved = Saved();
        Assert.Equal("Star", saved.IconGlyph);
        Assert.True(saved.IsPinned);
        Assert.Equal(CollectionCoverStyle.Fan, saved.CoverStyle);

        var reloaded = NewVm();
        Assert.Equal("Star", reloaded.IconGlyph);
        Assert.True(reloaded.IsPinned);
        Assert.Equal(CollectionCoverStyle.Fan, reloaded.CoverStyle);
        Assert.True(reloaded.CoverStyleChoices.Single(c => c.Style == CollectionCoverStyle.Fan).IsSelected);
    }

    [Fact]
    public void UseAsCover_LabelsTheAutoButton_ThePreviewFollows_AndPickingAgainClears()
    {
        var vm = NewVm();
        Assert.Equal("Auto (first member)", vm.AutoCoverLabel);
        Assert.Equal("Alpha", vm.PreviewCoverTile!.Title);

        var bravo = vm.Members.Single(m => m.DisplayTitle == "Bravo");
        bravo.UseAsCoverCommand.Execute(null);

        Assert.True(bravo.IsCoverMember);
        Assert.Equal("Auto (Bravo)", vm.AutoCoverLabel);
        Assert.Equal("Bravo", vm.PreviewCoverTile!.Title);
        Assert.Single(vm.Members, m => m.IsCoverMember);

        bravo.UseAsCoverCommand.Execute(null);
        Assert.False(bravo.IsCoverMember);
        Assert.Equal("Auto (first member)", vm.AutoCoverLabel);
    }

    [Fact]
    public void UseAsCover_SavesTheItemId_AndRemovingThatMemberClearsIt()
    {
        var vm = NewVm();
        var charlie = vm.Members.Single(m => m.DisplayTitle == "Charlie");
        charlie.UseAsCoverCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.Equal(charlie.CollectionItemId, Saved().CoverMemberItemId);

        var again = NewVm();
        Assert.True(again.Members.Single(m => m.DisplayTitle == "Charlie").IsCoverMember);

        again.Members.Single(m => m.DisplayTitle == "Charlie").RemoveCommand.Execute(null);
        Assert.Equal("Auto (first member)", again.AutoCoverLabel);
        again.SaveCommand.Execute(null);
        Assert.Null(Saved().CoverMemberItemId);
    }

    [Fact]
    public void RuleMatchedRows_CannotBeChosenAsTheCover()
    {
        var rule = new CollectionMemberRowViewModel(
            new CollectionMember(null, int.MaxValue, CollectionMemberKind.Series, 9000, "Rule", null, null, null), _ => { }, _ => { });

        Assert.False(rule.CanUseAsCover);
        Assert.False(rule.UseAsCoverCommand.CanExecute(null));
    }
}
