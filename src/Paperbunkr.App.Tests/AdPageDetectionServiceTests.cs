using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.AdDetection;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SkiaSharp;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="AdPageDetectionService"/> and <see cref="AdHashSeeder"/> against generated CBZ fixtures and a real
/// migrated SQLite file (docs/superpowers/specs/2026-09-21-comic-reader-page-intelligence-design.md §5). Pages are
/// synthetic block patterns from <see cref="PageHasherTests.Pattern"/>: the same seed is the same page, different
/// seeds are unrelated pages.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
[Trait("Speed", "Slow")]
public class AdPageDetectionServiceTests : IDisposable
{
    private const int AdSeed = 900;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_adscan_test_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private int _fileCounter;

    public AdPageDetectionServiceTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.Migrate();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext Ctx() => new(_options);

    private AdPageDetectionService Service() => new(() => new PaperbunkrDbContext(_options));

    private AdHashSeeder Seeder() => new(() => new PaperbunkrDbContext(_options));

    /// <summary>A CBZ whose pages are unique patterns, except the pages listed in <paramref name="adPages"/>, which show the ad.</summary>
    private string CreateCbz(int pageCount, params int[] adPages)
    {
        string path = Path.Combine(_root, $"issue_{_fileCounter++}.cbz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        int fileSalt = _fileCounter * 1000;
        for (int i = 0; i < pageCount; i++)
        {
            using var bitmap = PageHasherTests.Pattern(adPages.Contains(i) ? AdSeed : fileSalt + i);
            var entry = zip.CreateEntry($"page_{i:D3}.jpg", CompressionLevel.Fastest);
            using var stream = entry.Open();
            byte[] bytes = PageHasherTests.Encode(bitmap, SKEncodedImageFormat.Jpeg, quality: 70);
            stream.Write(bytes, 0, bytes.Length);
        }

        return path;
    }

    private int AddIssue(string? filePath, bool placeholder = false, bool missing = false)
    {
        using var context = Ctx();
        var series = new Series { Name = $"S{_fileCounter}" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = filePath, IsPlaceholder = placeholder, FileIsMissing = missing };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    /// <summary>What the reader does when the user tags a page Advertisement: the tag row (the seeder adds the hash separately).</summary>
    private void TagAsAdvertisement(int issueId, int page)
    {
        using var context = Ctx();
        context.IssuePages.Add(new IssuePage { IssueId = issueId, PageNumber = page, PageType = PageType.Advertisement });
        context.SaveChanges();
    }

    private int AddAdToLibrary()
    {
        long hash;
        using (var bitmap = PageHasherTests.Pattern(AdSeed))
        {
            hash = PageHasher.Compute(bitmap);
        }

        using var context = Ctx();
        var ad = new AdPageHash { Hash = hash, CreatedAt = DateTime.UtcNow };
        context.AdPageHashes.Add(ad);
        context.SaveChanges();
        return ad.Id;
    }

    [Fact]
    public void Scan_EmptyAdLibrary_DoesNothing()
    {
        AddIssue(CreateCbz(20, 18));

        var result = Service().Scan(null, CancellationToken.None);

        Assert.Equal(new AdScanResult(0, 0, 0, 0), result);
        using var context = Ctx();
        Assert.Empty(context.PageHashes.ToList());
    }

    [Fact]
    public void Scan_ProposesAPageThatMatchesAKnownAd()
    {
        int adId = AddAdToLibrary();
        int issueId = AddIssue(CreateCbz(20, 18));

        var result = Service().Scan(null, CancellationToken.None);

        using var context = Ctx();
        var proposal = Assert.Single(context.AdPageProposals.ToList());
        Assert.Equal(issueId, proposal.IssueId);
        Assert.Equal(18, proposal.PageNumber);
        Assert.Equal(adId, proposal.MatchedAdHashId);
        Assert.True(proposal.Distance <= AdDetectionLimits.MatchDistance);
        Assert.Equal(AdPageProposalStatus.Pending, proposal.Status);
        Assert.Equal(1, result.ProposalsCreated);
        Assert.Equal(13, result.PagesHashed); // first 3 + last 10 of 20
    }

    [Fact]
    public void Scan_OnlyLooksAtTheLeadingAndTrailingWindow()
    {
        AddAdToLibrary();
        // 20 pages: window is 0-2 and 10-19. Ad look-alikes at 2 and 10 are inside; 3, 5 and 9 are outside.
        AddIssue(CreateCbz(20, 2, 3, 5, 9, 10));

        Service().Scan(null, CancellationToken.None);

        using var context = Ctx();
        Assert.Equal(new[] { 2, 10 }, context.AdPageProposals.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToArray());
    }

    [Fact]
    public void Scan_ShortIssue_HashesEachPageOnce()
    {
        AddAdToLibrary();
        AddIssue(CreateCbz(6, 4));

        var result = Service().Scan(null, CancellationToken.None);

        Assert.Equal(6, result.PagesHashed); // windows overlap, every page counted once
        using var context = Ctx();
        Assert.Equal(6, context.PageHashes.Count());
    }

    [Fact]
    public void Scan_UnrelatedPagesAreNotProposed()
    {
        AddAdToLibrary();
        AddIssue(CreateCbz(20));

        var result = Service().Scan(null, CancellationToken.None);

        Assert.Equal(0, result.ProposalsCreated);
    }

    [Fact]
    public void Scan_UnchangedFile_IsNotHashedAgain()
    {
        AddAdToLibrary();
        AddIssue(CreateCbz(20, 18));
        var service = Service();
        service.Scan(null, CancellationToken.None);

        var second = service.Scan(null, CancellationToken.None);

        Assert.Equal(0, second.PagesHashed);
        Assert.Equal(0, second.ProposalsCreated);
        using var context = Ctx();
        Assert.Single(context.AdPageProposals.ToList());
    }

    [Fact]
    public void Scan_ChangedFile_IsHashedAgain()
    {
        AddAdToLibrary();
        string path = CreateCbz(20, 18);
        AddIssue(path);
        var service = Service();
        service.Scan(null, CancellationToken.None);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));

        var second = service.Scan(null, CancellationToken.None);

        // Page 18 already has a proposal so it is skipped; the other 12 window pages are re-hashed.
        Assert.Equal(12, second.PagesHashed);
    }

    [Fact]
    public void Scan_SkipsPagesThatAlreadyHaveATag()
    {
        AddAdToLibrary();
        int issueId = AddIssue(CreateCbz(20, 18, 19));
        using (var context = Ctx())
        {
            context.IssuePages.Add(new IssuePage { IssueId = issueId, PageNumber = 18, PageType = PageType.Deleted });
            context.SaveChanges();
        }

        Service().Scan(null, CancellationToken.None);

        using var read = Ctx();
        Assert.Equal(19, Assert.Single(read.AdPageProposals.ToList()).PageNumber);
    }

    [Fact]
    public void Scan_ARotatedButUntaggedPage_StillCountsAsACandidate()
    {
        AddAdToLibrary();
        int issueId = AddIssue(CreateCbz(20, 18));
        using (var context = Ctx())
        {
            context.IssuePages.Add(new IssuePage { IssueId = issueId, PageNumber = 18, RotationDegrees = 90 });
            context.SaveChanges();
        }

        Service().Scan(null, CancellationToken.None);

        using var read = Ctx();
        Assert.Single(read.AdPageProposals.ToList());
    }

    [Fact]
    public void Scan_ARejectedPage_IsNeverProposedAgain()
    {
        AddAdToLibrary();
        string path = CreateCbz(20, 18);
        AddIssue(path);
        var service = Service();
        service.Scan(null, CancellationToken.None);
        using (var context = Ctx())
        {
            var proposal = context.AdPageProposals.Single();
            proposal.Status = AdPageProposalStatus.Rejected;
            context.SaveChanges();
        }

        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
        var second = service.Scan(null, CancellationToken.None);

        Assert.Equal(0, second.ProposalsCreated);
        using var read = Ctx();
        Assert.Equal(AdPageProposalStatus.Rejected, Assert.Single(read.AdPageProposals.ToList()).Status);
    }

    [Fact]
    public void Scan_NeverWritesAPageTag()
    {
        AddAdToLibrary();
        AddIssue(CreateCbz(20, 18));

        Service().Scan(null, CancellationToken.None);

        using var context = Ctx();
        Assert.Empty(context.IssuePages.ToList());
    }

    [Fact]
    public void Scan_AnUnreadableFile_IsCountedAndTheRestStillScanned()
    {
        AddAdToLibrary();
        string broken = Path.Combine(_root, "broken.cbz");
        File.WriteAllBytes(broken, [1, 2, 3, 4, 5]);
        AddIssue(broken);
        AddIssue(CreateCbz(20, 18));

        var result = Service().Scan(null, CancellationToken.None);

        Assert.Equal(1, result.IssuesFailed);
        Assert.Equal(1, result.IssuesScanned);
        Assert.Equal(1, result.ProposalsCreated);
    }

    [Fact]
    public void Scan_SkipsPlaceholdersMissingFilesAndFilelessIssues()
    {
        AddAdToLibrary();
        AddIssue(CreateCbz(20, 18), placeholder: true);
        AddIssue(CreateCbz(20, 18), missing: true);
        AddIssue(null);

        var result = Service().Scan(null, CancellationToken.None);

        Assert.Equal(0, result.IssuesScanned);
        Assert.Equal(0, result.ProposalsCreated);
    }

    [Fact]
    public void Scan_CancelledBeforeItStarts_Throws()
    {
        AddAdToLibrary();
        AddIssue(CreateCbz(20, 18));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Service().Scan(null, cts.Token));
    }

    [Fact]
    public void Scan_ReportsProgressPerIssue()
    {
        AddAdToLibrary();
        AddIssue(CreateCbz(20));
        AddIssue(CreateCbz(20));
        var reports = new List<(int Done, int Total)>();

        Service().Scan(new SyncProgress<(int Done, int Total)>(reports.Add), CancellationToken.None);

        Assert.Equal((0, 2), reports.First());
        Assert.Equal((2, 2), reports.Last());
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [Fact]
    public void WindowFor_ReturnsFirstThreeAndLastTen()
    {
        Assert.Equal([0, 1, 2, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19], AdDetectionLimits.WindowFor(20));
        Assert.Equal([0, 1, 2, 3, 4], AdDetectionLimits.WindowFor(5));
        Assert.Empty(AdDetectionLimits.WindowFor(0));
    }

    // ===== Seeder =====

    [Fact]
    public void Seed_AddsTheTaggedPageToTheAdLibrary_WithItsSource()
    {
        int issueId = AddIssue(CreateCbz(10, 7));

        Seeder().Seed(issueId, 7);

        using var context = Ctx();
        var ad = Assert.Single(context.AdPageHashes.ToList());
        Assert.Equal(issueId, ad.SourceIssueId);
        Assert.Equal(7, ad.SourcePageNumber);
    }

    [Fact]
    public void Seed_ANearDuplicateOfAKnownAd_IsNotAddedAgain()
    {
        AddAdToLibrary();
        int issueId = AddIssue(CreateCbz(10, 7));

        Seeder().Seed(issueId, 7);

        using var context = Ctx();
        Assert.Single(context.AdPageHashes.ToList());
    }

    [Fact]
    public void Seed_ResolvesAPendingProposalForThatPage()
    {
        AddAdToLibrary();
        int issueId = AddIssue(CreateCbz(20, 18));
        Service().Scan(null, CancellationToken.None);

        Seeder().Seed(issueId, 18);

        using var context = Ctx();
        var proposal = Assert.Single(context.AdPageProposals.ToList());
        Assert.Equal(AdPageProposalStatus.Accepted, proposal.Status);
        Assert.NotNull(proposal.ResolvedAt);
    }

    [Fact]
    public void Unseed_RemovesTheHashThatPageSourced_AndItsPendingProposals()
    {
        int sourceIssue = AddIssue(CreateCbz(10, 7));
        TagAsAdvertisement(sourceIssue, 7);
        Seeder().Seed(sourceIssue, 7);
        int otherIssue = AddIssue(CreateCbz(20, 18));
        Service().Scan(null, CancellationToken.None);
        using (var context = Ctx())
        {
            Assert.Single(context.AdPageProposals.ToList()); // sanity: the seeded ad matched the other issue's page
        }

        Seeder().Unseed(sourceIssue, 7);

        using var read = Ctx();
        Assert.Empty(read.AdPageHashes.ToList());
        Assert.Empty(read.AdPageProposals.ToList());
        Assert.NotEqual(0, otherIssue);
    }

    [Fact]
    public void Unseed_KeepsRejectedProposals_ButClearsTheirMatch()
    {
        int sourceIssue = AddIssue(CreateCbz(10, 7));
        TagAsAdvertisement(sourceIssue, 7);
        Seeder().Seed(sourceIssue, 7);
        AddIssue(CreateCbz(20, 18));
        Service().Scan(null, CancellationToken.None);
        using (var context = Ctx())
        {
            context.AdPageProposals.Single().Status = AdPageProposalStatus.Rejected;
            context.SaveChanges();
        }

        Seeder().Unseed(sourceIssue, 7);

        using var read = Ctx();
        var proposal = Assert.Single(read.AdPageProposals.ToList());
        Assert.Equal(AdPageProposalStatus.Rejected, proposal.Status);
        Assert.Null(proposal.MatchedAdHashId);
    }

    [Fact]
    public void Unseed_APageThatSeededNothing_DoesNothing()
    {
        AddAdToLibrary();
        int issueId = AddIssue(CreateCbz(10));

        Seeder().Unseed(issueId, 3);

        using var context = Ctx();
        Assert.Single(context.AdPageHashes.ToList());
    }

    [Fact]
    public void Seed_AnUnreadableFile_DoesNotThrow()
    {
        string broken = Path.Combine(_root, "broken2.cbz");
        File.WriteAllBytes(broken, [9, 9, 9]);
        int issueId = AddIssue(broken);

        Seeder().Seed(issueId, 0);

        using var context = Ctx();
        Assert.Empty(context.AdPageHashes.ToList());
    }
}
