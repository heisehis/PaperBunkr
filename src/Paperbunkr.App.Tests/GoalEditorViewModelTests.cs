using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="GoalEditorViewModel"/>'s goal kinds and scope rows (docs/superpowers/specs/2026-10-04-insights-goal-scopes-design.md):
/// what a Finish goal requires, what Create writes, the quick-create and Renew pre-fills. Progress arithmetic is <c>GoalScopeResolverTests</c> (Data.Tests).
/// </summary>
public class GoalEditorViewModelTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;
    private readonly int _civilWarId;
    private readonly int _collectionId;

    public GoalEditorViewModelTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_goal_editor_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
        var list = new ReadingList { Name = "Civil War" };
        var collection = new Collection { Name = "Favourites" };
        ctx.ReadingLists.Add(list);
        ctx.Collections.Add(collection);
        ctx.Series.Add(new Series { Name = "Saga" });
        ctx.SaveChanges();
        _civilWarId = list.Id;
        _collectionId = collection.Id;
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static GoalEditorViewModel NewEditor(Action? onSaved = null) => new(onSaved ?? (() => { }), () => { });

    [Fact]
    public void ACountGoal_NeedsATarget_ButNoScope()
    {
        var editor = NewEditor();
        Assert.False(editor.CanCreate);

        editor.TargetText = "50";

        Assert.True(editor.CanCreate);
        Assert.True(editor.WholeLibrary);
    }

    [Fact]
    public void ABlankScopeRow_BlocksCreate_UntilItNamesSomethingThatExists()
    {
        var editor = NewEditor();
        editor.TargetText = "50";
        editor.AddScopeCommand.Execute(null);
        Assert.False(editor.CanCreate); // a blank row would silently mean "whole library"

        var row = editor.ScopeRows.Single();
        row.KindLabel = "Reading list";
        row.ValueText = "No such list";
        Assert.False(editor.CanCreate);

        row.ValueText = "civil war"; // case-insensitive
        Assert.True(editor.CanCreate);
    }

    [Fact]
    public void AFinishGoal_NeedsAScope_AndTheTargetIsNotAskedFor()
    {
        var editor = NewEditor();

        editor.SetKindCommand.Execute(GoalKind.Finish);

        Assert.True(editor.IsFinishGoal);
        Assert.Equal(GoalPeriodKind.NoDeadline, editor.PeriodKind);
        Assert.Contains(editor.PeriodOptions, o => o.Value == GoalPeriodKind.NoDeadline);
        Assert.False(editor.CanCreate); // one blank membership row is waiting to be filled

        editor.ScopeRows.Single().ValueText = "Civil War";
        Assert.True(editor.CanCreate);
        Assert.Equal("Finish Civil War", editor.Title);
    }

    [Fact]
    public void AFinishGoal_OnlyOffersScopesWithACountableSet()
    {
        var editor = NewEditor();
        editor.SetKindCommand.Execute(GoalKind.Finish);

        Assert.DoesNotContain("Publisher", editor.ScopeKindLabels);
        Assert.DoesNotContain("Series", editor.ScopeKindLabels);
        Assert.Contains("Reading list", editor.ScopeKindLabels);
        Assert.Contains("Collection", editor.ScopeKindLabels);

        editor.SetKindCommand.Execute(GoalKind.Count);
        Assert.Contains("Publisher", editor.ScopeKindLabels);
        Assert.Contains("Media type", editor.ScopeKindLabels);
    }

    [Fact]
    public void SwitchingToFinish_DropsRowsThatHaveNoCountableSet()
    {
        var editor = NewEditor();
        editor.AddScopeCommand.Execute(null);
        editor.ScopeRows.Single().KindLabel = "Publisher";
        editor.ScopeRows.Single().ValueText = "Marvel";

        editor.SetKindCommand.Execute(GoalKind.Finish);

        var row = Assert.Single(editor.ScopeRows);
        Assert.Contains(row.Kind, GoalEditorViewModel.FinishScopeKinds);
    }

    [Fact]
    public void Create_ForACountGoal_WritesCombinedScopeRows_AndTheDistinctFlag()
    {
        var saved = false;
        var editor = NewEditor(() => saved = true);
        editor.TargetText = "20";
        editor.DistinctOnly = true;
        editor.AddScopeCommand.Execute(null);
        editor.AddScopeCommand.Execute(null);
        editor.ScopeRows[0].KindLabel = "Reading list";
        editor.ScopeRows[0].ValueText = "Civil War";
        editor.ScopeRows[1].KindLabel = "Publisher";
        editor.ScopeRows[1].ValueText = "Marvel";

        editor.CreateCommand.Execute(null);

        Assert.True(saved);
        using var ctx = PaperbunkrDb.CreateContext();
        var goal = ctx.ReadingGoals.Include(g => g.Scopes).Single();
        Assert.Equal(GoalKind.Count, goal.Kind);
        Assert.Equal(20, goal.Target);
        Assert.True(goal.DistinctOnly);
        Assert.Equal(GoalScopeKind.Library, goal.ScopeKind); // the legacy pair stays unused
        Assert.Equal(2, goal.Scopes.Count);
        var list = goal.Scopes.Single(s => s.Kind == GoalScopeKind.ReadingList);
        Assert.Equal(_civilWarId.ToString(), list.Value);
        Assert.Equal("Civil War", list.Label);
        Assert.Equal("Marvel", goal.Scopes.Single(s => s.Kind == GoalScopeKind.Publisher).Value);
    }

    [Fact]
    public void Create_ForAFinishGoal_WithNoDeadline_UsesTheSentinelEnd_AndALiveTarget()
    {
        var editor = NewEditor();
        editor.SetKindCommand.Execute(GoalKind.Finish);
        editor.ScopeRows.Single().KindLabel = "Collection";
        editor.ScopeRows.Single().ValueText = "Favourites";

        editor.CreateCommand.Execute(null);

        using var ctx = PaperbunkrDb.CreateContext();
        var goal = ctx.ReadingGoals.Include(g => g.Scopes).Single();
        Assert.Equal(GoalKind.Finish, goal.Kind);
        Assert.Equal(GoalMetric.Items, goal.Metric);
        Assert.Equal(0, goal.Target);
        Assert.Equal(GoalPeriodKind.NoDeadline, goal.PeriodKind);
        Assert.Equal(DateOnly.MaxValue, goal.PeriodEnd);
        Assert.Equal(_collectionId.ToString(), goal.Scopes.Single().Value);
    }

    [Fact]
    public void StartFinishGoal_PrefillsAFinishGoalForThatList()
    {
        var editor = NewEditor();

        Assert.True(editor.StartFinishGoal(GoalScopeKind.ReadingList, _civilWarId));

        Assert.True(editor.IsFinishGoal);
        var row = Assert.Single(editor.ScopeRows);
        Assert.Equal(GoalScopeKind.ReadingList, row.Kind);
        Assert.Equal("Civil War", row.ValueText);
        Assert.True(editor.CanCreate);
        Assert.Equal("Finish Civil War", editor.Title);
    }

    [Fact]
    public void StartFinishGoal_RefusesAKindAFinishGoalCannotUse()
    {
        var editor = NewEditor();
        Assert.False(editor.StartFinishGoal(GoalScopeKind.Publisher, 1));
        Assert.False(editor.StartFinishGoal(GoalScopeKind.ReadingList, 9999)); // no such list
    }

    [Fact]
    public void Renew_CopiesKindDistinctFlagAndEveryScope()
    {
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            ctx.ReadingGoals.Add(new ReadingGoal
            {
                Title = "Old", Kind = GoalKind.Count, Metric = GoalMetric.Items, Target = 12, DistinctOnly = true,
                PeriodKind = GoalPeriodKind.ThisMonth, PeriodStart = new DateOnly(2026, 8, 1), PeriodEnd = new DateOnly(2026, 8, 31),
                CreatedUtc = DateTime.UtcNow,
                Scopes =
                {
                    new ReadingGoalScope { Kind = GoalScopeKind.ReadingList, Value = _civilWarId.ToString(), Label = "Civil War" },
                    new ReadingGoalScope { Kind = GoalScopeKind.Publisher, Value = "Marvel", Label = "Marvel" },
                },
            });
            ctx.SaveChanges();
        }

        int goalId;
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            goalId = ctx.ReadingGoals.Single().Id;
        }

        var editor = NewEditor();
        Assert.True(editor.LoadFrom(goalId));

        Assert.Equal(GoalKind.Count, editor.Kind);
        Assert.Equal("12", editor.TargetText);
        Assert.True(editor.DistinctOnly);
        Assert.Equal(GoalPeriodKind.ThisMonth, editor.PeriodKind);
        Assert.Equal(2, editor.ScopeRows.Count);
        Assert.Contains(editor.ScopeRows, r => r.Kind == GoalScopeKind.ReadingList && r.ValueText == "Civil War");
        Assert.Contains(editor.ScopeRows, r => r.Kind == GoalScopeKind.Publisher && r.ValueText == "Marvel");
        Assert.True(editor.CanCreate);
    }

    [Fact]
    public void Renew_OfAFinishGoal_KeepsItsNoDeadlineShape()
    {
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            ctx.ReadingGoals.Add(new ReadingGoal
            {
                Title = "Finish", Kind = GoalKind.Finish, Metric = GoalMetric.Items, PeriodKind = GoalPeriodKind.NoDeadline,
                PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = DateOnly.MaxValue, CreatedUtc = DateTime.UtcNow,
                Scopes = { new ReadingGoalScope { Kind = GoalScopeKind.ReadingList, Value = _civilWarId.ToString(), Label = "Civil War" } },
            });
            ctx.SaveChanges();
        }

        int goalId;
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            goalId = ctx.ReadingGoals.Single().Id;
        }

        var editor = NewEditor();
        Assert.True(editor.LoadFrom(goalId));

        Assert.True(editor.IsFinishGoal);
        Assert.Equal(GoalPeriodKind.NoDeadline, editor.PeriodKind);
        Assert.Equal("Civil War", Assert.Single(editor.ScopeRows).ValueText);
        Assert.True(editor.CanCreate);
    }
}
