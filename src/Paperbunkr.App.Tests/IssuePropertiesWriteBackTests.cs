using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises the Issue Properties Editor's CBZ ComicInfo.xml write-back trigger (docs/superpowers/
/// specs/2026-08-23-weighted-categorized-tags-design.md) - <see cref="ComicInfoWriteBackServiceTests"/>
/// already covers the write itself in isolation; this covers the *decision* Save makes about
/// whether to fire it at all. The trigger is real fire-and-forget background work
/// (<see cref="IssuePropertiesScreenViewModel.TriggerComicInfoWriteBack"/>), so these poll a bounded
/// window rather than asserting synchronously - same idiom as <c>LiveFolderWatchServiceTests</c>'s
/// <c>WaitUntilAsync</c>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class IssuePropertiesWriteBackTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_writeback_trigger_{Guid.NewGuid():N}.cbz");
    private readonly int _issueId;

    public IssuePropertiesWriteBackTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_issuepropswriteback_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzPath };
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
            if (File.Exists(_cbzPath)) File.Delete(_cbzPath);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The background write-back runs on a real thread-pool <c>Task.Run</c>, but its completion
    /// notification goes through <c>Dispatcher.UIThread.Post</c> (production needs that marshaling -
    /// <c>ShowToast</c> ultimately touches UI). This headless test host (<see cref="TestAppBuilder"/>,
    /// <c>SetupWithoutStarting</c>) never pumps that queue on its own, so each poll tick also drains
    /// it via <c>RunJobs()</c> - same idiom already established in
    /// <c>ReaderScreenViewModelTests.LoadIssue_GeneratesThumbnailsForEveryPage_NoneLeftNull</c>.
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        // Matches ReaderScreenViewModelTests' guard - an `await` can resume this method on a
        // different thread-pool thread than the one the headless Dispatcher bound to (re-checked
        // every iteration, not cached - which thread resumes after `await Task.Delay` can vary
        // iteration to iteration), and RunJobs() throws (not a no-op) when called off that thread.
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

    /// <summary>Returns null (not a throw) while the file is transiently unreadable - mid-swap
    /// during the export's write-to-temp-then-rename, or simply not created yet.</summary>
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
    public async Task Save_ChangedGenreValue_RewritesTheRealCbzFile()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1, new ComicInfo { Genre = "Original Genre" });
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_issueId);
        vm.Genre = "New Genre";

        vm.SaveCommand.Execute(null);

        bool updated = await WaitUntilAsync(() => ReadBack(_cbzPath)?.Genre == "New Genre", TimeSpan.FromSeconds(5));
        Assert.True(updated, "Expected the CBZ file's embedded Genre to update after Save.");
    }

    [Fact]
    public async Task Save_CategoryWeightOnlyChange_DoesNotTriggerAWriteBack()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1, new ComicInfo { Genre = "Original Genre" });
        var notifications = new List<(string Title, string Message)>();
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions), (t, m) => notifications.Add((t, m)));
        vm.Load(_issueId);
        // Genre text itself is untouched - only the per-tag Weight changes.
        var row = Assert.Single(vm.GenreTagRows);
        row.Weight = IssueTagWeight.Core;

        vm.SaveCommand.Execute(null);

        // Delete the file out from under a would-be write-back so any incorrectly-triggered attempt
        // fails fast and loud (via notify) rather than silently succeeding with identical content -
        // a same-content rewrite wouldn't be distinguishable from "correctly skipped" otherwise.
        File.Delete(_cbzPath);
        bool anyNotification = await WaitUntilAsync(() => notifications.Count > 0, TimeSpan.FromMilliseconds(600));
        Assert.False(anyNotification, "A Category/Weight-only edit should never trigger a file write-back.");
    }

    [Fact]
    public async Task Save_ChangedGenreValue_ButFileMissing_NotifiesFailure()
    {
        // No CbzFixture.Create - the file at _cbzPath never exists, so a real trigger attempt fails.
        var notifications = new List<(string Title, string Message)>();
        var vm = new IssuePropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions), (t, m) => notifications.Add((t, m)));
        vm.Load(_issueId);
        vm.Genre = "New Genre";

        vm.SaveCommand.Execute(null);

        bool notified = await WaitUntilAsync(() => notifications.Count > 0, TimeSpan.FromSeconds(5));
        Assert.True(notified, "Expected a failure notification once the background write-back attempt ran.");
        Assert.Equal("Couldn't update the file", notifications[0].Title);
    }
}
