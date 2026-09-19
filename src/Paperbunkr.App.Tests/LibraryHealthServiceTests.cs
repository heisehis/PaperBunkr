using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="LibraryHealthService"/> (docs/superpowers/specs/2026-09-06-missing-files-
/// library-health-design.md) against real files on disk, so the <c>File.Exists</c> check is genuine
/// rather than mocked - same "generate/delete real fixtures" precedent as
/// <see cref="LibraryFolderScannerTests"/>.
/// </summary>
public class LibraryHealthServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _root;

    public LibraryHealthServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_libraryhealth_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_libraryhealth_root_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private LibraryHealthService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    private int AddIssue(string fileName, bool createFile = true, int missingVerificationCount = 0, bool missingAcknowledged = false)
    {
        string path = Path.Combine(_root, fileName);
        if (createFile)
        {
            File.WriteAllText(path, "fixture");
        }

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Test Series" };
        var issue = new Issue
        {
            Series = series,
            Number = "1",
            FilePath = path,
            MissingVerificationCount = missingVerificationCount,
            MissingAcknowledged = missingAcknowledged,
        };
        context.Series.Add(series);
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public async Task Verify_FlagsMissingFile_IncrementsCount()
    {
        int issueId = AddIssue("gone.cbz", createFile: false);

        var result = await CreateService().VerifyAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.Checked);
        Assert.Equal(1, result.MissingNow);
        Assert.Equal(0, result.ConfirmedMissingCount);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var issue = context.Issues.Find(issueId)!;
        Assert.True(issue.FileIsMissing);
        Assert.Equal(1, issue.MissingVerificationCount);
    }

    [Fact]
    public async Task Verify_ClearsFlagAndResetsCount_WhenFileReappears()
    {
        int issueId = AddIssue("comic.cbz", createFile: true, missingVerificationCount: 3);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Issues.Find(issueId)!.FileIsMissing = true;
            context.SaveChanges();
        }

        await CreateService().VerifyAsync(new Progress<(int, int)>());

        using var verify = new PaperbunkrDbContext(_dbOptions);
        var issue = verify.Issues.Find(issueId)!;
        Assert.False(issue.FileIsMissing);
        Assert.Equal(0, issue.MissingVerificationCount);
    }

    [Fact]
    public async Task Verify_ReachesConfirmedThreshold_AfterTwoConsecutivePasses()
    {
        int issueId = AddIssue("gone.cbz", createFile: false);
        var service = CreateService();

        var first = await service.VerifyAsync(new Progress<(int, int)>());
        Assert.Equal(0, first.ConfirmedMissingCount);

        var second = await service.VerifyAsync(new Progress<(int, int)>());
        Assert.Equal(1, second.ConfirmedMissingCount);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(2, context.Issues.Find(issueId)!.MissingVerificationCount);
    }

    [Fact]
    public async Task Verify_RespectsConfiguredThreshold_NotJustTheDefault()
    {
        int issueId = AddIssue("gone.cbz", createFile: false);
        var service = CreateService();
        service.ConfirmedMissingThreshold = 3;

        var first = await service.VerifyAsync(new Progress<(int, int)>());
        var second = await service.VerifyAsync(new Progress<(int, int)>());
        Assert.Equal(0, second.ConfirmedMissingCount); // would be 1 at the default threshold of 2

        var third = await service.VerifyAsync(new Progress<(int, int)>());
        Assert.Equal(1, third.ConfirmedMissingCount);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(3, context.Issues.Find(issueId)!.MissingVerificationCount);
    }

    [Fact]
    public async Task Verify_RespectsMissingAcknowledged_ExcludesFromConfirmedCount()
    {
        AddIssue("gone.cbz", createFile: false, missingVerificationCount: 5, missingAcknowledged: true);

        var result = await CreateService().VerifyAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.MissingNow);
        Assert.Equal(0, result.ConfirmedMissingCount);
    }

    [Fact]
    public async Task ScopedVerifyAsync_OnlyChecksGivenIssueIds()
    {
        int scopedId = AddIssue("scoped-gone.cbz", createFile: false);
        int otherId = AddIssue("other-gone.cbz", createFile: false);

        var result = await CreateService().VerifyAsync(new[] { scopedId }, new Progress<(int, int)>());

        Assert.Equal(1, result.Checked);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.Issues.Find(scopedId)!.FileIsMissing);
        Assert.False(context.Issues.Find(otherId)!.FileIsMissing);
    }

    // ===================== Empty Rows: content-empty probe (docs/superpowers/specs/2026-09-17-
    // series-name-matching-and-empty-row-cleanup-design.md) - piggybacks on this same sweep rather
    // than a second full-library pass. =====================

    [Fact]
    public async Task Verify_FlagsContentEmpty_ForUnopenableFile()
    {
        // Plain text, not a real archive - PageDecodeCore.TryOpenProvider can't open it at all.
        int issueId = AddIssue("corrupt.cbz");

        var result = await CreateService().VerifyAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.ContentEmptyNow);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.True(context.Issues.Find(issueId)!.IsContentEmpty);
        Assert.False(context.Issues.Find(issueId)!.FileIsMissing); // present, just unreadable
    }

    [Fact]
    public async Task Verify_FlagsContentEmpty_ForZeroPageArchive()
    {
        string path = Path.Combine(_root, "zero-pages.cbz");
        CbzFixture.Create(path, pageCount: 0);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Test Series" };
            context.Series.Add(series);
            context.Issues.Add(new Issue { Series = series, Number = "1", FilePath = path });
            context.SaveChanges();
        }

        var result = await CreateService().VerifyAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.ContentEmptyNow);
    }

    [Fact]
    public async Task Verify_DoesNotFlagContentEmpty_ForHealthyArchive()
    {
        string path = Path.Combine(_root, "healthy.cbz");
        CbzFixture.Create(path, pageCount: 1);
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Test Series" };
            context.Series.Add(series);
            context.Issues.Add(new Issue { Series = series, Number = "1", FilePath = path });
            context.SaveChanges();
        }

        var result = await CreateService().VerifyAsync(new Progress<(int, int)>());

        Assert.Equal(0, result.ContentEmptyNow);
    }

    [Fact]
    public async Task Verify_DoesNotFlagContentEmpty_ForMissingFile()
    {
        // A missing file has nothing to probe - that case belongs to Missing Files, not Empty Rows.
        int issueId = AddIssue("gone.cbz", createFile: false);

        var result = await CreateService().VerifyAsync(new Progress<(int, int)>());

        Assert.Equal(0, result.ContentEmptyNow);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.False(context.Issues.Find(issueId)!.IsContentEmpty);
    }

    [Fact]
    public async Task Verify_ClearsContentEmpty_WhenFileBecomesReadable()
    {
        int issueId = AddIssue("was-corrupt.cbz"); // garbage text, unopenable
        await CreateService().VerifyAsync(new Progress<(int, int)>());
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            Assert.True(context.Issues.Find(issueId)!.IsContentEmpty);
        }

        // Same path, now a real readable archive.
        string path = Path.Combine(_root, "was-corrupt.cbz");
        File.Delete(path);
        CbzFixture.Create(path, pageCount: 1);

        await CreateService().VerifyAsync(new Progress<(int, int)>());

        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.False(verify.Issues.Find(issueId)!.IsContentEmpty);
    }

    // ===================== Scanning: missing-file handling (docs/superpowers/specs/2026-09-06-
    // scan-missing-file-handling-design.md) - the drive-reachability guard the unattended auto-
    // remove-on-scan path needs and the manual "Remove All Confirmed Missing" button doesn't. =====================

    [Fact]
    public void IsPathRootReachable_ExistingRoot_ReturnsTrue()
    {
        string path = Path.Combine(_root, "comic.cbz");
        Assert.True(LibraryHealthService.IsPathRootReachable(path));
    }

    [Fact]
    public void IsPathRootReachable_NonexistentDriveLetter_ReturnsFalse()
    {
        // "?:" is not a valid drive letter on any real Windows install - stands in for "unplugged".
        Assert.False(LibraryHealthService.IsPathRootReachable(@"?:\comics\gone.cbz"));
    }

    [Fact]
    public void IsPathRootReachable_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(LibraryHealthService.IsPathRootReachable(null));
        Assert.False(LibraryHealthService.IsPathRootReachable(""));
    }
}
