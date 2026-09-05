using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="LiveFolderWatchService"/> (docs/superpowers/specs/
/// 2026-08-23-live-folder-watch-scanning-design.md) against a real <see cref="FileSystemWatcher"/>
/// watching a real temp directory, with a short debounce/retry window (the test-only ctor seam) so
/// the suite doesn't have to sleep for the production 2-second window per case - same "generate via
/// the real code path" precedent <see cref="LibraryFolderScannerTests"/> already established.
/// </summary>
public class LiveFolderWatchServiceTests : IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _scanRoot;

    public LiveFolderWatchServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_watch_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _scanRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_watch_root_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scanRoot);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_scanRoot)) Directory.Delete(_scanRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private LiveFolderWatchService CreateService(Action<string, string>? showToast = null, Action? onLibraryChanged = null, Action<int>? onFilesMissing = null, Action<IReadOnlyList<int>>? onFilesImported = null)
    {
        return new LiveFolderWatchService(
            () => new PaperbunkrDbContext(_dbOptions),
            new LibraryFolderScanner(() => new PaperbunkrDbContext(_dbOptions)),
            showToast ?? ((_, _) => { }),
            onLibraryChanged ?? (() => { }),
            DebounceWindow,
            RetryDelay,
            onFilesMissing,
            onFilesImported);
    }

    private void AddWatchedFolder(string path, bool watch)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.WatchedFolders.Add(new WatchedFolder { Path = path, Watch = watch });
        context.SaveChanges();
    }

    private int IssueCount()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Issues.Count();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }

    [Fact]
    public async Task Created_InWatchedFolder_ImportsNewIssue_WithoutExplicitScan()
    {
        AddWatchedFolder(_scanRoot, watch: true);
        using var service = CreateService();
        service.Start();

        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz"), pageCount: 1);

        bool imported = await WaitUntilAsync(() => IssueCount() == 1, PollTimeout);

        Assert.True(imported, "Expected the live watcher to import the new file without an explicit scan call.");
    }

    [Fact]
    public async Task Created_InUnwatchedFolder_IsNotImported()
    {
        AddWatchedFolder(_scanRoot, watch: false);
        using var service = CreateService();
        service.Start();

        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz"), pageCount: 1);

        // Confirms the per-folder toggle actually gates watching, not just scanning - wait past
        // several debounce cycles, then assert nothing landed.
        await Task.Delay(DebounceWindow + DebounceWindow);
        Assert.Equal(0, IssueCount());
    }

    [Fact]
    public async Task Created_FileDuplicatingExisting_InvokesOnFilesImported_WithAddedIds()
    {
        AddWatchedFolder(_scanRoot, watch: true);

        int existingIssueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Kilo Station" };
            context.Series.Add(series);
            var issue = new Issue { Series = series, Number = "12", FilePath = @"C:\lib\kilo012_existing.cbz" };
            context.Issues.Add(issue);
            context.SaveChanges();
            existingIssueId = issue.Id;
        }

        IReadOnlyList<int>? imported = null;
        using var service = CreateService(onFilesImported: ids => imported = ids);
        service.Start();

        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz"), pageCount: 1);

        bool notified = await WaitUntilAsync(() => imported is not null, PollTimeout);
        Assert.True(notified, "Expected the import to invoke onFilesImported once the new file was added.");

        using var verifyContext = new PaperbunkrDbContext(_dbOptions);
        int newIssueId = verifyContext.Issues.Single(i => i.Id != existingIssueId).Id;
        Assert.Equal(new[] { newIssueId }, imported);
    }

    [Fact]
    public async Task Renamed_File_UpdatesFilePath_WithoutDuplicateOrProposal()
    {
        AddWatchedFolder(_scanRoot, watch: true);
        using var service = CreateService();
        service.Start();

        string originalPath = Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz");
        CbzFixture.Create(originalPath, pageCount: 1);
        Assert.True(await WaitUntilAsync(() => IssueCount() == 1, PollTimeout));

        string renamedPath = Path.Combine(_scanRoot, "Kilo Station 012 (2021) [renamed].cbz");
        File.Move(originalPath, renamedPath);

        bool renamed = await WaitUntilAsync(
            () =>
            {
                using var context = new PaperbunkrDbContext(_dbOptions);
                return context.Issues.Any(i => i.FilePath == renamedPath);
            },
            PollTimeout);
        Assert.True(renamed, "Expected the rename to update Issue.FilePath in place.");

        using var finalContext = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(1, finalContext.Issues.Count());
        var issue = Assert.Single(finalContext.Issues.Include(i => i.MetadataProposals));
        Assert.Empty(issue.MetadataProposals.Where(p => p.Field == MetadataProposalField.Series));
        Assert.False(issue.FileIsMissing);
    }

    [Fact]
    public async Task Renamed_Folder_PrefixUpdatesAllContainedIssuePaths()
    {
        AddWatchedFolder(_scanRoot, watch: true);
        using var service = CreateService();
        service.Start();

        string subFolder = Path.Combine(_scanRoot, "Series A");
        Directory.CreateDirectory(subFolder);
        CbzFixture.Create(Path.Combine(subFolder, "Series A 001.cbz"), pageCount: 1);
        CbzFixture.Create(Path.Combine(subFolder, "Series A 002.cbz"), pageCount: 1);
        Assert.True(await WaitUntilAsync(() => IssueCount() == 2, PollTimeout));

        string renamedFolder = Path.Combine(_scanRoot, "Series A Renamed");
        Directory.Move(subFolder, renamedFolder);

        bool renamed = await WaitUntilAsync(
            () =>
            {
                using var context = new PaperbunkrDbContext(_dbOptions);
                return context.Issues.All(i => i.FilePath!.StartsWith(renamedFolder));
            },
            PollTimeout);
        Assert.True(renamed, "Expected both issues' FilePath to be prefix-updated to the renamed folder.");
    }

    [Fact]
    public async Task Deleted_WatchedFile_SetsFileIsMissing_LeavesAcknowledgedUntouched()
    {
        AddWatchedFolder(_scanRoot, watch: true);
        using var service = CreateService();
        service.Start();

        string path = Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz");
        CbzFixture.Create(path, pageCount: 1);
        Assert.True(await WaitUntilAsync(() => IssueCount() == 1, PollTimeout));

        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            issueId = context.Issues.Single().Id;
        }

        File.Delete(path);

        bool missing = await WaitUntilAsync(
            () =>
            {
                using var context = new PaperbunkrDbContext(_dbOptions);
                return context.Issues.Single(i => i.Id == issueId).FileIsMissing;
            },
            PollTimeout);
        Assert.True(missing, "Expected the deleted file's Issue to be flagged FileIsMissing - closes the pre-existing gap where nothing ever set this for a natively-scanned issue.");

        using var finalContext = new PaperbunkrDbContext(_dbOptions);
        Assert.False(finalContext.Issues.Single(i => i.Id == issueId).MissingAcknowledged);
    }

    [Fact]
    public async Task Deleted_WatchedFile_RaisesOnFilesMissing_WithCount()
    {
        AddWatchedFolder(_scanRoot, watch: true);
        int? reportedCount = null;
        using var service = CreateService(onFilesMissing: count => reportedCount = count);
        service.Start();

        string path = Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz");
        CbzFixture.Create(path, pageCount: 1);
        Assert.True(await WaitUntilAsync(() => IssueCount() == 1, PollTimeout));

        File.Delete(path);

        bool notified = await WaitUntilAsync(() => reportedCount is not null, PollTimeout);
        Assert.True(notified, "Expected the deletion flush to invoke onFilesMissing so the caller can surface an alert.");
        Assert.Equal(1, reportedCount);
    }

    [Fact]
    public async Task Renamed_PreviouslyMissingFile_ClearsFileIsMissing()
    {
        AddWatchedFolder(_scanRoot, watch: true);

        string originalPath = Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz");
        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Kilo Station" };
            context.Series.Add(series);
            var issue = new Issue { Series = series, FilePath = originalPath, FileIsMissing = true };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }

        using var service = CreateService();
        service.Start();

        CbzFixture.Create(originalPath, pageCount: 1);
        string renamedPath = Path.Combine(_scanRoot, "Kilo Station 012 (2021) [renamed].cbz");
        File.Move(originalPath, renamedPath);

        bool cleared = await WaitUntilAsync(
            () =>
            {
                using var context = new PaperbunkrDbContext(_dbOptions);
                var issue = context.Issues.Single(i => i.Id == issueId);
                return issue.FilePath == renamedPath && !issue.FileIsMissing;
            },
            PollTimeout);
        Assert.True(cleared, "Expected the rename to both update FilePath and clear a stale FileIsMissing flag.");
    }

    [Fact]
    public async Task RapidCreation_OfSeveralFiles_ProducesExactlyOneToast()
    {
        AddWatchedFolder(_scanRoot, watch: true);
        int toastCount = 0;
        using var service = CreateService(showToast: (_, _) => Interlocked.Increment(ref toastCount));
        service.Start();

        CbzFixture.Create(Path.Combine(_scanRoot, "Series A 001.cbz"), pageCount: 1);
        CbzFixture.Create(Path.Combine(_scanRoot, "Series A 002.cbz"), pageCount: 1);
        CbzFixture.Create(Path.Combine(_scanRoot, "Series A 003.cbz"), pageCount: 1);

        Assert.True(await WaitUntilAsync(() => IssueCount() == 3, PollTimeout));

        // Give any (incorrect) extra flush a chance to happen before asserting the count is final.
        await Task.Delay(DebounceWindow + DebounceWindow);
        Assert.Equal(1, toastCount);
    }

    [Fact]
    public async Task Created_FileLockedThenReleased_IsImportedOnceReadable()
    {
        AddWatchedFolder(_scanRoot, watch: true);
        using var service = CreateService();
        service.Start();

        string path = Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz");
        CbzFixture.Create(path, pageCount: 1);

        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Held just long enough to make the watcher's first read-ready attempt(s) fail, well
            // inside its ~5-attempt retry budget at RetryDelay each - simulates "Created fired while
            // a large file was still being written."
            await Task.Delay(RetryDelay + RetryDelay);
        }

        bool imported = await WaitUntilAsync(() => IssueCount() == 1, PollTimeout);
        Assert.True(imported, "Expected the file to be imported once it became readable, within the retry window.");
    }
}
