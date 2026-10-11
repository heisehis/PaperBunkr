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
/// Storage for list layouts (docs/superpowers/specs/2026-10-04-list-layouts-design.md): named layouts and the per-list
/// rows, plus the layout record's tolerant JSON.
/// </summary>
public class ListLayoutServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly ListLayoutService _service;

    public ListLayoutServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_listlayoutsvc_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        _service = new ListLayoutService(() => new PaperbunkrDbContext(_dbOptions));
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
    public void SaveNamed_AppendsInOrder_AndIsPerScreen()
    {
        _service.SaveNamed(WorkspaceScreen.Library, "Wide", "{\"a\":1}");
        _service.SaveNamed(WorkspaceScreen.Library, "Narrow", "{\"a\":2}");
        _service.SaveNamed(WorkspaceScreen.Books, "By author", "{}");

        Assert.Equal(new[] { "Wide", "Narrow" }, _service.ListNamed(WorkspaceScreen.Library).Select(l => l.Name));
        Assert.Equal(new[] { "By author" }, _service.ListNamed(WorkspaceScreen.Books).Select(l => l.Name));
    }

    [Fact]
    public void SaveNamed_SameNameAnyCase_ReplacesInPlace()
    {
        var first = _service.SaveNamed(WorkspaceScreen.Library, "Wide", "{\"a\":1}");
        _service.SaveNamed(WorkspaceScreen.Library, "Other", "{}");
        var again = _service.SaveNamed(WorkspaceScreen.Library, " wide ", "{\"a\":9}");

        Assert.Equal(first.Id, again.Id);
        var rows = _service.ListNamed(WorkspaceScreen.Library);
        Assert.Equal(2, rows.Count);
        Assert.Equal("{\"a\":9}", rows[0].StateJson);
        Assert.Equal("Wide", rows[0].Name);
    }

    [Fact]
    public void Rename_RefusesBlankAndTakenNames()
    {
        var a = _service.SaveNamed(WorkspaceScreen.Library, "A", "{}");
        _service.SaveNamed(WorkspaceScreen.Library, "B", "{}");

        Assert.False(_service.Rename(a.Id, "  "));
        Assert.False(_service.Rename(a.Id, "b"));
        Assert.True(_service.Rename(a.Id, "C"));
        Assert.Equal(new[] { "C", "B" }, _service.ListNamed(WorkspaceScreen.Library).Select(l => l.Name));
    }

    [Fact]
    public void Reorder_And_Delete()
    {
        var a = _service.SaveNamed(WorkspaceScreen.Library, "A", "{}");
        var b = _service.SaveNamed(WorkspaceScreen.Library, "B", "{}");
        var c = _service.SaveNamed(WorkspaceScreen.Library, "C", "{}");

        _service.Reorder(WorkspaceScreen.Library, new[] { c.Id, a.Id, b.Id });
        Assert.Equal(new[] { "C", "A", "B" }, _service.ListNamed(WorkspaceScreen.Library).Select(l => l.Name));

        _service.Delete(a.Id);
        Assert.Equal(new[] { "C", "B" }, _service.ListNamed(WorkspaceScreen.Library).Select(l => l.Name));
    }

    [Fact]
    public void Assignment_SetGetClear_IsKeyedByScreenAndList()
    {
        Assert.Null(_service.GetAssignment(WorkspaceScreen.Library, "all"));

        _service.SetAssignment(WorkspaceScreen.Library, "all", "{\"x\":1}");
        _service.SetAssignment(WorkspaceScreen.Library, "collection:4", "{\"x\":2}");
        _service.SetAssignment(WorkspaceScreen.Books, "all", "{\"x\":3}");
        _service.SetAssignment(WorkspaceScreen.Library, "all", "{\"x\":4}");

        Assert.Equal("{\"x\":4}", _service.GetAssignment(WorkspaceScreen.Library, "all"));
        Assert.Equal("{\"x\":2}", _service.GetAssignment(WorkspaceScreen.Library, "collection:4"));
        Assert.Equal("{\"x\":3}", _service.GetAssignment(WorkspaceScreen.Books, "all"));

        _service.ClearAssignment(WorkspaceScreen.Library, "collection:4");
        Assert.Null(_service.GetAssignment(WorkspaceScreen.Library, "collection:4"));
    }

    [Fact]
    public void SetOnAllLists_ReplacesTheDefaultAndDropsEveryListsOwnRow_OnThatScreenOnly()
    {
        _service.SetAssignment(WorkspaceScreen.Library, ListLayoutAssignment.DefaultKey, "{\"old\":1}");
        _service.SetAssignment(WorkspaceScreen.Library, "all", "{\"x\":1}");
        _service.SetAssignment(WorkspaceScreen.Library, "content:Manga", "{\"x\":2}");
        _service.SetAssignment(WorkspaceScreen.Books, "all", "{\"b\":1}");

        _service.SetOnAllLists(WorkspaceScreen.Library, "{\"new\":1}");

        Assert.Equal("{\"new\":1}", _service.GetAssignment(WorkspaceScreen.Library, ListLayoutAssignment.DefaultKey));
        Assert.Null(_service.GetAssignment(WorkspaceScreen.Library, "all"));
        Assert.Null(_service.GetAssignment(WorkspaceScreen.Library, "content:Manga"));
        Assert.Equal("{\"b\":1}", _service.GetAssignment(WorkspaceScreen.Books, "all"));
    }

    // --- starter layouts ---

    private static System.Collections.Generic.IReadOnlyList<(string Name, string StateJson)> Templates =>
        ListLayoutTemplates.Library.Select(t => (t.Name, ListLayoutStateJson.Serialize(t.State))).ToList();

    [Fact]
    public void Templates_AreSeededOnce_AndADeletedOneStaysDeleted()
    {
        _service.EnsureTemplatesSeeded(WorkspaceScreen.Library, Templates);
        var seeded = _service.ListNamed(WorkspaceScreen.Library);
        Assert.Equal(5, seeded.Count);
        Assert.Equal(ListLayoutTemplates.Library.Select(t => t.Name), seeded.Select(l => l.Name));

        _service.Delete(seeded[0].Id);
        _service.EnsureTemplatesSeeded(WorkspaceScreen.Library, Templates);

        Assert.Equal(4, _service.ListNamed(WorkspaceScreen.Library).Count);
        Assert.Empty(_service.ListNamed(WorkspaceScreen.Books));
    }

    [Fact]
    public void Templates_LeaveAUsersOwnLayoutOfTheSameNameAlone_AndSurviveSetOnAllLists()
    {
        _service.SaveNamed(WorkspaceScreen.Library, "cover wall", "{\"mine\":1}");

        _service.EnsureTemplatesSeeded(WorkspaceScreen.Library, Templates);
        _service.SetOnAllLists(WorkspaceScreen.Library, "{}");
        _service.EnsureTemplatesSeeded(WorkspaceScreen.Library, Templates);

        var rows = _service.ListNamed(WorkspaceScreen.Library);
        Assert.Equal(5, rows.Count);
        Assert.Equal("{\"mine\":1}", rows.Single(l => l.Name == "cover wall").StateJson);
    }

    [Fact]
    public void Templates_AreValidLayouts_ThatSurviveNormalizing()
    {
        foreach (var (name, state) in ListLayoutTemplates.Library)
        {
            var back = ListLayoutStateJson.Deserialize(ListLayoutStateJson.Serialize(state));

            Assert.Equal(state.Columns?.Count, back.Columns?.Count);
            Assert.Equal(state.TileElements, back.TileElements);
            Assert.Equal(state.ViewMode, back.ViewMode);
            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }

    // --- the layout record's JSON ---

    [Fact]
    public void State_RoundTrips()
    {
        var state = new ListLayoutState(
            Columns: new[] { new ListLayoutColumn(IssueListSortField.Title, true, 300), new ListLayoutColumn(IssueListSortField.Writer, false, 90) },
            ViewMode: LibraryViewMode.DetailsTable,
            CoverFit: LibraryGridCoverFit.Tiles,
            CaptionFields: new[] { IssueListSortField.Publisher, IssueListSortField.Year },
            HideCaptions: true,
            TileElements: new[] { TileTextElement.Title, TileTextElement.Writer },
            SortField: IssueListSortField.Title,
            SortDirection: SortDirection.Ascending,
            GroupField: IssueListGroupField.Publisher);

        var back = ListLayoutStateJson.Deserialize(ListLayoutStateJson.Serialize(state));

        Assert.Equal(state.Columns, back.Columns);
        Assert.Equal(LibraryViewMode.DetailsTable, back.ViewMode);
        Assert.Equal(LibraryGridCoverFit.Tiles, back.CoverFit);
        Assert.Equal(state.CaptionFields, back.CaptionFields);
        Assert.True(back.HideCaptions);
        Assert.Equal(state.TileElements, back.TileElements);
        Assert.Equal(IssueListSortField.Title, back.SortField);
        Assert.Equal(SortDirection.Ascending, back.SortDirection);
        Assert.Equal(IssueListGroupField.Publisher, back.GroupField);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void State_CorruptOrEmpty_IsTheDefaults(string? json)
    {
        var state = ListLayoutStateJson.Deserialize(json);

        Assert.Null(state.Columns);
        Assert.Null(state.CaptionFields);
        Assert.Null(state.TileElements);
        Assert.Equal(LibraryViewMode.PosterGrid, state.ViewMode);
    }

    [Fact]
    public void State_UnknownKeysAreIgnored_AndMissingOnesDefault()
    {
        var state = ListLayoutStateJson.Deserialize("{\"ViewMode\":\"List\",\"SomethingNew\":5}");

        Assert.Equal(LibraryViewMode.List, state.ViewMode);
        Assert.Equal(IssueListSortField.Added, state.SortField);
    }

    [Fact]
    public void Normalize_DropsDuplicateColumns_ClampsWidths_AndFallsBackWhenNothingIsVisible()
    {
        var state = ListLayoutStateJson.Normalize(new ListLayoutState(Columns: new[]
        {
            new ListLayoutColumn(IssueListSortField.Title, true, 5),
            new ListLayoutColumn(IssueListSortField.Title, true, 200),
            new ListLayoutColumn(IssueListSortField.Writer, true, 99999),
        }));

        Assert.Equal(2, state.Columns!.Count);
        Assert.Equal(ListLayoutStateJson.MinColumnWidth, state.Columns[0].Width);
        Assert.Equal(ListLayoutStateJson.MaxColumnWidth, state.Columns[1].Width);

        var hidden = ListLayoutStateJson.Normalize(new ListLayoutState(Columns: new[] { new ListLayoutColumn(IssueListSortField.Title, false, 100) }));
        Assert.Null(hidden.Columns);
    }

    [Fact]
    public void Normalize_KeepsAtMostThreeCaptionLines()
    {
        var state = ListLayoutStateJson.Normalize(new ListLayoutState(CaptionFields: new[]
        {
            IssueListSortField.Title, IssueListSortField.Writer, IssueListSortField.Publisher, IssueListSortField.Year,
        }));

        Assert.Equal(ListLayoutState.MaxCaptionLines, state.CaptionFields!.Count);
    }
}
