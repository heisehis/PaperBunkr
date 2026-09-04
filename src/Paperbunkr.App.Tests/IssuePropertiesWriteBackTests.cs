using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the Issue Properties Editor's decision about whether a Save should enqueue a file
/// metadata write-back (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md).
/// <see cref="MetadataFileWriteBackServiceTests"/> / <see cref="MetadataWriteBackQueueTests"/>
/// cover the write + queue in isolation; this covers the snapshot-compare guard in
/// <see cref="IssuePropertiesScreenViewModel.Save"/> via an injected fake enqueue callback.
/// </summary>
public class IssuePropertiesWriteBackTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _issueId;
    private readonly List<int> _enqueued = new();

    public IssuePropertiesWriteBackTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_issuepropswriteback_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = @"C:\comics\test.cbz" };
        issue.MergeFrom(IssueTagField.Genre, new[] { "Original Genre" });
        context.Issues.Add(issue);
        context.SaveChanges();
        _issueId = issue.Id;
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

    private IssuePropertiesScreenViewModel CreateViewModel() =>
        new(() => { }, () => new PaperbunkrDbContext(_dbOptions), notify: null, history: null, enqueueMetadataWriteBack: _enqueued.Add);

    [Fact]
    public void Save_ChangedComicInfoField_EnqueuesWriteBack()
    {
        var vm = CreateViewModel();
        vm.Load(_issueId);
        vm.Summary = "A brand new summary.";

        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { _issueId }, _enqueued);
    }

    [Fact]
    public void Save_ChangedGenreValue_EnqueuesWriteBack()
    {
        var vm = CreateViewModel();
        vm.Load(_issueId);
        vm.Genre = "New Genre";

        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { _issueId }, _enqueued);
    }

    [Fact]
    public void Save_NoFileMappedFieldChanged_DoesNotEnqueue()
    {
        var vm = CreateViewModel();
        vm.Load(_issueId);
        // Save without touching anything.

        vm.SaveCommand.Execute(null);

        Assert.Empty(_enqueued);
    }

    [Fact]
    public void Save_CategoryWeightOnlyChange_EnqueuesForSidecar()
    {
        var vm = CreateViewModel();
        vm.Load(_issueId);
        var row = Assert.Single(vm.GenreTagRows);
        row.Weight = IssueTagWeight.Core; // flat Genre CSV unchanged; sidecar content changes.

        vm.SaveCommand.Execute(null);

        Assert.Equal(new[] { _issueId }, _enqueued);
    }
}
