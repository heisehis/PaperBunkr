using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests.ReadingLists;

public sealed class ReadingListTagReconcilerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_auto_tags_{Guid.NewGuid():N}.db");
    private DbContextOptions<PaperbunkrDbContext> Options => new DbContextOptionsBuilder<PaperbunkrDbContext>()
        .UseSqlite($"Data Source={_path}").Options;

    public ReadingListTagReconcilerTests()
    {
        using var context = Create();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    private PaperbunkrDbContext Create() => new(Options);

    [Fact]
    public void SaveChanges_GeneratesMetadataAndBothIssueTagFields_WithoutWeights()
    {
        int listId;
        using (var context = Create())
        {
            var issue = new Issue { Number = "1", Series = new Series { Name = "Secret Wars" } };
            issue.Tags.Add(new IssueTag { Field = IssueTagField.Genre, Value = "Action", Category = "Genre", Weight = IssueTagWeight.Core });
            issue.Tags.Add(new IssueTag { Field = IssueTagField.Tags, Value = "Found Family", Category = "Theme", Weight = IssueTagWeight.Defining });
            var storyEvent = new StoryEvent { Name = "Secret Wars" };
            var continuity = new Continuity { Name = "Marvel Universe" };
            var list = new ReadingList
            {
                Name = "Secret Wars Order",
                Source = "MetronLists",
                Type = ReadingListType.Event,
                StoryEvent = storyEvent,
                Continuity = continuity,
            };
            list.Items.Add(new ReadingListItem { Issue = issue, SortOrder = 0 });
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;
        }

        using var check = Create();
        var tags = check.ReadingListTags.Where(tag => tag.ReadingListId == listId).ToList();
        Assert.Equal(6, tags.Count);
        Assert.Contains(tags, tag => tag.Value == "Metron reading lists" && tag.Category == "Source");
        Assert.Contains(tags, tag => tag.Value == "Event" && tag.Category == "List type");
        Assert.Contains(tags, tag => tag.Value == "Secret Wars" && tag.Category == "Story Event");
        Assert.Contains(tags, tag => tag.Value == "Marvel Universe" && tag.Category == "Continuity");
        Assert.Contains(tags, tag => tag.Value == "Action" && tag.Category == "Genre");
        Assert.Contains(tags, tag => tag.Value == "Found Family" && tag.Category == "Theme");
        Assert.All(tags, tag =>
        {
            Assert.Equal(ReadingListTagOrigin.Generated, tag.Origin);
            Assert.Equal(IssueTagWeight.Unset, tag.Weight);
        });
    }

    [Fact]
    public void Reconcile_PreservesManualTagsAndUpdatesOnlyGeneratedRows()
    {
        int listId;
        using (var context = Create())
        {
            var list = new ReadingList { Name = "Manual", Type = ReadingListType.User };
            list.Tags.Add(new ReadingListTag { Value = "Action", Category = "Genre", Weight = IssueTagWeight.Core });
            context.ReadingLists.Add(list);
            context.SaveChanges();
            listId = list.Id;
        }

        using (var context = Create())
        {
            var list = context.ReadingLists.Single(item => item.Id == listId);
            list.Type = ReadingListType.Community;
            context.SaveChanges();
        }

        using var check = Create();
        var tags = check.ReadingListTags.Where(tag => tag.ReadingListId == listId).ToList();
        var manual = Assert.Single(tags.Where(tag => tag.Origin == ReadingListTagOrigin.User));
        Assert.Equal(("Action", "Genre", IssueTagWeight.Core), (manual.Value, manual.Category, manual.Weight));
        Assert.Contains(tags, tag => tag.Value == "Community" && tag.Category == "List type");
    }

    [Fact]
    public void IssueTagEditsAndListMembershipChanges_RefreshAffectedLists()
    {
        int listId;
        int issueId;
        int itemId;
        using (var context = Create())
        {
            var issue = new Issue { Number = "1", Series = new Series { Name = "Series" } };
            issue.Tags.Add(new IssueTag { Field = IssueTagField.Tags, Value = "Old", Category = "Mood" });
            var list = new ReadingList { Name = "List", Type = ReadingListType.User };
            var item = new ReadingListItem { Issue = issue, SortOrder = 0 };
            list.Items.Add(item);
            context.ReadingLists.Add(list);
            context.SaveChanges();
            (listId, issueId, itemId) = (list.Id, issue.Id, item.Id);
        }

        using (var context = Create())
        {
            var tag = context.IssueTags.Single(tag => tag.IssueId == issueId);
            tag.Value = "New";
            context.SaveChanges();
        }

        using (var context = Create())
        {
            Assert.Contains(context.ReadingListTags, tag => tag.ReadingListId == listId && tag.Value == "New");
            Assert.DoesNotContain(context.ReadingListTags, tag => tag.ReadingListId == listId && tag.Value == "Old");
            context.ReadingListItems.Remove(context.ReadingListItems.Single(item => item.Id == itemId));
            context.SaveChanges();
        }

        using var check = Create();
        Assert.DoesNotContain(check.ReadingListTags, tag => tag.ReadingListId == listId && tag.Category == "Mood");
    }

    [Fact]
    public void ReconcileAll_IsIdempotent_AndProcessesBoundedBatches()
    {
        using (var context = Create())
        {
            context.ReadingLists.AddRange(Enumerable.Range(0, 5).Select(n => new ReadingList { Name = $"List {n}", Type = ReadingListType.User }));
            context.SaveChanges();
            context.ReadingListTags.RemoveRange(context.ReadingListTags.Where(tag => tag.Origin == ReadingListTagOrigin.Generated));
            context.SaveChanges();
        }

        int first = ReadingListTagReconciler.ReconcileAll(Create, batchSize: 2);
        int second = ReadingListTagReconciler.ReconcileAll(Create, batchSize: 2);

        Assert.Equal(5, first); // one generated List type tag for each list
        Assert.Equal(0, second);
    }
}
