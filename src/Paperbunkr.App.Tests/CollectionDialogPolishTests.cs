using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Edit Collection dialog polish (docs/superpowers/specs/2026-11-06-collection-dialog-polish-design.md): custom accent, contrast hint,
/// description counter, accordion defaults, drag/sort over manual vs rule-matched rows, and the live preview's buffered state.
/// </summary>
public class CollectionDialogPolishTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _collectionId;

    public CollectionDialogPolishTests()
    {
        CollectionPropertiesScreenViewModel.ResetSectionState();
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collectiondialogpolish_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var ids = new List<int>();
        foreach (string name in new[] { "Charlie", "Alpha", "Bravo" })
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
        CollectionPropertiesScreenViewModel.ResetSectionState();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private CollectionPropertiesScreenViewModel NewVm(Func<Color>? surface = null)
    {
        var vm = new CollectionPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        if (surface is not null)
        {
            vm.SurfaceColorProvider = surface;
        }

        vm.Load(_collectionId);
        return vm;
    }

    private static CollectionMemberRowViewModel RuleRow(string title) =>
        new(new CollectionMember(null, 0, CollectionMemberKind.Series, 9000, title, null, null, null), _ => { });

    [Fact]
    public void CustomAccent_ValidHexIsAppliedAndShownAsCustomSwatch()
    {
        var vm = NewVm();
        Assert.False(vm.IsCustomAccent);

        vm.CustomAccentHex = "12ab34";
        vm.ApplyCustomAccentCommand.Execute(null);

        Assert.Equal("#12AB34", vm.AccentColor);
        Assert.True(vm.IsCustomAccent);
        Assert.Null(vm.CustomAccentError);
    }

    [Fact]
    public void CustomAccent_InvalidHexKeepsLastGoodValueAndReportsError()
    {
        var vm = NewVm();
        vm.SetAccentColorCommand.Execute("#5FA889");

        vm.CustomAccentHex = "nope";
        vm.ApplyCustomAccentCommand.Execute(null);

        Assert.Equal("#5FA889", vm.AccentColor);
        Assert.NotNull(vm.CustomAccentError);
    }

    [Fact]
    public void PresetColourIsNotCustomAndClearingRemovesIt()
    {
        var vm = NewVm();
        vm.SetAccentColorCommand.Execute("#5fa889");
        Assert.False(vm.IsCustomAccent);

        vm.ClearAccentColorCommand.Execute(null);
        Assert.False(vm.IsCustomAccent);
        Assert.False(vm.HasAccentContrastWarning);
    }

    [Fact]
    public void ContrastWarning_FollowsTheSurfaceSeam()
    {
        var dark = NewVm(() => Color.FromRgb(0x1E, 0x1A, 0x22));
        dark.SetAccentColorCommand.Execute("#2A2430");
        Assert.True(dark.HasAccentContrastWarning);
        dark.SetAccentColorCommand.Execute("#C9803F");
        Assert.False(dark.HasAccentContrastWarning);

        var light = NewVm(() => Color.FromRgb(0xF5, 0xF1, 0xEA));
        light.SetAccentColorCommand.Execute("#F0ECE0");
        Assert.True(light.HasAccentContrastWarning);
    }

    [Fact]
    public void Description_CounterIsSoftAndNeverBlocksSave()
    {
        var vm = NewVm();
        vm.Description = new string('x', 520);

        Assert.True(vm.DescriptionOverLimit);
        Assert.Equal("520 / 500", vm.DescriptionCounter);

        vm.SaveCommand.Execute(null);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(520, context.Collections.Find(_collectionId)!.Description!.Length);
    }

    [Fact]
    public void Sections_DefaultsAndSessionMemory()
    {
        var first = NewVm();
        Assert.True(first.IsAppearanceOpen);
        Assert.True(first.IsMembersOpen);
        Assert.False(first.IsRulesOpen);
        Assert.False(first.IsRelatedOpen);

        first.ToggleRulesCommand.Execute(null);
        first.ToggleAppearanceCommand.Execute(null);

        var second = NewVm();
        Assert.True(second.IsRulesOpen);
        Assert.False(second.IsAppearanceOpen);
    }

    [Fact]
    public void Summaries_ReflectRulesAndMembers()
    {
        var vm = NewVm();
        Assert.Equal("None set", vm.RulesSummary);
        Assert.Equal("None", vm.RelatedSummary);
        Assert.Equal("3", vm.MembersSummary);
    }

    [Fact]
    public void Sort_ReordersManualRowsAndLeavesRuleRowsInPlace()
    {
        var vm = NewVm();
        vm.Members.Add(RuleRow("Zulu"));

        vm.SortMembers(CollectionMemberSort.TitleAscending);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Zulu" }, vm.Members.Select(m => m.DisplayTitle));

        vm.SortMembers(CollectionMemberSort.TitleDescending);
        Assert.Equal(new[] { "Charlie", "Bravo", "Alpha", "Zulu" }, vm.Members.Select(m => m.DisplayTitle));
        Assert.True(vm.Members.Last().IsRuleMatched);
    }

    [Fact]
    public void Sort_WithRuleRowInTheMiddleKeepsItsPosition()
    {
        var vm = NewVm();
        vm.Members.Insert(1, RuleRow("Middle"));

        vm.SortMembers(CollectionMemberSort.TitleAscending);

        Assert.Equal(new[] { "Alpha", "Middle", "Bravo", "Charlie" }, vm.Members.Select(m => m.DisplayTitle));
    }

    [Fact]
    public void Sort_IsBufferedUntilSaveAndCancelDiscardsIt()
    {
        var vm = NewVm();
        vm.SortMembers(CollectionMemberSort.TitleAscending);
        vm.CancelCommand.Execute(null);

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, CollectionResolver.GetMembers(context, _collectionId).Select(m => m.DisplayTitle));
        }

        vm.SaveCommand.Execute(null);
        using var after = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, CollectionResolver.GetMembers(after, _collectionId).Select(m => m.DisplayTitle));
    }

    [Fact]
    public void Drag_MovesAmongManualRowsAndIgnoresRuleRows()
    {
        var vm = NewVm();
        var rule = RuleRow("Rule");
        vm.Members.Add(rule);
        var charlie = vm.Members[0];
        var bravo = vm.Members[2];

        vm.MoveMemberTo(charlie, bravo);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Rule" }, vm.Members.Select(m => m.DisplayTitle));

        vm.MoveMemberTo(vm.Members[0], rule);
        vm.MoveMemberTo(rule, vm.Members[0]);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Rule" }, vm.Members.Select(m => m.DisplayTitle));
    }

    [Fact]
    public void Preview_ReadsBufferedNameCountAndFollowsOrder()
    {
        var vm = NewVm();
        vm.Name = "  ";
        Assert.Equal("Untitled collection", vm.PreviewName);
        vm.Name = "Fresh";
        Assert.Equal("Fresh", vm.PreviewName);
        Assert.Equal("3 items", vm.PreviewCountText);
        Assert.Equal("Charlie", vm.PreviewCoverTile!.Title);

        vm.SortMembers(CollectionMemberSort.TitleAscending);
        Assert.Equal("Alpha", vm.PreviewCoverTile!.Title);

        vm.IsAutoCover = false;
        Assert.Null(vm.PreviewCoverTile);
    }
}
