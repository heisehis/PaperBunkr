using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Two library entries pointing at ONE file - what an organize that raced the folder watcher, a rescan or a restore can leave behind.
/// Removing one of them used to send the shared file to the Recycle Bin, so the entry that was kept lost its file. Also: removing entries but
/// keeping the file, and folding same-file entries together without touching any file.</summary>
public sealed class SharedFileRemovalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_shared_file_{Guid.NewGuid():N}");
    private readonly string _dbPath;

    public SharedFileRemovalTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private string RealFile(string name = "Red Hulk 001.cbz")
    {
        string path = Path.Combine(_root, name);
        File.WriteAllText(path, "not really a comic");
        return path;
    }

    private (int First, int Second) TwoEntriesForOneFile(string path)
    {
        using var context = NewContext();
        var series = context.Series.Add(new Series { Name = "Red Hulk" }).Entity;
        context.SaveChanges();
        var first = context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = path }).Entity;
        var second = context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = path }).Entity;
        context.SaveChanges();
        return (first.Id, second.Id);
    }

    [Fact]
    public void RemovingOneOfTwoEntriesForOneFile_LeavesTheFileAlone_AndDoesNotBlacklistItsPath()
    {
        string path = RealFile();
        var (first, second) = TwoEntriesForOneFile(path);

        using (var context = NewContext())
        {
            LibraryDeletionHelper.RemoveIssue(context, context.Issues.Find(second)!);     // deleteFile = true: the normal "Delete"
            context.SaveChanges();
        }

        Assert.True(File.Exists(path));                                                     // the kept entry's file was NOT recycled
        using var verify = NewContext();
        Assert.Equal(new[] { first }, verify.Issues.Select(i => i.Id).ToList());
        Assert.Empty(verify.RemovedFilePaths);                                              // and the path is not on the "do not re-import" list
    }

    [Fact]
    public void RemovingAnEntryButKeepingItsFile_LeavesTheFile_AndRemembersToIgnoreThePath()
    {
        string path = RealFile();
        TwoEntriesForOneFile(RealFile("Other.cbz"));      // just gives the library a series
        int lone;
        using (var context = NewContext())
        {
            var entry = context.Issues.Add(new Issue { SeriesId = context.Series.First().Id, Number = "2", FilePath = path }).Entity;
            context.SaveChanges();
            lone = entry.Id;
        }

        using (var context = NewContext())
        {
            LibraryDeletionHelper.RemoveIssue(context, context.Issues.Find(lone)!, deleteFile: false);
            context.SaveChanges();
        }

        Assert.True(File.Exists(path));
        using var verify = NewContext();
        Assert.DoesNotContain(verify.Issues, i => i.Id == lone);
        var removed = Assert.Single(verify.RemovedFilePaths);
        Assert.Equal(path, removed.FilePath);
        Assert.True(removed.KeepFile);
    }

    [Fact]
    public void RemovingASeriesKeepingTheFiles_LeavesEveryFile()
    {
        string a = RealFile("a.cbz");
        string b = RealFile("b.cbz");
        using (var seed = NewContext())
        {
            var series = seed.Series.Add(new Series { Name = "Red Hulk" }).Entity;
            seed.SaveChanges();
            seed.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = a });
            seed.Issues.Add(new Issue { SeriesId = series.Id, Number = "2", FilePath = b });
            seed.SaveChanges();
        }

        using (var context = NewContext())
        {
            var series = context.Series.Include(s => s.Issues).Single();
            LibraryDeletionHelper.RemoveSeries(context, series, deleteFile: false);
            context.SaveChanges();
        }

        Assert.True(File.Exists(a) && File.Exists(b));
        using var verify = NewContext();
        Assert.Empty(verify.Series);
        Assert.Empty(verify.Issues);
        Assert.All(verify.RemovedFilePaths, r => Assert.True(r.KeepFile));
        Assert.Equal(2, verify.RemovedFilePaths.Count());
    }

    [Fact]
    public void TheMerge_FoldsSameFileEntriesIntoOne_KeepingTheOneThatIsInAList_AndTouchesNoFile()
    {
        string path = RealFile();
        var (first, second) = TwoEntriesForOneFile(path);
        using (var seed = NewContext())
        {
            var list = new ReadingList { Name = "Arc", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            list.Items.Add(new ReadingListItem { IssueId = second, SortOrder = 0 });          // the SECOND entry is the one in a list
            seed.ReadingLists.Add(list);
            seed.Issues.Find(first)!.LastPageRead = 0;
            seed.Issues.Find(second)!.LastPageRead = 7;
            seed.SaveChanges();
        }

        SamePathMergeResult result;
        using (var context = NewContext())
        {
            result = SamePathEntryMerger.Merge(context);
        }

        Assert.Equal(new SamePathMergeResult(1, 1), result);
        Assert.True(File.Exists(path));
        using var verify = NewContext();
        var kept = Assert.Single(verify.Issues);
        Assert.Equal(second, kept.Id);                                                       // the entry that was in a list survives
        Assert.Equal(kept.Id, Assert.Single(verify.ReadingListItems).IssueId);
        Assert.Empty(verify.RemovedFilePaths);
        Assert.Contains("Merged 1 extra entry", result.ToString());
    }

    [Fact]
    public void TheMerge_HandsMembershipsAndProgressToTheKeptEntry_WhenTheKeeperLacksThem()
    {
        string path = RealFile();
        var (first, second) = TwoEntriesForOneFile(path);
        using (var seed = NewContext())
        {
            // Both entries are in list A (the keeper by being in MORE lists); only the doomed one is in list B and has progress.
            var a = new ReadingList { Name = "A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            a.Items.Add(new ReadingListItem { IssueId = first, SortOrder = 0 });
            a.Items.Add(new ReadingListItem { IssueId = second, SortOrder = 1 });
            var b = new ReadingList { Name = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            b.Items.Add(new ReadingListItem { IssueId = first, SortOrder = 0 });
            var c = new ReadingList { Name = "C", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            c.Items.Add(new ReadingListItem { IssueId = first, SortOrder = 0 });
            seed.ReadingLists.AddRange(a, b, c);
            var doomed = seed.Issues.Find(second)!;
            doomed.LastPageRead = 9;
            doomed.Rating = 4.5f;
            seed.SaveChanges();
        }

        using (var context = NewContext())
        {
            SamePathEntryMerger.Merge(context);
        }

        using var verify = NewContext();
        var kept = Assert.Single(verify.Issues);
        Assert.Equal(first, kept.Id);                                                        // in three lists vs. one
        Assert.Equal(9, kept.LastPageRead);                                                  // the furthest progress moved over
        Assert.Equal(4.5f, kept.Rating);
        Assert.Equal(3, verify.ReadingListItems.Count(i => i.IssueId == first));            // no list lost the comic and none got it twice
    }

    [Fact]
    public void TheMerge_LeavesTwoEntriesForTwoDifferentFilesAlone()
    {
        string a = RealFile("a.cbz");
        string b = RealFile("b.cbz");
        using (var seed = NewContext())
        {
            var series = seed.Series.Add(new Series { Name = "Red Hulk" }).Entity;
            seed.SaveChanges();
            seed.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = a });
            seed.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = b });
            seed.SaveChanges();
        }

        using var context = NewContext();
        var result = SamePathEntryMerger.Merge(context);

        Assert.Equal(0, result.EntriesRemoved);
        Assert.Equal("No two library entries share a file.", result.ToString());
        Assert.Equal(2, context.Issues.Count());
    }
}
