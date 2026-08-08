using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>CRUD + ordering for <see cref="VirtualTagDefinition"/> (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md §1.1).</summary>
public class VirtualTagDefinitionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public VirtualTagDefinitionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_virtualtags_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
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

    [Fact]
    public void AddUpdateDelete_RoundTrips()
    {
        var tag = new VirtualTagDefinition { Name = "Series + Volume", CaptionFormat = "{Series} Vol.{Volume}", SortOrder = 0 };
        _context.VirtualTagDefinitions.Add(tag);
        _context.SaveChanges();
        int id = tag.Id;

        var reloaded = _context.VirtualTagDefinitions.First(v => v.Id == id);
        reloaded.CaptionFormat = "{Series} v{Volume}";
        reloaded.IsEnabled = false;
        _context.SaveChanges();

        var afterUpdate = _context.VirtualTagDefinitions.First(v => v.Id == id);
        Assert.Equal("{Series} v{Volume}", afterUpdate.CaptionFormat);
        Assert.False(afterUpdate.IsEnabled);

        _context.VirtualTagDefinitions.Remove(afterUpdate);
        _context.SaveChanges();

        Assert.Empty(_context.VirtualTagDefinitions);
    }

    [Fact]
    public void SortOrder_Persists()
    {
        _context.VirtualTagDefinitions.AddRange(
            new VirtualTagDefinition { Name = "B", CaptionFormat = "{Series}", SortOrder = 1 },
            new VirtualTagDefinition { Name = "A", CaptionFormat = "{Series}", SortOrder = 0 });
        _context.SaveChanges();

        var ordered = _context.VirtualTagDefinitions.OrderBy(v => v.SortOrder).ToList();

        Assert.Equal("A", ordered[0].Name);
        Assert.Equal("B", ordered[1].Name);
    }
}
