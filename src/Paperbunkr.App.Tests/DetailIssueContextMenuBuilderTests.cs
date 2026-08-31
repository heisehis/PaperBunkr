using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="DetailIssueContextMenuBuilder"/> (docs/superpowers/specs/2026-08-31-
/// keyboard-operability-design.md) - the comic Detail screen's issue-tile menu, which turned out to
/// have an already-dead <c>IssueContextMenu</c> resource (found during a broader sweep) rather than
/// no menu at all as first assumed - this builder ports that resource's full content plus the one
/// genuinely new "Open in Reader" entry. Same DB-redirect fixture shape as
/// <see cref="MangaDetailContextMenuBuilderTests"/> since <see cref="DetailTabsViewModel"/>'s public
/// constructor resolves its own DB context.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailIssueContextMenuBuilderTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public DetailIssueContextMenuBuilderTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_detailissuectx_test_{Guid.NewGuid():N}.db");
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

    private static DetailTabsViewModel CreateViewModel() =>
        new(goToProperties: _ => { }, goToBulkProperties: _ => { });

    [Fact]
    public void Build_IssueCard_ReturnsExpectedEntriesInOrder()
    {
        var vm = CreateViewModel();
        var builder = new DetailIssueContextMenuBuilder(vm);
        var issue = new IssueCardSample { Id = 1, Title = "#1", CoverBrush = Brushes.Gray, FilePath = @"C:\comics\issue1.cbz" };

        var entries = builder.Build(issue);

        Assert.NotNull(entries);
        var headers = entries!.Select(e => e.IsSeparator ? null : e.Header).ToList();
        Assert.Equal(
            new[] { "Edit Properties", "Open in Reader", "Show in Explorer", null, "Mark as Read", "Mark as Unread", "Quick Rate…", null, "Set Cover…", "Reset Cover" },
            headers);
        Assert.Same(vm.EditIssuePropertiesCommand, entries[0].Command);
        Assert.Same(issue, entries[0].CommandParameter);
    }

    [Fact]
    public void Build_IssueCardWithNoFile_ShowInExplorerIsDisabled()
    {
        var builder = new DetailIssueContextMenuBuilder(CreateViewModel());
        var issue = new IssueCardSample { Id = 1, Title = "#1", CoverBrush = Brushes.Gray };

        var entries = builder.Build(issue);

        Assert.False(entries!.Single(e => e.Header == "Show in Explorer").IsEnabled);
    }

    [Fact]
    public void Build_UnrecognizedTarget_ReturnsNull()
    {
        var builder = new DetailIssueContextMenuBuilder(CreateViewModel());

        Assert.Null(builder.Build(new object()));
        Assert.Null(builder.Build(null));
    }
}
