using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Sharing;
using Paperbunkr.Sharing;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="DbShareCatalogSource"/> (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md
/// §5): what the host is willing to serve, resolved from the sharing scope. Real SQLite, no network.
/// </summary>
public class DbShareCatalogSourceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly int _seriesId;
    private readonly int _servable1, _servable2, _servable3;
    private readonly int _placeholder, _missing, _noPath;
    private ShareScope _scope = new() { Mode = ShareMode.All };

    public DbShareCatalogSourceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_sharecat_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Saga", Publisher = "Image" };
        var other = new Series { Name = "Other" };
        context.Series.AddRange(series, other);
        context.SaveChanges();
        _seriesId = series.Id;

        Issue Make(Series s, string number, Action<Issue>? tweak = null)
        {
            var i = new Issue
            {
                SeriesId = s.Id, Number = number, Title = $"Issue {number}", FilePath = $@"C:\comics\{s.Name}-{number}.cbz",
                // Host-personal state that must never be shared, seeded so a leak would be visible:
                Rating = 5, Review = "private review", Notes = "private note", LastPageRead = 7, OpenCount = 9,
            };
            tweak?.Invoke(i);
            return i;
        }

        var a = Make(series, "1", i => i.MergeFrom(IssueTagField.Tags, new[] { "Space" }));
        var b = Make(series, "2");
        var c = Make(other, "1");
        var placeholder = Make(series, "3", i => { i.IsPlaceholder = true; i.FilePath = null; });
        var missing = Make(series, "4", i => i.FileIsMissing = true);
        var noPath = Make(series, "5", i => i.FilePath = null);
        context.Issues.AddRange(a, b, c, placeholder, missing, noPath);
        context.SaveChanges();
        (_servable1, _servable2, _servable3) = (a.Id, b.Id, c.Id);
        (_placeholder, _missing, _noPath) = (placeholder.Id, missing.Id, noPath.Id);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private DbShareCatalogSource Source(TimeProvider? time = null, TimeSpan? ttl = null) =>
        new(() => new PaperbunkrDbContext(_options), () => _scope, time, ttl);

    private static async Task<List<int>> AllIssueIdsAsync(DbShareCatalogSource source)
    {
        var ids = new List<int>();
        string? cursor = null;
        do
        {
            var page = await source.GetCatalogPageAsync(cursor, 2, CancellationToken.None);
            ids.AddRange(page.Issues.Select(i => i.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return ids;
    }

    [Fact]
    public async Task ModeNone_SharesNothing()
    {
        _scope = new ShareScope { Mode = ShareMode.None };
        var source = Source();

        Assert.Empty(await AllIssueIdsAsync(source));
        Assert.False(await source.IsIssueSharedAsync(_servable1, default));
    }

    [Fact]
    public async Task ModeAll_SharesOnlyServableIssues()
    {
        var source = Source();

        var ids = await AllIssueIdsAsync(source);

        Assert.Equal(new[] { _servable1, _servable2, _servable3 }.OrderBy(x => x), ids);
        Assert.DoesNotContain(_placeholder, ids);
        Assert.DoesNotContain(_missing, ids);
        Assert.DoesNotContain(_noPath, ids);
        Assert.False(await source.IsIssueSharedAsync(_placeholder, default));
        Assert.False(await source.IsIssueSharedAsync(_noPath, default));
    }

    [Fact]
    public async Task Paging_WalksEveryIssueOnce_AndIncludesEachPagesSeries()
    {
        var source = Source();

        var first = await source.GetCatalogPageAsync(null, 2, default);
        var second = await source.GetCatalogPageAsync(first.NextCursor, 2, default);

        Assert.Equal(2, first.Issues.Count);
        Assert.NotNull(first.NextCursor);
        Assert.Single(second.Issues);
        Assert.Null(second.NextCursor);
        Assert.All(first.Issues, i => Assert.Contains(first.Series, s => s.Id == i.SeriesId));
        Assert.Equal(first.CatalogVersion, second.CatalogVersion);
    }

    [Fact]
    public async Task Dtos_NeverLeakHostPersonalData()
    {
        var page = await Source().GetCatalogPageAsync(null, 50, default);
        string json = System.Text.Json.JsonSerializer.Serialize(page);

        Assert.DoesNotContain("private review", json);
        Assert.DoesNotContain("private note", json);
        Assert.DoesNotContain("comics", json, StringComparison.OrdinalIgnoreCase); // no file path fragments
        Assert.Contains("Space", json); // real metadata still flows
    }

    [Fact]
    public async Task Selected_ReadingList_SharesOnlyItsIssues_AndListsIt()
    {
        int listId;
        using (var context = new PaperbunkrDbContext(_options))
        {
            var list = new ReadingList { Name = "Favorites" };
            context.ReadingLists.Add(list);
            context.SaveChanges();
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = _servable1, SortOrder = 0 });
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = _placeholder, SortOrder = 1 }); // unservable: still excluded
            context.SaveChanges();
            listId = list.Id;
        }

        _scope = new ShareScope { Mode = ShareMode.Selected, ReadingListIds = { listId } };
        var source = Source();

        Assert.Equal(new[] { _servable1 }, await AllIssueIdsAsync(source));
        Assert.False(await source.IsIssueSharedAsync(_servable2, default));
        var lists = await source.GetListsAsync(default);
        Assert.Equal("Favorites", lists.Single().Name);
        Assert.Equal(DbShareCatalogSource.ReadingListKind, lists.Single().Kind);
    }

    [Fact]
    public async Task Selected_Collection_ResolvesSeriesAndIssueMembers()
    {
        int collectionId;
        using (var context = new PaperbunkrDbContext(_options))
        {
            var collection = new Collection { Name = "Mix" };
            context.Collections.Add(collection);
            context.SaveChanges();
            context.Set<CollectionItem>().Add(new CollectionItem { CollectionId = collection.Id, SeriesId = _seriesId, SortOrder = 0 });
            context.SaveChanges();
            collectionId = collection.Id;
        }

        _scope = new ShareScope { Mode = ShareMode.Selected, CollectionIds = { collectionId } };

        // The whole "Saga" series is a member, so its servable issues are shared - not "Other"'s.
        Assert.Equal(new[] { _servable1, _servable2 }.OrderBy(x => x), await AllIssueIdsAsync(Source()));
    }

    [Fact]
    public async Task Selected_SmartList_SharesMatchingIssues()
    {
        int smartId;
        using (var context = new PaperbunkrDbContext(_options))
        {
            var root = new SmartListConditionGroup { Mode = SmartListGroupMode.And };
            root.Conditions.Add(new SmartListCondition { Field = SmartListField.Publisher, Operator = SmartListOperator.Is, Value = "Image" });
            var smart = new SmartList { Name = "Image books", RootGroup = root };
            context.SmartLists.Add(smart);
            context.SaveChanges();
            smartId = smart.Id;

            // Give issue 1 (only) the publisher the rule looks for.
            context.Issues.Single(i => i.Id == _servable1).Publisher = "Image";
            context.SaveChanges();
        }

        _scope = new ShareScope { Mode = ShareMode.Selected, SmartListIds = { smartId } };

        Assert.Equal(new[] { _servable1 }, await AllIssueIdsAsync(Source()));
    }

    [Fact]
    public async Task VersionChanges_WhenVisibleMetadataChanges_ButNotWhenPersonalStateDoes()
    {
        var time = new FakeTimeProvider();
        var source = Source(time, TimeSpan.FromSeconds(1));
        string before = await source.GetCatalogVersionAsync(default);

        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Issues.Single(i => i.Id == _servable1).Rating = 1;      // personal - invisible to clients
            context.Issues.Single(i => i.Id == _servable1).LastPageRead = 20;
            context.SaveChanges();
        }
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(before, await source.GetCatalogVersionAsync(default));

        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Issues.Single(i => i.Id == _servable1).Title = "Renamed";  // visible
            context.SaveChanges();
        }
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.NotEqual(before, await source.GetCatalogVersionAsync(default));
    }

    [Fact]
    public async Task TheContentStamp_ChangesWhenTheBookIsReplaced_AndRevealsNothingAboutIt()
    {
        var source = Source(new FakeTimeProvider(), TimeSpan.Zero);
        string? before;
        using (var context = new PaperbunkrDbContext(_options))
        {
            var issue = context.Issues.Single(i => i.Id == _servable1);
            issue.FileSize = 123456; issue.FileModifiedTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc); issue.PageCount = 22;
            context.SaveChanges();
        }
        before = (await source.GetCatalogPageAsync(null, 50, default)).Issues.Single(i => i.Id == _servable1).ContentStamp;

        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Issues.Single(i => i.Id == _servable1).FileModifiedTime = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);   // file replaced on disk
            context.SaveChanges();
        }
        source.Invalidate();
        string? after = (await source.GetCatalogPageAsync(null, 50, default)).Issues.Single(i => i.Id == _servable1).ContentStamp;

        Assert.NotNull(before);
        Assert.NotEqual(before, after);
        Assert.Equal(16, after!.Length);
        Assert.DoesNotContain("123456", after);                               // an opaque hash, not the raw size
    }

    [Fact]
    public async Task ABookWithNoFileFacts_HasNoStamp_AndTheCatalogVersionMovesWithTheStamp()
    {
        var source = Source(new FakeTimeProvider(), TimeSpan.Zero);
        var page = await source.GetCatalogPageAsync(null, 50, default);
        Assert.Null(page.Issues.First().ContentStamp);                        // seeded rows have no size/mtime

        string versionBefore = await source.GetCatalogVersionAsync(default);
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.Issues.Single(i => i.Id == _servable1).FileSize = 999;
            context.SaveChanges();
        }
        source.Invalidate();

        Assert.NotEqual(versionBefore, await source.GetCatalogVersionAsync(default));   // a replaced book makes clients re-pull the catalog
    }

    [Fact]
    public async Task Snapshot_IsCachedUntilTtlOrInvalidate()
    {
        var time = new FakeTimeProvider();
        var source = Source(time, TimeSpan.FromSeconds(30));
        Assert.True(await source.IsIssueSharedAsync(_servable1, default));

        _scope = new ShareScope { Mode = ShareMode.None };
        Assert.True(await source.IsIssueSharedAsync(_servable1, default)); // still cached

        source.Invalidate();
        Assert.False(await source.IsIssueSharedAsync(_servable1, default)); // narrowed scope now applies

        _scope = new ShareScope { Mode = ShareMode.All };
        time.Advance(TimeSpan.FromSeconds(31));
        Assert.True(await source.IsIssueSharedAsync(_servable1, default)); // TTL expiry rebuilds
    }
}
