using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="MangaDetailContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-
/// keyboard-operability-design.md) - the ported chapter-row menu, formerly dead (a plain
/// <c>ContextMenu</c> element that never renders in this Avalonia build). Same DB-redirect fixture
/// shape as <see cref="MangaDetailScreenViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MangaDetailContextMenuBuilderTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public MangaDetailContextMenuBuilderTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mangadetailctx_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static MangaDetailScreenViewModel CreateViewModel() =>
        new(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });

    [Fact]
    public void Build_ChapterRow_ReturnsExpectedEntriesInOrder()
    {
        var builder = new MangaDetailContextMenuBuilder(CreateViewModel());
        var row = new ChapterRowSample { Id = 1, DisplayNumber = "1" };

        var entries = builder.Build(row);

        Assert.NotNull(entries);
        var headers = entries!.Select(e => e.IsSeparator ? null : e.Header).ToList();
        Assert.Equal(
            new[] { "Edit Properties", "Show in Explorer", null, "Mark as Read", "Mark as Unread", null, "Set Cover…", "Reset Cover" },
            headers);
    }

    [Fact]
    public void Build_ChapterRow_CommandsCarryTheRowAsParameter()
    {
        var vm = CreateViewModel();
        var builder = new MangaDetailContextMenuBuilder(vm);
        var row = new ChapterRowSample { Id = 42, DisplayNumber = "5" };

        var entries = builder.Build(row);

        var editEntry = entries!.First(e => e.Header == "Edit Properties");
        Assert.Same(vm.EditChapterPropertiesCommand, editEntry.Command);
        Assert.Same(row, editEntry.CommandParameter);
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var builder = new MangaDetailContextMenuBuilder(CreateViewModel());

        var entries = builder.Build(new object());

        Assert.Null(entries);
    }

    [Fact]
    public void Build_NullTarget_ReturnsNull()
    {
        var builder = new MangaDetailContextMenuBuilder(CreateViewModel());

        var entries = builder.Build(null);

        Assert.Null(entries);
    }
}
