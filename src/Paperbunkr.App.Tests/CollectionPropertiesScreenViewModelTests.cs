using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CollectionPropertiesScreenViewModel"/>'s buffered Load/Save/Cancel
/// (docs/superpowers/specs/2026-08-27-collections-design.md, step 8) - name/description/accent/
/// cover fields plus the reorderable/removable member list.
/// </summary>
public class CollectionPropertiesScreenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _collectionId;
    private readonly int _seriesAId;
    private readonly int _seriesBId;

    public CollectionPropertiesScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collectionpropsvm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var seriesA = new Series { Name = "Alpha", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        var seriesB = new Series { Name = "Bravo", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        context.Series.AddRange(seriesA, seriesB);
        context.SaveChanges();
        _seriesAId = seriesA.Id;
        _seriesBId = seriesB.Id;

        var collection = CollectionService.Create(context, "Original Name");
        CollectionService.SetAppearance(context, collection.Id, "Original description", "#C9803F", null, isAutoCover: true);
        CollectionService.AddItems(context, collection.Id, seriesIds: new[] { _seriesAId, _seriesBId });
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

    private Collection GetCollection()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Collections.Find(_collectionId)!;
    }

    [Fact]
    public void Load_PopulatesBufferFromDatabase_IncludingMembers()
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));

        vm.Load(_collectionId);

        Assert.Equal("Edit \"Original Name\"", vm.HeaderLabel);
        Assert.Equal("Original Name", vm.Name);
        Assert.Equal("Original description", vm.Description);
        Assert.Equal("#C9803F", vm.AccentColor);
        Assert.True(vm.IsAutoCover);
        Assert.Equal(new[] { "Alpha", "Bravo" }, vm.Members.Select(m => m.DisplayTitle));
        Assert.All(vm.Members, m => Assert.Equal("Series", m.KindLabel));
    }

    [Fact]
    public void Save_PersistsNameDescriptionAndAccentColor()
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);

        vm.Name = "Renamed";
        vm.Description = "New description";
        vm.SetAccentColorCommand.Execute("#5FA889");
        vm.SaveCommand.Execute(null);

        var collection = GetCollection();
        Assert.Equal("Renamed", collection.Name);
        Assert.Equal("New description", collection.Description);
        Assert.Equal("#5FA889", collection.AccentColor);
    }

    [Fact]
    public void ClearAccentColor_PersistsNull()
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);

        vm.ClearAccentColorCommand.Execute(null);
        vm.SaveCommand.Execute(null);

        Assert.Null(GetCollection().AccentColor);
    }

    [Fact]
    public void RemoveMemberRow_ThenSave_DeletesTheCollectionItem()
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        var alpha = vm.Members.Single(m => m.DisplayTitle == "Alpha");

        alpha.RemoveCommand.Execute(null);
        Assert.DoesNotContain(alpha, vm.Members);
        vm.SaveCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var remaining = CollectionResolver.GetMembers(context, _collectionId);
        Assert.Single(remaining);
        Assert.Equal("Bravo", remaining[0].DisplayTitle);
    }

    [Fact]
    public void MoveMemberDownThenUp_ReordersAndPersists()
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        var alpha = vm.Members.Single(m => m.DisplayTitle == "Alpha");

        vm.MoveMemberDownCommand.Execute(alpha);
        Assert.Equal(new[] { "Bravo", "Alpha" }, vm.Members.Select(m => m.DisplayTitle));

        vm.SaveCommand.Execute(null);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var persisted = CollectionResolver.GetMembers(context, _collectionId);
        Assert.Equal(new[] { "Bravo", "Alpha" }, persisted.Select(m => m.DisplayTitle));
    }

    [Fact]
    public void Cancel_DiscardsUnsavedEdits()
    {
        bool goBackCalled = false;
        var vm = new CollectionPropertiesScreenViewModel(() => goBackCalled = true, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);

        vm.Name = "Should not persist";
        vm.CancelCommand.Execute(null);

        Assert.True(goBackCalled);
        Assert.Equal("Original Name", GetCollection().Name);
    }

    // --- Related Collections (Collection-to-Collection relations) ---

    private int CreateOtherCollection(string name)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return CollectionService.Create(context, name).Id;
    }

    [Fact]
    public void ToggleAddRelation_TogglesPanelState_AndClearsSearch()
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);

        vm.ToggleAddRelationCommand.Execute(null);
        Assert.True(vm.IsAddingRelation);

        vm.ToggleAddRelationCommand.Execute(null);
        Assert.False(vm.IsAddingRelation);
        Assert.Equal(string.Empty, vm.RelationSearchQuery);
    }

    [Fact]
    public void RelationSearchQuery_ExcludesCurrentCollection_MatchesByName()
    {
        CreateOtherCollection("Justice League Saga");
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);

        vm.RelationSearchQuery = "justice";

        var result = Assert.Single(vm.RelationSearchResults);
        Assert.Equal("Justice League Saga", result.Name);
    }

    [Fact]
    public void AddRelation_CreatesRelation_RefreshesRelatedCollections_ClosesPanel()
    {
        int otherId = CreateOtherCollection("Justice League Saga");
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        vm.ToggleAddRelationCommand.Execute(null);
        vm.RelationSearchQuery = "justice";
        var target = Assert.Single(vm.RelationSearchResults);

        vm.AddRelationCommand.Execute(target);

        Assert.False(vm.IsAddingRelation);
        var chip = Assert.Single(vm.RelatedCollections);
        Assert.Equal(otherId, chip.CollectionId);
        Assert.Equal("Justice League Saga", chip.Name);
        Assert.Equal("Related", chip.RelationTypeLabel);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.CollectionRelations);
    }

    [Fact]
    public void RemoveRelation_DeletesRelation_ClearsFromRelatedCollections()
    {
        int otherId = CreateOtherCollection("Justice League Saga");
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        vm.ToggleAddRelationCommand.Execute(null);
        vm.RelationSearchQuery = "justice";
        vm.AddRelationCommand.Execute(Assert.Single(vm.RelationSearchResults));
        var related = Assert.Single(vm.RelatedCollections);

        vm.RemoveRelationCommand.Execute(related);

        Assert.Empty(vm.RelatedCollections);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.CollectionRelations);
    }

    // --- Rule slots (docs/superpowers/specs/2026-08-30-smart-collections-design.md) ---

    private int CreateSeriesSmartList(string matchName)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var list = new SmartList
        {
            Name = "series rule",
            TargetKind = SmartListTargetKind.Series,
            RootGroup = new SmartListConditionGroup
            {
                Mode = SmartListGroupMode.And,
                Conditions = [new SmartListCondition { Field = SmartListField.SeriesName, Operator = SmartListOperator.Is, Value = matchName }],
            },
        };
        context.SmartLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    [Fact]
    public void SetSeriesRule_PersistsTheSlot_AndReflectsRuleMatchedMemberOnReload()
    {
        int seriesCId = default;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var seriesC = new Series { Name = "Charlie" };
            context.Series.Add(seriesC);
            context.SaveChanges();
            seriesCId = seriesC.Id;
        }
        int ruleId = CreateSeriesSmartList("Charlie");

        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        vm.SelectedSeriesSmartList = vm.SeriesSmartLists.Single(o => o.Id == ruleId);
        vm.SetSeriesRuleCommand.Execute(null);

        Assert.Equal(ruleId, GetCollection().SeriesSmartListId);
        var ruleRow = Assert.Single(vm.Members, m => m.DisplayTitle == "Charlie");
        Assert.True(ruleRow.IsRuleMatched);
        Assert.False(ruleRow.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public void ClearSeriesRule_NullsTheSlot()
    {
        int ruleId = CreateSeriesSmartList("Alpha");
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        vm.SelectedSeriesSmartList = vm.SeriesSmartLists.Single(o => o.Id == ruleId);
        vm.SetSeriesRuleCommand.Execute(null);

        vm.ClearSeriesRuleCommand.Execute(null);

        Assert.Null(GetCollection().SeriesSmartListId);
    }

    [Fact]
    public void Save_ReordersOnlyManualMembers_RuleMatchedRowsAreExcluded()
    {
        int ruleId = CreateSeriesSmartList("Charlie"); // matches no manual member -> rule-matched-only row
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.Add(new Series { Name = "Charlie" });
            context.SaveChanges();
        }

        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        vm.SelectedSeriesSmartList = vm.SeriesSmartLists.Single(o => o.Id == ruleId);
        vm.SetSeriesRuleCommand.Execute(null);
        vm.Load(_collectionId); // re-load so Members reflects the newly-set rule before Save

        vm.SaveCommand.Execute(null);

        // Manual members (Alpha/Bravo) keep their real CollectionItem rows; the rule-matched
        // "Charlie" row never becomes one - Save must not have thrown treating it as reorderable.
        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(2, verify.CollectionItems.Count(ci => ci.CollectionId == _collectionId));
    }

    // --- Related (Series-node MediaRelation edges, docs/superpowers/specs/2026-08-30-media-
    // relation-collection-nodes-design.md) ---

    private int AddOtherSeries(string name)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    [Fact]
    public void SearchSeriesRelationCandidates_FindsSeriesOnly()
    {
        AddOtherSeries("Justice League Origin");
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);

        vm.SeriesRelationSearchQuery = "justice";

        var result = Assert.Single(vm.SeriesRelationSearchResults);
        Assert.Equal("Justice League Origin", result.Name);
    }

    [Fact]
    public void AddSeriesRelation_PersistsMixedMediaRelation_PopulatesRelatedSeriesChip()
    {
        int otherId = AddOtherSeries("Justice League Origin");
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        vm.SelectedSeriesRelationTypeOption = CollectionPropertiesScreenViewModel.RelationTypeOptions.First(o => o.Type == RelationType.Crossover);
        vm.SeriesRelationSearchQuery = "justice";
        var target = Assert.Single(vm.SeriesRelationSearchResults);

        vm.AddSeriesRelationCommand.Execute(target);

        var chip = Assert.Single(vm.RelatedSeries);
        Assert.Equal(otherId, chip.SeriesId);
        Assert.Equal("Justice League Origin", chip.Name);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var relation = Assert.Single(context.MediaRelations);
        Assert.Equal(_collectionId, relation.SourceCollectionId);
        Assert.Equal(otherId, relation.TargetSeriesId);
    }

    [Fact]
    public void RemoveSeriesRelation_DeletesRelation_ClearsFromRelatedSeries()
    {
        int otherId = AddOtherSeries("Justice League Origin");
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);
        vm.SeriesRelationSearchQuery = "justice";
        vm.AddSeriesRelationCommand.Execute(Assert.Single(vm.SeriesRelationSearchResults));
        var chip = Assert.Single(vm.RelatedSeries);

        vm.RemoveSeriesRelationCommand.Execute(chip);

        Assert.Empty(vm.RelatedSeries);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Empty(context.MediaRelations);
    }

    [Fact]
    public void RelatedSeriesSection_DoesNotInterfereWith_RelatedCollectionsSection()
    {
        int otherCollectionId = CreateOtherCollection("Sister Collection");
        int otherSeriesId = AddOtherSeries("Justice League Origin");
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_collectionId);

        vm.ToggleAddRelationCommand.Execute(null);
        vm.RelationSearchQuery = "sister";
        vm.AddRelationCommand.Execute(Assert.Single(vm.RelationSearchResults));

        vm.SeriesRelationSearchQuery = "justice";
        vm.AddSeriesRelationCommand.Execute(Assert.Single(vm.SeriesRelationSearchResults));

        var collectionChip = Assert.Single(vm.RelatedCollections);
        Assert.Equal(otherCollectionId, collectionChip.CollectionId);
        var seriesChip = Assert.Single(vm.RelatedSeries);
        Assert.Equal(otherSeriesId, seriesChip.SeriesId);
    }
}
