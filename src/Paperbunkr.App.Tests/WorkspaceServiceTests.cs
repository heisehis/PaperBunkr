using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="WorkspaceService"/> CRUD, ordering, built-in guards, and idempotent seeding
/// (docs/superpowers/specs/2026-09-03-library-saved-workspaces-design.md).
/// </summary>
public class WorkspaceServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_workspacesvc_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        _service = new WorkspaceService(() => new PaperbunkrDbContext(_dbOptions));
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

    [Fact]
    public void EnsureBuiltInsSeeded_CreatesTheStarters_ForEachScreen()
    {
        _service.EnsureBuiltInsSeeded();

        var library = _service.List(WorkspaceScreen.Library);
        Assert.Equal(new[] { "All comics", "Currently reading", "Manga" }, library.Select(w => w.Name));
        Assert.All(library, w => Assert.True(w.IsBuiltIn));

        var books = _service.List(WorkspaceScreen.Books);
        Assert.Equal(new[] { "All books", "Recently added", "By series" }, books.Select(w => w.Name));
    }

    [Fact]
    public void EnsureBuiltInsSeeded_IsIdempotent_AndLeavesUserRowsAlone()
    {
        _service.EnsureBuiltInsSeeded();
        var userRow = _service.Create(WorkspaceScreen.Library, "Manga", WorkspaceStateJson.Serialize(new LibraryWorkspaceState()));

        _service.EnsureBuiltInsSeeded();
        _service.EnsureBuiltInsSeeded();

        var library = _service.List(WorkspaceScreen.Library);
        Assert.Equal(3, library.Count(w => w.IsBuiltIn));
        Assert.Single(library, w => !w.IsBuiltIn && w.Id == userRow.Id);
        // A user's identically-named "Manga" neither blocks the built-in nor is touched.
        Assert.Equal(2, library.Count(w => w.Name == "Manga"));
    }

    [Fact]
    public void List_OrdersBuiltInsFirst_ThenBySortOrder()
    {
        _service.EnsureBuiltInsSeeded();
        var b = _service.Create(WorkspaceScreen.Library, "B", "{}");
        var a = _service.Create(WorkspaceScreen.Library, "A", "{}");
        _service.Reorder(WorkspaceScreen.Library, new[] { a.Id, b.Id });

        var names = _service.List(WorkspaceScreen.Library).Select(w => w.Name).ToList();

        Assert.Equal(new[] { "All comics", "Currently reading", "Manga", "A", "B" }, names);
    }

    [Fact]
    public void BuiltInGuards_RejectRenameUpdateDelete()
    {
        _service.EnsureBuiltInsSeeded();
        var manga = _service.List(WorkspaceScreen.Library).Single(w => w.Name == "Manga");

        _service.Rename(manga.Id, "Renamed");
        _service.UpdateState(manga.Id, "{\"ViewMode\":\"List\"}");
        _service.Delete(manga.Id);

        var after = _service.List(WorkspaceScreen.Library).Single(w => w.Id == manga.Id);
        Assert.Equal("Manga", after.Name);
        Assert.Equal(manga.StateJson, after.StateJson);
    }

    [Fact]
    public void UserWorkspace_CanBeRenamedUpdatedDeleted()
    {
        var w = _service.Create(WorkspaceScreen.Books, "Mine", "{}");

        _service.Rename(w.Id, "Mine v2");
        _service.UpdateState(w.Id, "{\"GroupField\":\"Series\"}");
        var updated = _service.List(WorkspaceScreen.Books).Single();
        Assert.Equal("Mine v2", updated.Name);
        Assert.Contains("Series", updated.StateJson);

        _service.Delete(w.Id);
        Assert.Empty(_service.List(WorkspaceScreen.Books));
    }

    [Fact]
    public void Reorder_IgnoresBuiltInsAndForeignScreenIds()
    {
        _service.EnsureBuiltInsSeeded();
        var manga = _service.List(WorkspaceScreen.Library).Single(w => w.Name == "Manga");
        var booksRow = _service.Create(WorkspaceScreen.Books, "BooksOne", "{}");
        var one = _service.Create(WorkspaceScreen.Library, "One", "{}");

        // Built-in id + a Books id mixed in must not throw or mis-order the one real Library user row.
        _service.Reorder(WorkspaceScreen.Library, new[] { manga.Id, booksRow.Id, one.Id });

        Assert.Equal("One", _service.List(WorkspaceScreen.Library).Last().Name);
        Assert.Equal("Manga", _service.List(WorkspaceScreen.Library).Single(w => w.Id == manga.Id).Name);
    }
}
