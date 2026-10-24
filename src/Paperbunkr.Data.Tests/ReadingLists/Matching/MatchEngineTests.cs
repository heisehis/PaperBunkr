using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.Data.Tests.ReadingLists.Matching;

/// <summary>
/// The pure half of the matching core (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.1-§5.5): number and series
/// keys, and the engine's classification, against hand-built snapshots - no database.
/// </summary>
public class MatchEngineTests
{
    // --- IssueNumberKey (decision Q16) ---

    [Theory]
    [InlineData("1", "001")]
    [InlineData("1", "#1")]
    [InlineData("1", " # 1 ")]
    [InlineData("½", "1/2")]
    [InlineData("½", "0.5")]
    [InlineData(".5", "0.5")]
    [InlineData("1.AU", "1.au")]
    [InlineData("01.5", "1.5")]
    [InlineData("Annual 1", "annual  1")]
    [InlineData("0", "000")]
    public void NumberKey_TreatsTheseAsTheSameIssue(string a, string b) => Assert.True(IssueNumberKey.Same(a, b));

    [Theory]
    [InlineData("1", "1.AU")]
    [InlineData("1", "1A")]
    [InlineData("1", "Annual 1")]
    [InlineData("1", "1/2")]
    [InlineData("1.1", "1.10")]
    [InlineData("1", "10")]
    [InlineData("1", "-1")]
    [InlineData("", "")]
    [InlineData(null, "1")]
    public void NumberKey_KeepsTheseApart(string? a, string? b) => Assert.False(IssueNumberKey.Same(a, b));

    // --- SeriesKeys ---

    [Theory]
    [InlineData("Cataclysm: The Ultimates", "Cataclysm - The Ultimates")]
    [InlineData("Hal Jordan & The Green Lantern Corps", "Hal Jordan And The Green Lantern Corps")]
    [InlineData("Batman Vol. 2", "Batman")]
    [InlineData("The Flash", "Flash")]
    public void SeriesKey_FoldsEquivalentNames(string a, string b) => Assert.Equal(SeriesKeys.Of(a), SeriesKeys.Of(b));

    [Theory]
    [InlineData("King in Black: Thunderbolts", "Thunderbolts")]
    [InlineData("Black Cat", "Mary Jane & Black Cat")]
    [InlineData("Superman", "Superman Unlimited")]
    public void SeriesKey_DoesNotFoldDifferentSeries(string a, string b) => Assert.NotEqual(SeriesKeys.Of(a), SeriesKeys.Of(b));

    // --- Engine ---

    private static LibraryIssue Comic(int id, int seriesId, string series, string number, int? year = null, string? volume = null, bool missing = false, int pages = 20) =>
        new(id, seriesId, series, SeriesKeys.Of(series), number, IssueNumberKey.Of(number), volume, year, null, missing, pages, 1000);

    private static LibraryIndex Library(IEnumerable<LibraryIssue> issues, params (int IssueId, ComicProvider Provider, string Id)[] ids) => new(issues, ids);

    [Fact]
    public void ComicVineId_CarriedByOneComic_IsVerified_EvenWhenTheNameDiffers()
    {
        var library = Library([Comic(1, 1, "Something Else", "9")], (1, ComicProvider.ComicVine, "555"));
        var result = MatchEngine.Match(new ComicReference("Frank Miller's RoboCop", "1", ComicVineIssueId: "555"), library);

        Assert.Equal(MatchClass.Verified, result.Class);
        Assert.Equal(1, result.AutoLink!.Id);
        Assert.True(result.ById);
    }

    [Fact]
    public void MetronId_IsItsOwnNamespace()
    {
        // The same number under the other provider must not match.
        var library = Library([Comic(1, 1, "Herc", "7"), Comic(2, 2, "Other", "3")], (2, ComicProvider.ComicVine, "42"));
        var result = MatchEngine.Match(new ComicReference("Herc", "7", MetronIssueId: "42"), library);

        Assert.Equal(MatchClass.Strong, result.Class);     // fell through to metadata; the id matched nothing
        Assert.Equal(1, result.AutoLink!.Id);
        Assert.False(result.ById);
    }

    [Fact]
    public void UnknownId_FallsBackToMetadata()
    {
        var library = Library([Comic(1, 1, "Herc", "7", 2011)]);
        var result = MatchEngine.Match(new ComicReference("Herc", "7", Year: 2011, ComicVineIssueId: "999"), library);
        Assert.Equal(MatchClass.Strong, result.Class);
    }

    [Fact]
    public void IdsPointingAtDifferentComics_AreAConflict()
    {
        var library = Library([Comic(1, 1, "A", "1"), Comic(2, 2, "B", "1")], (1, ComicProvider.ComicVine, "10"), (2, ComicProvider.Metron, "20"));
        var result = MatchEngine.Match(new ComicReference("A", "1", ComicVineIssueId: "10", MetronIssueId: "20"), library);

        Assert.Equal(MatchClass.Conflict, result.Class);
        Assert.Null(result.AutoLink);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void TwoFilesOfOneComicUnderOneId_AreOneCandidate_ThePresentCopy()
    {
        var library = Library(
            [Comic(1, 1, "A", "1", 2020, missing: true), Comic(2, 1, "A", "1", 2020, pages: 30)],
            (1, ComicProvider.ComicVine, "10"), (2, ComicProvider.ComicVine, "10"));
        var result = MatchEngine.Match(new ComicReference("A", "1", ComicVineIssueId: "10"), library);

        Assert.Equal(MatchClass.Verified, result.Class);
        Assert.Equal(2, result.AutoLink!.Id);
    }

    [Fact]
    public void NumberOwnedInASecondSeriesRowWithTheSameName_IsFound()
    {
        // The proven failure: row 1 is an empty/other row found first, the comic sits in row 2.
        var library = Library([Comic(1, 1, "Herc", "1"), Comic(2, 2, "Herc", "7", 2011)]);
        var result = MatchEngine.Match(new ComicReference("Herc", "7", Year: 2011), library);

        Assert.Equal(MatchClass.Strong, result.Class);
        Assert.Equal(2, result.AutoLink!.Id);
    }

    [Fact]
    public void ThreeVolumes_TheYearPicksOne()
    {
        var library = Library([Comic(1, 1, "Avengers", "1", 1963), Comic(2, 1, "Avengers", "1", 1996), Comic(3, 1, "Avengers", "1", 2010)]);
        var result = MatchEngine.Match(new ComicReference("Avengers", "1", Year: 1996), library);

        Assert.Equal(MatchClass.Strong, result.Class);
        Assert.Equal(2, result.AutoLink!.Id);
        Assert.Equal(3, result.Candidates.Count);       // the others stay listed
    }

    [Fact]
    public void ThreeVolumes_StartYearVolumePicksOne()
    {
        var library = Library([Comic(1, 1, "Avengers", "1", 1963, "1963"), Comic(2, 1, "Avengers", "1", 1997, "1996"), Comic(3, 1, "Avengers", "1", 2010, "2010")]);
        var result = MatchEngine.Match(new ComicReference("Avengers", "1", Volume: "1996"), library);

        Assert.Equal(MatchClass.Strong, result.Class);
        Assert.Equal(2, result.AutoLink!.Id);
    }

    [Fact]
    public void TwoVolumes_NothingToTellThemApart_IsAmbiguous()     // decision Q3
    {
        var library = Library([Comic(1, 1, "Avengers", "1", 1963), Comic(2, 1, "Avengers", "1", 1996)]);
        var result = MatchEngine.Match(new ComicReference("Avengers", "1"), library);

        Assert.Equal(MatchClass.Ambiguous, result.Class);
        Assert.Null(result.AutoLink);
        Assert.True(result.NeedsChoice);
    }

    [Fact]
    public void LoneCandidateFromAnotherDecade_IsOnlyPossible()     // decision Q4
    {
        var library = Library([Comic(1, 1, "Thunderbolts", "1", 1997)]);
        var result = MatchEngine.Match(new ComicReference("Thunderbolts", "1", Year: 2021), library);

        Assert.Equal(MatchClass.Possible, result.Class);
        Assert.Null(result.AutoLink);
        Assert.Contains("different year", result.Candidates[0].Summary);
    }

    [Fact]
    public void YearWithinOne_Agrees()
    {
        var library = Library([Comic(1, 1, "Herc", "7", 2012)]);
        Assert.Equal(MatchClass.Strong, MatchEngine.Match(new ComicReference("Herc", "7", Year: 2011), library).Class);
    }

    [Fact]
    public void OrdinalVolumeAgainstStartYearVolume_SaysNothing()
    {
        // A .cbl writes Volume="2016"; the file says volume 5. Different kinds of number: not a conflict.
        var library = Library([Comic(1, 1, "Flash", "1", 2016, "5")]);
        var result = MatchEngine.Match(new ComicReference("The Flash", "1", Volume: "2016", Year: 2016), library);

        Assert.Equal(MatchClass.Strong, result.Class);
        Assert.Contains("volume unknown", result.Candidates[0].Summary);
    }

    [Fact]
    public void AlternateTitle_AnswersForItsSeries()
    {
        var library = new LibraryIndex([Comic(1, 1, "Shingeki no Kyojin", "1")], alternateTitles: [(1, "Attack on Titan")]);
        Assert.Equal(1, MatchEngine.Match(new ComicReference("Attack on Titan", "1"), library).AutoLink!.Id);
    }

    [Fact]
    public void NothingFits_IsUnresolved()
    {
        var library = Library([Comic(1, 1, "Herc", "7")]);
        Assert.Equal(MatchClass.Unresolved, MatchEngine.Match(new ComicReference("Herc", "8"), library).Class);
        Assert.Equal(MatchClass.Unresolved, MatchEngine.Match(new ComicReference("King in Black: Herc", "7"), library).Class);
    }

    [Fact]
    public void Resolver_SecondEntryForTheSameComic_IsADuplicate()     // decision Q12
    {
        var library = Library([Comic(1, 1, "Herc", "7")]);
        var resolution = ReadingListResolver.Resolve(library,
            [new ComicReference("Herc", "7"), new ComicReference("Herc", "007"), new ComicReference("Gone", "1"), new ComicReference("Gone", "1")]);

        Assert.Equal(
            [EntryOutcome.MatchedByMetadata, EntryOutcome.Duplicate, EntryOutcome.Missing, EntryOutcome.Duplicate],
            resolution.Entries.Select(e => e.Outcome));

        var report = resolution.Report(["a line"], reported: 6);
        Assert.Equal(report.Parsed, report.Matched + report.NeedsChoice + report.Missing + report.Duplicates.Count);
        Assert.True(report.Incomplete);     // 6 reported, 4 parsed + 1 skipped
        Assert.Contains("Herc #007", report.Duplicates);
    }
}
