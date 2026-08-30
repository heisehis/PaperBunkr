using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the Smart Lists results grid (docs/superpowers/specs/
/// 2026-08-09-smart-lists-results-view-design.md) - previously untested, since
/// <see cref="SmartScreenViewModel"/> only ever surfaced <c>MatchCountLabel</c>. Redirects
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file, matching
/// <see cref="ReaderScreenViewModelTests"/>'s pattern - <see cref="SmartScreenViewModel"/> has no
/// injected context-factory seam. Runs under <see cref="AvaloniaTestCollection"/> since cover
/// brushes/images need a real Skia platform.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SmartScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public SmartScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_smart_vm_test_{Guid.NewGuid():N}.db");
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

    private static int CreateSeriesWithIssues(string seriesName, params string[] issueNumbers)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();

        foreach (var number in issueNumbers)
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = number });
        }

        context.SaveChanges();
        return series.Id;
    }

    private static int CreateSmartList(string name, params SmartListCondition[] conditions)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new SmartList
        {
            Name = name,
            IsSystem = false,
            RootGroup = new SmartListConditionGroup { Mode = SmartListGroupMode.And, Conditions = conditions.ToList() },
        };
        context.SmartLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    [Fact]
    public void LoadSmartList_PopulatesResults_MatchingBuildQuery()
    {
        CreateSeriesWithIssues("Series One", "1", "2");
        CreateSeriesWithIssues("Series Two", "1");
        int listId = CreateSmartList("Everything"); // zero conditions -> matches every issue

        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.LoadSmartList(listId);

        Assert.Equal(3, vm.Results.Count);
        Assert.Equal("3", vm.MatchCountLabel);
        Assert.True(vm.HasResults);
    }

    [Fact]
    public void EditingCondition_UpdatesResultsAndCount_Live()
    {
        int matchSeriesId = CreateSeriesWithIssues("Match Me", "1");
        CreateSeriesWithIssues("Skip Me", "1");
        var condition = new SmartListCondition { Field = SmartListField.SeriesName, Operator = SmartListOperator.Is, Value = "Nonexistent" };
        int listId = CreateSmartList("Filtered", condition);

        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.LoadSmartList(listId);
        Assert.Empty(vm.Results);
        Assert.False(vm.HasResults);
        Assert.Equal("0", vm.MatchCountLabel);

        vm.RootGroup!.Conditions[0].Value = "Match Me";

        Assert.True(vm.HasResults);
        var result = Assert.Single(vm.Results);
        Assert.Equal(matchSeriesId, result.SeriesId);
        Assert.Equal("1", vm.MatchCountLabel);
    }

    [Fact]
    public void Results_MapCoverBrushToEachIssuesOwnSeries_NotASharedBrush()
    {
        int seriesAId = CreateSeriesWithIssues("Series A", "1", "2");
        int seriesBId = CreateSeriesWithIssues("Series B", "1");
        int listId = CreateSmartList("Everything");

        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.LoadSmartList(listId);

        var expectedColorA = FirstStopColor(SeriesCardSample.CoverBrushFor("Series A"));
        var expectedColorB = FirstStopColor(SeriesCardSample.CoverBrushFor("Series B"));
        Assert.NotEqual(expectedColorA, expectedColorB); // sanity: the two series really do differ

        foreach (var result in vm.Results)
        {
            var actualColor = FirstStopColor(result.CoverBrush);
            var expectedColor = result.SeriesId == seriesAId ? expectedColorA
                : result.SeriesId == seriesBId ? expectedColorB
                : throw new Xunit.Sdk.XunitException($"Unexpected SeriesId {result.SeriesId}");
            Assert.Equal(expectedColor, actualColor);
        }
    }

    [Fact]
    public void SelectResultCommand_InvokesGoToSeries_WithTheResultsSeriesId()
    {
        int seriesId = CreateSeriesWithIssues("Only Series", "1");
        int listId = CreateSmartList("Everything");
        int? navigatedSeriesId = null;
        var vm = new SmartScreenViewModel(goToSeries: id => navigatedSeriesId = id, goToBook: _ => { });
        vm.LoadSmartList(listId);

        vm.SelectResultCommand.Execute(vm.Results[0]);

        Assert.Equal(seriesId, navigatedSeriesId);
    }

    /// <summary>
    /// P6 follow-up (docs/alpha-todo.md): the sidebar "▾ Maintenance" caret in MainWindow.axaml
    /// used to be a plain unbound TextBlock that looked like a collapse toggle but did nothing.
    /// </summary>
    [Fact]
    public void ToggleMaintenance_FlipsIsMaintenanceExpanded()
    {
        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        Assert.True(vm.IsMaintenanceExpanded);

        vm.ToggleMaintenanceCommand.Execute(null);
        Assert.False(vm.IsMaintenanceExpanded);

        vm.ToggleMaintenanceCommand.Execute(null);
        Assert.True(vm.IsMaintenanceExpanded);
    }

    private static Color FirstStopColor(IBrush brush) => Assert.IsType<LinearGradientBrush>(brush).GradientStops[0].Color;

    // --- SmartList Engine v2 (docs/superpowers/specs/2026-08-28-smartlist-engine-v2-design.md) ---

    [Fact]
    public void RootGroup_AddConditionAndAddGroup_MutateTheTree_AndAGroupCanBeRemoved()
    {
        SeedSystemList("All Series");
        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.CreateNewCommand.Execute(null);

        var root = vm.RootGroup!;
        Assert.Empty(root.Conditions);
        Assert.Empty(root.ChildGroups);

        root.AddConditionCommand.Execute(null);
        root.AddGroupCommand.Execute(null);
        Assert.Single(root.Conditions);
        Assert.Single(root.ChildGroups);

        var child = root.ChildGroups[0];
        Assert.False(child.IsRoot);
        Assert.True(child.CanRemove);
        child.RemoveCommand.Execute(null);
        Assert.Empty(root.ChildGroups);
    }

    [Fact]
    public void RootGroup_CannotBeRemoved()
    {
        SeedSystemList("All Series");
        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.CreateNewCommand.Execute(null);

        Assert.True(vm.RootGroup!.IsRoot);
        Assert.False(vm.RootGroup.CanRemove);
    }

    [Fact]
    public void SelectingAllPropertiesField_RevealsTheSecondarySearchInDropdown_DefaultingToAll()
    {
        SeedSystemList("All Series");
        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.CreateNewCommand.Execute(null);
        vm.RootGroup!.AddConditionCommand.Execute(null);

        var row = vm.RootGroup.Conditions[0];
        Assert.False(row.IsAllPropertiesField); // starts on SeriesName

        row.SelectedField = row.FieldOptions.Single(f => f.Field == Paperbunkr.Data.Entities.SmartListField.AllProperties);

        Assert.True(row.IsAllPropertiesField);
        Assert.Equal(Paperbunkr.Data.Entities.SearchMode.All, row.SelectedSearchMode.Mode);
        Assert.True(row.ShowCaseToggle);
    }

    [Fact]
    public void NestedOrGroup_PersistsThroughSaveAndReload_AndEvaluatesRecursively()
    {
        int zenithA = CreateSeriesWithIssues("Zenith A", "1");
        int zenithB = CreateSeriesWithIssues("Zenith B", "1");
        CreateSeriesWithIssues("Other", "1");
        SeedSystemList("All Series");

        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.CreateNewCommand.Execute(null);

        // Root AND: SeriesName contains "Zenith"  AND  child OR( SeriesName is "Zenith A" , SeriesName is "Zenith B" )
        var root = vm.RootGroup!;
        root.AddConditionCommand.Execute(null);
        root.Conditions[0].SelectedField = root.Conditions[0].FieldOptions.Single(f => f.Field == Paperbunkr.Data.Entities.SmartListField.SeriesName);
        root.Conditions[0].SelectedOperator = root.Conditions[0].OperatorOptions.Single(o => o.Operator == Paperbunkr.Data.Entities.SmartListOperator.Contains);
        root.Conditions[0].Value = "Zenith";

        root.AddGroupCommand.Execute(null);
        var child = root.ChildGroups[0];
        Assert.True(child.IsOr); // "Add group" seeds an OR group
        child.AddConditionCommand.Execute(null);
        child.AddConditionCommand.Execute(null);
        child.Conditions[0].Value = "Zenith A";
        child.Conditions[1].Value = "Zenith B";

        vm.SaveCommand.Execute(null);

        // Reload fresh and confirm the tree + evaluation survived.
        var reloaded = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        reloaded.LoadSmartList(vm.CustomLists.Single().Id);
        Assert.Single(reloaded.RootGroup!.ChildGroups);
        Assert.True(reloaded.RootGroup.ChildGroups[0].IsOr);
        Assert.Equal(2, reloaded.RootGroup.ChildGroups[0].Conditions.Count);

        var matchedSeries = reloaded.Results.Select(r => r.SeriesId).Distinct().OrderBy(x => x).ToList();
        Assert.Equal(new[] { zenithA, zenithB }.OrderBy(x => x), matchedSeries);
    }

    // --- Delete a custom list (docs/superpowers/specs/2026-08-22-delete-functionality-design.md) ---

    /// <summary>This test fixture's DB only calls <c>context.Database.EnsureCreated()</c> (bare EF Core schema creation), not the app's own <c>PaperbunkrDb.EnsureCreated</c> which additionally seeds the real system smart lists - so a test needing one present has to seed it directly.</summary>
    private static int SeedSystemList(string name)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new SmartList { Name = name, IsSystem = true, SortOrder = context.SmartLists.Count() };
        context.SmartLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    [Fact]
    public void BuiltInAndMaintenanceLists_HaveNoDeleteConfirm()
    {
        SeedSystemList("All Series");
        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });

        Assert.NotEmpty(vm.BuiltInLists);
        Assert.All(vm.BuiltInLists, l => Assert.Null(l.DeleteConfirm));
        Assert.All(vm.MaintenanceLists, l => Assert.Null(l.DeleteConfirm));
    }

    [Fact]
    public void DeleteConfirm_Trigger_RequiresTwoClicks_ThenRemovesTheCustomList()
    {
        SeedSystemList("All Series"); // something to fall back to once the active custom list is gone
        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.CreateNewCommand.Execute(null);
        var summary = vm.CustomLists.Single();
        Assert.NotNull(summary.DeleteConfirm);

        summary.DeleteConfirm!.TriggerCommand.Execute(null);
        Assert.Single(vm.CustomLists); // still armed, not yet deleted

        summary.DeleteConfirm.TriggerCommand.Execute(null);
        Assert.Empty(vm.CustomLists);

        using var context = PaperbunkrDb.CreateContext();
        Assert.False(context.SmartLists.Any(l => l.Id == summary.Id));
    }

    [Fact]
    public void Delete_OfTheActiveCustomList_FallsBackToABuiltInList()
    {
        SeedSystemList("All Series");
        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        vm.CreateNewCommand.Execute(null); // becomes active
        var summary = vm.CustomLists.Single();

        summary.DeleteConfirm!.TriggerCommand.Execute(null);
        summary.DeleteConfirm.TriggerCommand.Execute(null);

        Assert.Empty(vm.CustomLists);
        Assert.Equal("All Series", vm.ListName); // fell back to the built-in list, not a blank screen
    }

    [Fact]
    public void Delete_NeverOffered_CannotDeleteASystemList()
    {
        int systemListId = SeedSystemList("All Series");

        var vm = new SmartScreenViewModel(goToSeries: _ => { }, goToBook: _ => { });
        var summary = vm.BuiltInLists.Concat(vm.MaintenanceLists).First(l => l.Id == systemListId);

        Assert.Null(summary.DeleteConfirm); // no delete affordance exists for a system list at all
        using var verifyContext = PaperbunkrDb.CreateContext();
        Assert.True(verifyContext.SmartLists.Any(l => l.Id == systemListId));
    }
}
