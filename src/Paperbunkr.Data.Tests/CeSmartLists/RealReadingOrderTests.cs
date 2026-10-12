using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests.CeSmartLists;

/// <summary>
/// Reading-list import against reading-order files written by ComicRack itself (<c>TestData\ReadingOrders</c>, event reading orders a user supplied;
/// docs/superpowers/specs/2026-10-11-format-presets-design.md). They must still be recognised as reading lists now that a <c>.cbl</c> can also be a smart list, and keep
/// their books in order.
/// </summary>
public class RealReadingOrderTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pb_readingorders_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public RealReadingOrderTests()
    {
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "ReadingOrders", name + ".cbl");

    private List<string> ImportedOrder(string file)
    {
        CblImportResult result = CblImport.ImportAny(_context, Fixture(file));
        Assert.Equal(CblFileKind.ReadingList, result.Kind);
        Assert.Null(result.SmartList);
        int id = result.ReadingList!.Id;
        return _context.ReadingListItems
            .Where(i => i.ReadingListId == id)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.Issue!.Series!.Name + " #" + i.Issue.Number)
            .ToList();
    }

    [Theory]
    [InlineData("001 - Zatanna_s Search")]
    [InlineData("001 - Reed Richards and Sue Storm_s Wedding")]
    [InlineData("003 - The Night Gwen Stacy Died")]
    [InlineData("010 - Contest of Champions")]
    public void ARealReadingOrder_IsAReadingList_NotASmartList(string file)
    {
        Assert.Equal(CblFileKind.ReadingList, CblFile.Detect(Fixture(file)));
    }

    [Fact]
    public void AnEventAcrossSeveralSeries_KeepsItsReadingOrder()
    {
        Assert.Equal(
            ["Hawkman #4", "Detective Comics #336", "The Atom #19", "Green Lantern #42", "Detective Comics #355", "Justice League of America #51"],
            ImportedOrder("001 - Zatanna_s Search"));
    }

    [Fact]
    public void AShortEvent_ImportsItsBooksInOrder()
    {
        Assert.Equal(["The Amazing Spider-Man #121", "The Amazing Spider-Man #122"], ImportedOrder("003 - The Night Gwen Stacy Died"));
        Assert.Equal(
            ["Marvel Super Hero Contest of Champions #1", "Marvel Super Hero Contest of Champions #2", "Marvel Super Hero Contest of Champions #3"],
            ImportedOrder("010 - Contest of Champions"));
    }

    [Fact]
    public void TheListKeepsTheNameInsideTheFile_NotTheFileName()
    {
        CblImportResult result = CblImport.ImportAny(_context, Fixture("001 - Reed Richards and Sue Storm_s Wedding"));

        Assert.Equal("001 - Reed Richards and Sue Storm’s Wedding", result.ReadingList!.Name);
        Assert.Single(result.ReadingList.Items);
    }
}
