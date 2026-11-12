using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

public partial class DetailLayoutsV2Tests
{
    [Fact] public void StoryArcLayout_UpgradesToDetectedSections_AndUsesTheSameOrderForReadingAndSaving()
    {
        var vm = CreateComicScreen(); ReadingQueue? queue = null; int saved = 0;
        vm.ReadQueue = (_, order) => queue = order; vm.OpenReadingList = id => saved = id;
        vm.ApplyLayoutJson(DetailListLayoutStateJson.Serialize(DetailListLayoutState.ComicDefault with { GroupBy = "StoryArc" }));
        Assert.Equal(new[] { "Origins", "Other issues" }, vm.IssueGroups.Select(g => g.Header));
        var arc = vm.IssueGroups[0]; Assert.Equal(2, arc.TotalCount); Assert.Contains("from the Story Arc field", arc.Subtitle);
        Assert.Equal("Read this arc", arc.ReadLabel); Assert.True(arc.IsFirstSection);
        arc.ReadCommand!.Execute(null); arc.SaveCommand!.Execute(null);
        Assert.Equal(arc.Items.Select(c => c.Id), queue!.IssueIds);
        using var db = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(queue.IssueIds, db.ReadingListItems.Where(i => i.ReadingListId == saved).OrderBy(i => i.SortOrder).Select(i => i.IssueId));
        Assert.Equal(DetailGrouping.Arcs, DetailListLayoutStateJson.Deserialize(vm.CaptureLayoutJson(), false).GroupBy);
    }
    [Fact] public void CreatorRuns_AreOfferedOnlyWhenUseful_AndShowProgressAndOtherIssues()
    {
        var vm = CreateComicScreen(); Assert.DoesNotContain("Creator runs", vm.CreateDetailListOptions(() => { }).GroupChoices);
        using (var db = new PaperbunkrDbContext(_dbOptions))
        {
            foreach (var issue in db.Issues) { issue.Writer = "Claremont"; issue.PageCount = 20; issue.LastPageRead = 19; }
            db.Issues.AddRange(Enumerable.Range(4, 5).Select(n => new Issue { SeriesId = _seriesId, Number = n.ToString(), Writer = n <= 7 ? "Claremont" : "Other", PageCount = 20 }));
            db.SaveChanges(); EntityIndexSync.RebuildAll(db);
        }
        vm.LoadSeries(LoadSeries());
        Assert.Contains("Creator runs", vm.CreateDetailListOptions(() => { }).GroupChoices);
        vm.ApplyLayoutJson(DetailListLayoutStateJson.Serialize(DetailListLayoutState.ComicDefault with { GroupBy = DetailGrouping.CreatorRuns }));
        Assert.Equal(new[] { "Claremont", "Other issues" }, vm.IssueGroups.Select(g => g.Header));
        Assert.Equal(7, vm.IssueGroups[0].TotalCount); Assert.Equal(3, vm.IssueGroups[0].ReadCount);
        Assert.Equal("Read this run", vm.IssueGroups[0].ReadLabel);
    }
    [Fact] public void LibraryGroupers_KeepTheCompleteWriterAndMainCharacterFields()
    {
        var issue = new Issue { Writer = "Claremont, Byrne", MainCharacterOrTeam = "X-Men", Characters = "Storm" };
        var row = IssueListRow.FromIssue(issue, new Series { Name = "Uncanny" });
        Assert.Equal("Claremont, Byrne", IssueListFieldCatalog.GroupFields[IssueListGroupField.Writer].GroupKey(row));
        Assert.Equal("X-Men", IssueListFieldCatalog.GroupFields[IssueListGroupField.MainCharacterOrTeam].GroupKey(row));
        Assert.Equal(DetailGrouping.Arcs, DetailListLayoutStateJson.Deserialize("{\"GroupBy\":\"StoryArc\"}", false).GroupBy);
    }
}
