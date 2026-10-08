using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Relink suggestions and the duplicate-keeper ranking (docs/superpowers/specs/2026-10-06-smart-features-design.md §5.1, §5.2). Both
/// are pure over <see cref="Issue"/> rows, so they are tested without a database.
/// </summary>
public class LibraryHygieneResolverTests
{
    private static int _nextId = 1;

    private static Series NewSeries(string name) => new() { Id = _nextId++, Name = name };

    private static Issue Issue(Series series, string number, string path, long? size = null, int? pages = null, bool missing = false) => new()
    {
        Id = _nextId++,
        Series = series,
        SeriesId = series.Id,
        Number = number,
        FilePath = path,
        FileSize = size,
        PageCount = pages,
        FileIsMissing = missing,
    };

    // ----- Missing-file matches -----

    [Fact]
    public void Match_SameSizeAndPageCount_IsExact_EvenUnderADifferentSeriesName()
    {
        var old = NewSeries("Saga");
        var reimported = NewSeries("Saga (2012)");
        var lost = Issue(old, "13", @"D:\old\Saga 013.cbz", size: 52_000_000, pages: 28, missing: true);
        var found = Issue(reimported, "13", @"D:\new\Saga 013.cbz", size: 52_000_000, pages: 28);
        var other = Issue(reimported, "14", @"D:\new\Saga 014.cbz", size: 51_000_000, pages: 28);

        var match = Assert.Single(MissingFileMatchResolver.Find([lost], [found, other])).Value;

        Assert.Equal(MissingFileMatchTier.Exact, match.Tier);
        Assert.Equal(found.Id, match.CandidateIssueId);
        Assert.Equal(@"D:\new\Saga 013.cbz", match.CandidatePath);
    }

    [Fact]
    public void Match_SameSeriesNumberAndFormat_ButADifferentFile_IsProbable()
    {
        var old = NewSeries("Saga");
        var reimported = NewSeries("Saga");
        var lost = Issue(old, "13", @"D:\old\Saga 013.cbz", size: 52_000_000, pages: 28, missing: true);
        var rescan = Issue(reimported, "013", @"D:\new\Saga 13 (digital).cbz", size: 80_000_000, pages: 30);

        var match = Assert.Single(MissingFileMatchResolver.Find([lost], [rescan])).Value;

        Assert.Equal(MissingFileMatchTier.Probable, match.Tier);
        Assert.Equal(rescan.Id, match.CandidateIssueId);
    }

    [Fact]
    public void Match_NeedsTheSameFormatNumberAndSeries_ForTheProbableTier()
    {
        var saga = NewSeries("Saga");
        var lost = Issue(saga, "13", @"D:\old\Saga 013.cbz", missing: true);
        var wrongFormat = Issue(NewSeries("Saga"), "13", @"D:\new\Saga 013.pdf");
        var wrongNumber = Issue(NewSeries("Saga"), "14", @"D:\new\Saga 014.cbz");
        var wrongSeries = Issue(NewSeries("Monstress"), "13", @"D:\new\Monstress 013.cbz");

        Assert.Empty(MissingFileMatchResolver.Find([lost], [wrongFormat, wrongNumber, wrongSeries]));
    }

    [Fact]
    public void Match_NeverOffersAMissingPlaceholderOrPathlessEntry_NorTheEntryItself()
    {
        var saga = NewSeries("Saga");
        var lost = Issue(saga, "13", @"D:\old\Saga 013.cbz", size: 1000, pages: 20, missing: true);
        var alsoMissing = Issue(saga, "13", @"D:\x\Saga 013.cbz", size: 1000, pages: 20, missing: true);
        var placeholder = Issue(saga, "13", @"D:\y\Saga 013.cbz", size: 1000, pages: 20);
        placeholder.IsPlaceholder = true;

        Assert.Empty(MissingFileMatchResolver.Find([lost], [lost, alsoMissing, placeholder]));
    }

    [Fact]
    public void Match_APresentEntry_GoesToOnlyOneMissingEntry_AndExactMatchesAreAssignedFirst()
    {
        var saga = NewSeries("Saga");
        var probableOnly = Issue(saga, "13", @"D:\old\a\Saga 013.cbz", size: 999, pages: 20, missing: true);
        var exact = Issue(saga, "13", @"D:\old\b\Saga 013.cbz", size: 1000, pages: 20, missing: true);
        var found = Issue(saga, "13", @"D:\new\Saga 013.cbz", size: 1000, pages: 20);

        var matches = MissingFileMatchResolver.Find([probableOnly, exact], [found]);

        var match = Assert.Single(matches).Value;
        Assert.Equal(exact.Id, match.MissingIssueId);
        Assert.Equal(MissingFileMatchTier.Exact, match.Tier);
    }

    // ----- Duplicate keeper -----

    private static Issue Copy(string path, int? pages, long? size, DateTime? added = null) => new()
    {
        Id = _nextId++,
        FilePath = path,
        PageCount = pages,
        FileSize = size,
        AddedTime = added,
    };

    [Fact]
    public void Keeper_PrefersMorePages_AndSaysSo()
    {
        var shorter = Copy(@"C:\a.cbz", 24, 60_000_000);
        var longer = Copy(@"C:\b.cbz", 28, 40_000_000);

        var keeper = DuplicateKeeperRanker.Recommend([shorter, longer]);

        Assert.Equal(longer.Id, keeper!.IssueId);
        Assert.Equal("Most pages: 28 vs 24", keeper.Reason);
    }

    [Fact]
    public void Keeper_WithEqualPages_PrefersTheLargerScan()
    {
        var small = Copy(@"C:\a.cbz", 28, 20 * 1024 * 1024);
        var large = Copy(@"C:\b.cbz", 28, 60 * 1024 * 1024);

        var keeper = DuplicateKeeperRanker.Recommend([small, large]);

        Assert.Equal(large.Id, keeper!.IssueId);
        Assert.Equal("Larger pages: 60 MB vs 20 MB", keeper.Reason);
    }

    [Fact]
    public void Keeper_WithEqualPagesAndSize_PrefersTheMoreOpenFormat_ThenTheOlderCopy()
    {
        var cbr = Copy(@"C:\a.cbr", 28, 1000);
        var cbz = Copy(@"C:\b.cbz", 28, 1000);
        var byFormat = DuplicateKeeperRanker.Recommend([cbr, cbz]);
        Assert.Equal(cbz.Id, byFormat!.IssueId);
        Assert.Equal("CBZ over CBR", byFormat.Reason);

        var newer = Copy(@"C:\c.cbz", 28, 1000, new DateTime(2026, 5, 1));
        var older = Copy(@"C:\d.cbz", 28, 1000, new DateTime(2024, 5, 1));
        var byAge = DuplicateKeeperRanker.Recommend([newer, older]);
        Assert.Equal(older.Id, byAge!.IssueId);
        Assert.Equal("In your library the longest", byAge.Reason);
    }

    [Fact]
    public void Keeper_NeverPicksACopyWhoseFileIsMissingOrUnreadable_OverOneThatIsThere()
    {
        var missing = Copy(@"C:\a.cbz", 40, 90_000_000);
        missing.FileIsMissing = true;
        var present = Copy(@"C:\b.cbz", 20, 10_000_000);
        var byPresence = DuplicateKeeperRanker.Recommend([missing, present]);
        Assert.Equal(present.Id, byPresence!.IssueId);
        Assert.Equal("The other copy's file is missing", byPresence.Reason);

        var empty = Copy(@"C:\c.cbz", 40, 90_000_000);
        empty.IsContentEmpty = true;
        var readable = Copy(@"C:\d.cbz", 20, 10_000_000);
        var byContent = DuplicateKeeperRanker.Recommend([empty, readable]);
        Assert.Equal(readable.Id, byContent!.IssueId);
        Assert.Equal("The other copy cannot be read", byContent.Reason);
    }

    [Fact]
    public void Keeper_OfASingleCopy_IsNothing()
    {
        Assert.Null(DuplicateKeeperRanker.Recommend([Copy(@"C:\a.cbz", 20, 1000)]));
        Assert.Null(DuplicateKeeperRanker.Recommend([]));
    }
}
