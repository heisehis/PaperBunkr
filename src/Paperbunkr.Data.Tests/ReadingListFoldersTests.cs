using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests;

/// <summary><see cref="ReadingListFolders"/> (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §1).</summary>
public class ReadingListFoldersTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_folders_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public ReadingListFoldersTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = NewContext();
        context.Database.EnsureCreated();
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

    private PaperbunkrDbContext NewContext() => new(_options);

    private int AddFolder(string name, int? parent = null)
    {
        using var context = NewContext();
        var folder = ReadingListFolders.Create(context, name, parent);
        context.SaveChanges();
        return folder.Id;
    }

    private int AddList(string name, int? folder = null)
    {
        using var context = NewContext();
        var list = new ReadingList { Name = name };
        context.ReadingLists.Add(list);
        ReadingListFolders.PlaceNewList(context, list, folder);
        context.SaveChanges();
        return list.Id;
    }

    private IReadOnlyList<string> Names(int? folder)
    {
        using var context = NewContext();
        return ReadingListFolders.Children(context, folder).Select(c => (c.IsFolder ? "F:" : "L:") + c.Name).ToList();
    }

    [Fact]
    public void Children_PutFoldersFirst_InSortOrder()
    {
        AddList("Saga");
        AddFolder("Crisis");
        AddList("Avengers");
        AddFolder("X-Men");

        Assert.Equal(new[] { "F:Crisis", "F:X-Men", "L:Saga", "L:Avengers" }, Names(null));
    }

    [Fact]
    public void PlaceNewList_AppendsInsideTheFolder_AndFallsBackToTopLevelForAMissingFolder()
    {
        int crisis = AddFolder("Crisis");
        AddList("One", crisis);
        AddList("Two", crisis);
        AddList("Stray", 9999);

        Assert.Equal(new[] { "L:One", "L:Two" }, Names(crisis));
        Assert.Contains("L:Stray", Names(null));
        using var context = NewContext();
        Assert.Equal(new[] { 0, 1 }, context.ReadingLists.Where(l => l.FolderId == crisis).OrderBy(l => l.SortOrder).Select(l => l.SortOrder));
    }

    [Fact]
    public void Move_RefusesAMoveIntoItselfOrADescendant()
    {
        int a = AddFolder("A");
        int b = AddFolder("B", a);
        int c = AddFolder("C", b);

        using var context = NewContext();
        Assert.False(ReadingListFolders.Move(context, a, a, 0));
        Assert.False(ReadingListFolders.Move(context, a, c, 0));
        Assert.True(ReadingListFolders.Move(context, c, null, 0));
        context.SaveChanges();

        Assert.Equal(new[] { "F:C", "F:A" }, Names(null));
        Assert.Empty(Names(b));
    }

    [Fact]
    public void MoveList_ReordersWithinAFolder_AndRenumbersTheOldOne()
    {
        int f = AddFolder("F");
        int one = AddList("One");
        AddList("Two");
        AddList("Three");

        using (var context = NewContext())
        {
            Assert.True(ReadingListFolders.MoveList(context, one, f, 0));
            context.SaveChanges();
        }

        Assert.Equal(new[] { "L:One" }, Names(f));
        using (var context = NewContext())
        {
            Assert.Equal(new[] { 0, 1 }, context.ReadingLists.Where(l => l.FolderId == null).OrderBy(l => l.SortOrder).Select(l => l.SortOrder));
            int three = context.ReadingLists.Single(l => l.Name == "Three").Id;
            ReadingListFolders.MoveList(context, three, null, 0);
            context.SaveChanges();
        }

        Assert.Equal(new[] { "F:F", "L:Three", "L:Two" }, Names(null));
    }

    [Fact]
    public void Delete_MovesContentsUpToTheParent_AfterWhatItAlreadyHolds_AndKeepsEveryList()
    {
        int top = AddFolder("Top");
        int doomed = AddFolder("Doomed", top);
        AddList("Existing", top);
        AddFolder("Inner", doomed);
        AddList("First", doomed);
        AddList("Second", doomed);

        using (var context = NewContext())
        {
            ReadingListFolders.Delete(context, doomed);
            context.SaveChanges();
        }

        Assert.Equal(new[] { "F:Inner", "L:Existing", "L:First", "L:Second" }, Names(top));
        using var check = NewContext();
        Assert.Equal(3, check.ReadingLists.Count());
        Assert.Null(check.ReadingListFolders.Find(doomed));
    }

    [Fact]
    public void SortAlphabetically_IgnoresLeadingArticles_FoldersBeforeLists()
    {
        AddList("The Walking Dead");
        AddList("Avengers");
        AddList("A Zebra Story");
        AddFolder("The X Folder");
        AddFolder("Batman Folder");

        using (var context = NewContext())
        {
            ReadingListFolders.SortAlphabetically(context, null);
            context.SaveChanges();
        }

        Assert.Equal(new[] { "F:Batman Folder", "F:The X Folder", "L:Avengers", "L:The Walking Dead", "L:A Zebra Story" }, Names(null));
    }

    [Fact]
    public void Create_TrimsAndDefaultsTheName_AndSetDescriptionClearsBlank()
    {
        int f = AddFolder("   ");
        using var context = NewContext();
        Assert.Equal("New folder", context.ReadingListFolders.Find(f)!.Name);
        ReadingListFolders.SetDescription(context, f, "  notes ");
        Assert.Equal("notes", context.ReadingListFolders.Find(f)!.Description);
        ReadingListFolders.SetDescription(context, f, " ");
        Assert.Null(context.ReadingListFolders.Find(f)!.Description);
    }
}
