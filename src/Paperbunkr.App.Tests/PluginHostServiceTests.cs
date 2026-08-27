using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Plugins;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PluginHostService.SetCommandEnabled"/>'s persistence (docs/superpowers/
/// specs/2026-08-24-plugin-api-v2-design.md §3) against a temp SQLite file, same
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> seam as <see cref="BackupServiceTests"/>.
/// Doesn't go through <see cref="PluginHostService.Initialize"/> (that needs a live MainWindow) -
/// constructs a throwaway <see cref="CSharpCommand"/> directly, matching the sparse-table
/// convention <see cref="PluginCommandState"/>'s doc comment describes.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PluginHostServiceTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public PluginHostServiceTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_plugin_host_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); }
        catch (IOException) { }
    }

    private static CSharpCommand MakeCommand(bool enabled) => new()
    {
        PluginKey = "sample",
        Hook = "Startup",
        Key = "sample.cmd",
        Name = "Sample",
        Enabled = enabled,
        ScriptPath = "unused.csx",
    };

    [Fact]
    public void SetCommandEnabled_writes_a_new_override_row_when_none_exists()
    {
        var host = new PluginHostService();
        var command = MakeCommand(enabled: true);

        host.SetCommandEnabled(command, enabled: false);

        Assert.False(command.Enabled);
        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var row = Assert.Single(context.PluginCommandStates);
        Assert.Equal("sample", row.PluginKey);
        Assert.Equal("sample.cmd", row.CommandKey);
        Assert.False(row.Enabled);
    }

    [Fact]
    public void SetCommandEnabled_updates_the_existing_row_instead_of_duplicating_it()
    {
        var host = new PluginHostService();
        var command = MakeCommand(enabled: true);

        host.SetCommandEnabled(command, enabled: false);
        host.SetCommandEnabled(command, enabled: true);

        using var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var row = Assert.Single(context.PluginCommandStates);
        Assert.True(row.Enabled);
    }
}
