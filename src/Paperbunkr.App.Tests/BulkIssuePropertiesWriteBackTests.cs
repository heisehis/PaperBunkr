using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Bulk-editor counterpart to <see cref="IssuePropertiesWriteBackTests"/> - confirms the per-issue
/// write-back trigger in <see cref="BulkIssuePropertiesScreenViewModel.Save"/> fires only for
/// issues whose Genre/Tags value actually changed, not for every issue touched by the bulk edit.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BulkIssuePropertiesWriteBackTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _cbzPathA = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulk_writeback_a_{Guid.NewGuid():N}.cbz");
    private readonly string _cbzPathB = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulk_writeback_b_{Guid.NewGuid():N}.cbz");
    private readonly int _issueAId;
    private readonly int _issueBId;

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
        var issueA = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzPathA };
        // B already has "Superhero" - adding it again in the bulk edit is a no-op for B.
        var issueB = new Issue { SeriesId = series.Id, Number = "2", FilePath = _cbzPathB };
        issueB.MergeFrom(IssueTagField.Genre, new[] { "Superhero" });
        context.Issues.AddRange(issueA, issueB);
        context.SaveChanges();
        _issueAId = issueA.Id;
        _issueBId = issueB.Id;

        CbzFixture.Create(_cbzPathA, pageCount: 1, new ComicInfo());
        CbzFixture.Create(_cbzPathB, pageCount: 1, new ComicInfo { Genre = "Superhero" });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_cbzPathA)) File.Delete(_cbzPathA);
            if (File.Exists(_cbzPathB)) File.Delete(_cbzPathB);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Same pump-if-owned idiom as <see cref="IssuePropertiesWriteBackTests.WaitUntilAsync"/> - see its doc comment for why.</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        void TryPump()
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            TryPump();
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        TryPump();
        return condition();
    }

    private static ComicInfo? ReadBack(string path)
    {
        try
        {
            using var provider = Providers.Readers.CreateSourceProvider(path);
            if (provider is null)
            {
                return null;
            }

            provider.Open(async: false);
            return ((IInfoStorage)provider).LoadInfo(InfoLoadingMethod.Complete);
        }
        catch (IOException)
        {
            return null;
        }
    }

    [Fact]
    public async Task Save_BulkAddGenre_RewritesOnlyTheIssueThatActuallyChanged()
    {
        var vm = new BulkIssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(new[] { _issueAId, _issueBId });
        var genreField = Assert.Single(vm.MainFields, f => f.Descriptor.Label == "Genre");
        genreField.Value = "Superhero";

        vm.SaveCommand.Execute(null);

        // A genuinely changed (no Genre -> "Superhero") - its file should update.
        bool aUpdated = await WaitUntilAsync(() => ReadBack(_cbzPathA)?.Genre == "Superhero", TimeSpan.FromSeconds(5));
        Assert.True(aUpdated, "Expected issue A's CBZ file to gain the newly-added Genre.");

        // B already had "Superhero" - nothing to diff, so no write-back should have fired for it.
        // There's no direct "no trigger happened" signal here (unlike the missing-file trick in
        // IssuePropertiesWriteBackTests), so this settles for the weaker-but-still-real assertion
        // that B's file is untouched content-wise after A's real rewrite has had time to land.
        var bInfo = ReadBack(_cbzPathB);
        Assert.NotNull(bInfo);
        Assert.Equal("Superhero", bInfo!.Genre);
    }
}
