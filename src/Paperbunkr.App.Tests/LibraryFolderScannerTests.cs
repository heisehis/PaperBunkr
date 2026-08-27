using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="LibraryFolderScanner"/> (docs/superpowers/specs/
/// 2026-08-07-preferences-libraries-tab-design.md §2) against real synthetic .cbz fixtures
/// (<see cref="CbzFixture"/>, same "generate via the real code path" precedent used throughout
/// this codebase). Filename-parsing correctness itself is <c>ComicNameInfo</c>'s own already-ported
/// CE concern - these tests focus on the new glue: extension filtering, idempotent re-scan, and
/// series find-or-create.
/// </summary>
public class LibraryFolderScannerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _scanRoot;

    public LibraryFolderScannerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_scanner_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _scanRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_scanner_root_{Guid.NewGuid():N}");
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

    private LibraryFolderScanner CreateScanner() => new(() => new PaperbunkrDbContext(_dbOptions));

    private void AddWatchedFolder(string path)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.WatchedFolders.Add(new WatchedFolder { Path = path });
        context.SaveChanges();
    }

    [Fact]
    public async Task ScanAllAsync_CreatesSeriesAndIssue_FromParsedFilename()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesAdded);
        Assert.Equal(1, result.SeriesTouched);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal("Kilo Station", series.Name);
        var issue = Assert.Single(context.Issues.Include(i => i.MetadataProposals));
        // No embedded ComicInfo.xml - Number/Year land as MetadataProposal rows (docs/superpowers/
        // specs/2026-08-17-metadata-model-phase2a-metadata-proposals-design.md), not direct field
        // writes. Default policy is Automatic, so they're already Accepted and Effective* surfaces
        // them exactly like the pre-existing direct-write behavior did.
        Assert.Null(issue.Number);
        Assert.Null(issue.Year);
        Assert.Equal("12", issue.EffectiveNumber());
        Assert.Equal(2021, issue.EffectiveYear());
        Assert.Equal(series.Id, issue.SeriesId);

        var numberProposal = Assert.Single(issue.MetadataProposals, p => p.Field == MetadataProposalField.Number);
        Assert.Equal("12", numberProposal.ProposedValue);
        Assert.Equal(MetadataProposalSource.FilenameParser, numberProposal.Source);
        Assert.Equal(MetadataProposalStatus.Accepted, numberProposal.Status);
        Assert.NotNull(numberProposal.ResolvedAt);
    }

    [Fact]
    public async Task ScanAllAsync_PromptPolicy_CreatesPendingProposal_EffectiveValueStaysNullUntilAccepted()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().MetadataResolutionPolicy = MetadataResolutionPolicy.Prompt;
            context.SaveChanges();
        }

        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context2 = new PaperbunkrDbContext(_dbOptions);
        var issue = Assert.Single(context2.Issues.Include(i => i.MetadataProposals));
        Assert.Null(issue.EffectiveNumber()); // Pending proposal contributes nothing
        var numberProposal = Assert.Single(issue.MetadataProposals, p => p.Field == MetadataProposalField.Number);
        Assert.Equal(MetadataProposalStatus.Pending, numberProposal.Status);
        Assert.Null(numberProposal.ResolvedAt);
    }

    [Fact]
    public async Task ScanAllAsync_ReScanning_IsIdempotent_NoDuplicates()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);
        var scanner = CreateScanner();
        await scanner.ScanAllAsync(new Progress<(int, int)>());

        var second = await scanner.ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(0, second.IssuesAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.Issues);
    }

    [Fact]
    public async Task ScanAllAsync_MultipleNewIssuesSameSeries_ShareOneSeriesRow()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 002 (2020).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(2, result.IssuesAdded);
        Assert.Equal(1, result.SeriesTouched);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.Series);
        Assert.Equal(2, context.Issues.Count());
    }

    [Fact]
    public async Task ScanAllAsync_ExistingSeries_AddsIssueToIt_NotADuplicateSeries()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.Add(new Series { Name = "Kilo Station" });
            context.SaveChanges();
        }

        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 003 (2022).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(verify.Series);
        Assert.Single(verify.Issues);
    }

    [Fact]
    public async Task ScanAllAsync_IgnoresUnsupportedExtensions()
    {
        File.WriteAllText(Path.Combine(_scanRoot, "notes.txt"), "hello");
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(0, result.IssuesAdded);
    }

    [Fact]
    public async Task ScanAllAsync_ReportsProgress()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);
        var reports = new List<(int Done, int Total)>();

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>(reports.Add));

        Assert.Equal((0, 1), reports.First());
        Assert.Equal((1, 1), reports.Last());
    }

    [Fact]
    public async Task ScanAllAsync_EmbeddedComicInfo_WinsOverMisleadingFilename()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "Real Series",
            Number = "3",
            Volume = 2,
            Year = 2023,
            Writer = "Real Writer",
            Publisher = "Real Publisher",
        };
        // Series name deliberately matches the embedded value here (docs/superpowers/specs/
        // 2026-08-17-metadata-model-phase2b-series-reassignment-design.md) - a real, deliberately
        // mismatched filename Series is covered by this file's dedicated Series-reassignment tests
        // instead; mixing that trigger into this test (whose actual focus is Number/Volume/Year/
        // Writer/Publisher) would make the two features' behaviors hard to tell apart at a glance.
        CbzFixture.Create(Path.Combine(_scanRoot, "Real Series 099 (1999).cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal("Real Series", series.Name);
        var issue = Assert.Single(context.Issues.Include(i => i.MetadataProposals));
        Assert.Equal("3", issue.Number);
        Assert.Equal("2", issue.Volume);
        Assert.Equal(2023, issue.Year);
        Assert.Equal("Real Writer", issue.Writer);
        Assert.Equal("Real Publisher", issue.Publisher);
        // Embedded metadata is already authoritative-by-source - no proposal needed for a field
        // that already has a real, trusted value (design doc's explicit scope note).
        Assert.Empty(issue.MetadataProposals);
    }

    [Fact]
    public async Task ScanAllAsync_EmbeddedComicInfoMissingAField_FallsBackToFilenameForThatFieldOnly()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "Real Series",
            // Number/Volume/Year deliberately left unset - filename parsing should fill these in.
        };
        // Series name matches the embedded value - see the comment on
        // ScanAllAsync_EmbeddedComicInfo_WinsOverMisleadingFilename for why a real Series mismatch
        // isn't mixed into this test.
        CbzFixture.Create(Path.Combine(_scanRoot, "Real Series 007 (2018).cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal("Real Series", series.Name); // embedded won
        var issue = Assert.Single(context.Issues.Include(i => i.MetadataProposals));
        Assert.Equal("7", issue.EffectiveNumber()); // filename fallback, via an Accepted proposal
        Assert.Equal(2018, issue.EffectiveYear()); // filename fallback, via an Accepted proposal
    }

    [Fact]
    public async Task ScanAllAsync_MalformedComicInfoXml_FallsBackToFilenameParsing()
    {
        string path = Path.Combine(_scanRoot, "Kilo Station 005 (2019).cbz");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            var page = zip.CreateEntry("page_000.png");
            using (var pageStream = page.Open())
            {
                using var bitmap = new System.Drawing.Bitmap(16, 16);
                bitmap.Save(pageStream, System.Drawing.Imaging.ImageFormat.Png);
            }

            var infoEntry = zip.CreateEntry("ComicInfo.xml");
            using var infoStream = infoEntry.Open();
            byte[] garbage = "<not-valid-xml this is broken"u8.ToArray();
            infoStream.Write(garbage, 0, garbage.Length);
        }
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal("Kilo Station", series.Name);
        var issue = Assert.Single(context.Issues.Include(i => i.MetadataProposals));
        Assert.Equal("5", issue.EffectiveNumber());
        Assert.Equal(2019, issue.EffectiveYear());
    }

    [Fact]
    public async Task SyncMetadataAsync_FillsBlankFields_PreservesExistingOnes()
    {
        string cbzPath = Path.Combine(_scanRoot, "already-linked.cbz");
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Writer = "Embedded Writer",
            Publisher = "Embedded Publisher",
        };
        CbzFixture.Create(cbzPath, pageCount: 1, embedded);

        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Existing Series" };
            context.Series.Add(series);
            var issue = new Issue { Series = series, FilePath = cbzPath, Writer = "Existing Writer" };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }

        var result = await CreateScanner().SyncMetadataAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesUpdated);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        var updated = verify.Issues.Single(i => i.Id == issueId);
        Assert.Equal("Existing Writer", updated.Writer); // preserved
        Assert.Equal("Embedded Publisher", updated.Publisher); // filled in
    }

    [Fact]
    public async Task SyncMetadataAsync_MissingFile_SkippedGracefully()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Existing Series" };
            context.Series.Add(series);
            context.Issues.Add(new Issue { Series = series, FilePath = Path.Combine(_scanRoot, "does-not-exist.cbz") });
            context.SaveChanges();
        }

        var result = await CreateScanner().SyncMetadataAsync(new Progress<(int, int)>());

        Assert.Equal(0, result.IssuesUpdated);
    }

    [Fact]
    public async Task SyncMetadataAsync_ReRunAfterFullSync_ReportsNoFurtherUpdates()
    {
        string cbzPath = Path.Combine(_scanRoot, "already-linked.cbz");
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo { Writer = "Embedded Writer" };
        CbzFixture.Create(cbzPath, pageCount: 1, embedded);

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Existing Series" };
            context.Series.Add(series);
            context.Issues.Add(new Issue { Series = series, FilePath = cbzPath });
            context.SaveChanges();
        }

        var scanner = CreateScanner();
        var first = await scanner.SyncMetadataAsync(new Progress<(int, int)>());
        var second = await scanner.SyncMetadataAsync(new Progress<(int, int)>());

        Assert.Equal(1, first.IssuesUpdated);
        Assert.Equal(0, second.IssuesUpdated);
    }

    // ===================== Manga/ContentType scan-time detection (docs/superpowers/specs/
    // 2026-08-16-manga-content-type-classification-design.md §4) =====================

    [Fact]
    public async Task ScanAllAsync_EmbeddedMangaYesAndRightToLeft_NewSeries_SetsContentTypeAndReadingMode()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "One Piece",
            Manga = cYo.Projects.ComicRack.Engine.MangaYesNo.YesAndRightToLeft,
        };
        CbzFixture.Create(Path.Combine(_scanRoot, "One Piece 001.cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal(ContentType.Manga, series.ContentType);
        Assert.Equal(ReadingMode.RightToLeft, series.ReadingMode);
    }

    /// <summary>
    /// Real bug, caught while writing this test: a naive "isNewSeries" implementation could be
    /// bypassed if the guard were based on ContentType's own value instead of series novelty - this
    /// asserts the actual guarding behavior directly (an existing, already-classified series stays
    /// untouched by a later scan, regardless of what its ContentType happens to be).
    /// </summary>
    [Fact]
    public async Task ScanAllAsync_EmbeddedManga_ExistingSeries_NeverOverwritesItsContentType()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.Add(new Series { Name = "One Piece", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight });
            context.SaveChanges();
        }

        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "One Piece",
            Manga = cYo.Projects.ComicRack.Engine.MangaYesNo.YesAndRightToLeft,
        };
        CbzFixture.Create(Path.Combine(_scanRoot, "One Piece 001.cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var verify = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(verify.Series);
        Assert.Equal(ContentType.Comic, series.ContentType);
        Assert.Equal(ReadingMode.LeftToRight, series.ReadingMode);
    }

    [Fact]
    public async Task ScanAllAsync_NoEmbeddedManga_NewSeries_LeavesContentTypeAtItsDefault()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal(ContentType.Unknown, series.ContentType); // Series.ContentType's own property initializer
    }

    // ===================== LanguageISO content-type heuristic (docs/superpowers/specs/
    // 2026-08-23-language-iso-content-type-heuristic-design.md) =====================

    [Fact]
    public async Task ScanAllAsync_LanguageIsoJapanese_NoEmbeddedManga_NewSeries_SetsContentTypeMangaAndRightToLeft()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "One Piece",
            LanguageISO = "ja",
        };
        CbzFixture.Create(Path.Combine(_scanRoot, "One Piece 001.cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal(ContentType.Manga, series.ContentType);
        Assert.Equal(ReadingMode.RightToLeft, series.ReadingMode);
    }

    [Fact]
    public async Task ScanAllAsync_LanguageIsoKorean_NewSeries_SetsContentTypeManhwaAndWebtoon()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "Solo Leveling",
            LanguageISO = "ko",
        };
        CbzFixture.Create(Path.Combine(_scanRoot, "Solo Leveling 001.cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal(ContentType.Manhwa, series.ContentType);
        Assert.Equal(ReadingMode.Webtoon, series.ReadingMode);
    }

    /// <summary>The embedded Manga field is a deliberate classification; LanguageISO is only an
    /// inference. Confirms the field wins even when it disagrees with what LanguageISO would imply.</summary>
    [Fact]
    public async Task ScanAllAsync_EmbeddedMangaFieldPresent_LanguageIsoIgnored()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "One Piece",
            Manga = cYo.Projects.ComicRack.Engine.MangaYesNo.No,
            LanguageISO = "ja",
        };
        CbzFixture.Create(Path.Combine(_scanRoot, "One Piece 001.cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal(ContentType.Comic, series.ContentType);
        Assert.Equal(ReadingMode.LeftToRight, series.ReadingMode);
    }

    [Fact]
    public async Task ScanAllAsync_LanguageIsoHeuristic_ExistingSeries_NeverOverwritesItsContentType()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.Add(new Series { Name = "One Piece", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight });
            context.SaveChanges();
        }

        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "One Piece",
            LanguageISO = "ja",
        };
        CbzFixture.Create(Path.Combine(_scanRoot, "One Piece 001.cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var verify = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(verify.Series);
        Assert.Equal(ContentType.Comic, series.ContentType);
        Assert.Equal(ReadingMode.LeftToRight, series.ReadingMode);
    }

    // --- Series-reassignment proposals (docs/superpowers/specs/2026-08-17-metadata-model-phase2b-
    // series-reassignment-design.md) ---

    [Fact]
    public async Task ScanAllAsync_AutomaticPolicy_SeriesMismatch_AttachesToFilenameSeries_CreatesAcceptedProposal()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo { Series = "Embedded Name", Number = "1" };
        CbzFixture.Create(Path.Combine(_scanRoot, "Filename Name 001 (2020).cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        // Default policy is Automatic - the issue is attached to the filename-derived series, not
        // the embedded-derived one, and no "Embedded Name" series is ever created at all.
        var series = Assert.Single(context.Series);
        Assert.Equal("Filename Name", series.Name);
        var issue = Assert.Single(context.Issues.Include(i => i.MetadataProposals));
        Assert.Equal(series.Id, issue.SeriesId);

        var proposal = Assert.Single(issue.MetadataProposals, p => p.Field == MetadataProposalField.Series);
        Assert.Equal("Embedded Name", proposal.CurrentValue);
        Assert.Equal("Filename Name", proposal.ProposedValue);
        Assert.Equal(MetadataProposalStatus.Accepted, proposal.Status);
        Assert.NotNull(proposal.ResolvedAt);
    }

    [Fact]
    public async Task ScanAllAsync_PromptPolicy_SeriesMismatch_StaysOnEmbeddedSeries_CreatesPendingProposal()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.GetOrCreateAppSettings().MetadataResolutionPolicy = MetadataResolutionPolicy.Prompt;
            context.SaveChanges();
        }

        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo { Series = "Embedded Name", Number = "1" };
        CbzFixture.Create(Path.Combine(_scanRoot, "Filename Name 001 (2020).cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context2 = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context2.Series);
        Assert.Equal("Embedded Name", series.Name); // stays on embedded, same as pre-2b behavior
        var issue = Assert.Single(context2.Issues.Include(i => i.MetadataProposals));
        Assert.Equal(series.Id, issue.SeriesId);

        var proposal = Assert.Single(issue.MetadataProposals, p => p.Field == MetadataProposalField.Series);
        Assert.Equal(MetadataProposalStatus.Pending, proposal.Status);
        Assert.Null(proposal.ResolvedAt);
    }

    [Fact]
    public async Task ScanAllAsync_SeriesNamesMatch_NoProposalCreated()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo { Series = "Kilo Station", Number = "1" };
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 001 (2020).cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var issue = Assert.Single(context.Issues.Include(i => i.MetadataProposals));
        Assert.DoesNotContain(issue.MetadataProposals, p => p.Field == MetadataProposalField.Series);
    }

    [Fact]
    public async Task ScanAllAsync_NoEmbeddedInfo_NoSeriesProposalCreated()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var issue = Assert.Single(context.Issues.Include(i => i.MetadataProposals));
        Assert.DoesNotContain(issue.MetadataProposals, p => p.Field == MetadataProposalField.Series);
    }
}
