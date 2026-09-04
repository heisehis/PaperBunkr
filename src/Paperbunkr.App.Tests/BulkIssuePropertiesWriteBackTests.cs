using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Bulk-editor counterpart to <see cref="IssuePropertiesWriteBackTests"/> - confirms the per-issue
/// write-back enqueue in <see cref="BulkIssuePropertiesScreenViewModel.Save"/> fires only for
/// issues whose file-mapped content actually changed, not for every issue the bulk edit touched
/// (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md).
/// </summary>
public class BulkIssuePropertiesWriteBackTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _issueAId;
    private readonly int _issueBId;
    private readonly List<int> _enqueued = new();

    public BulkIssuePropertiesWriteBackTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulkwriteback_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();

        // A has no Genre yet - the bulk "add Superhero" below is a real change for it.
        var issueA = new Issue { SeriesId = series.Id, Number = "1", FilePath = @"C:\comics\a.cbz" };
        // B already has "Superhero" - adding it again in the bulk edit is a no-op for B.
        var issueB = new Issue { SeriesId = series.Id, Number = "2", FilePath = @"C:\comics\b.cbz" };
        issueB.MergeFrom(IssueTagField.Genre, new[] { "Superhero" });
        context.Issues.AddRange(issueA, issueB);
        context.SaveChanges();
        _issueAId = issueA.Id;
        _issueBId = issueB.Id;
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

    private BulkIssuePropertiesScreenViewModel CreateViewModel() =>
        new(() => { }, () => new PaperbunkrDbContext(_dbOptions), notify: null, history: null, enqueueMetadataWriteBack: _enqueued.Add);

    [Fact]
    public void Save_BulkAddGenre_EnqueuesOnlyTheIssueThatActuallyChanged()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });
        var genreField = Assert.Single(vm.MainFields, f => f.Descriptor.Label == "Genre");
        genreField.Value = "Superhero";

        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { _issueAId }, _enqueued);
    }

    [Fact]
    public void Save_NothingStaged_EnqueuesNothing()
    {
        var vm = CreateViewModel();
        vm.Load(new[] { _issueAId, _issueBId });

        vm.SaveCommand.Execute(null);

        Assert.Empty(_enqueued);
    }
}
