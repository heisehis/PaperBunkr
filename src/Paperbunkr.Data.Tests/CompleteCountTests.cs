using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary><see cref="CompleteCount"/> (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §2), checked against what ComicRack's script does.</summary>
public class CompleteCountTests
{
    private static int _nextId;

    private static Series Series(bool complete = true, int id = 1) =>
        new() { Id = id, Name = "S" + id, Status = complete ? SeriesStatus.Completed : SeriesStatus.Ongoing };

    private static Issue Book(Series series, string? number, int? year = null, int? month = null, string? volume = null, bool placeholder = false) =>
        new() { Id = ++_nextId, SeriesId = series.Id, Series = series, Number = number, Year = year, Month = month, Volume = volume, IsPlaceholder = placeholder };

    private static Issue[] Run(Series series, params string?[] numbers) => numbers.Select(n => Book(series, n)).ToArray();

    [Fact]
    public void ACompletedSeries_GetsItsLastNumberAsTheCount_OnEveryTargetBook()
    {
        var series = Series();
        var books = Run(series, "1", "2", "3", "12");

        var counts = CompleteCount.Compute(books, books);

        Assert.All(books, b => Assert.Equal(12, counts[b.Id]));
    }

    [Fact]
    public void AnOngoingSeries_IsLeftAlone()
    {
        var series = Series(complete: false);
        var books = Run(series, "1", "2");

        Assert.Empty(CompleteCount.Compute(books, books));
    }

    [Fact]
    public void OnlyTheTargetBooksAreGivenACount_ButTheWholeSeriesDecidesIt()
    {
        var series = Series();
        var books = Run(series, "1", "2", "3", "10");

        var counts = CompleteCount.Compute(books, [books[0]]);

        Assert.Equal(10, Assert.Single(counts).Value);
    }

    [Fact]
    public void VolumesOfOneSeries_AreCountedApart()
    {
        var series = Series();
        var first = new[] { Book(series, "1", volume: "1"), Book(series, "6", volume: "1") };
        var second = new[] { Book(series, "1", volume: "2"), Book(series, "3", volume: "2") };
        var all = first.Concat(second).ToArray();

        var counts = CompleteCount.Compute(all, all);

        Assert.Equal(6, counts[first[0].Id]);
        Assert.Equal(3, counts[second[1].Id]);
    }

    [Fact]
    public void ANumberOfAThousandOrMore_IsNotTheSeriesLastNumber()
    {
        var series = Series();
        var books = Run(series, "1", "2", "2019");

        var counts = CompleteCount.Compute(books, books);

        Assert.Equal(2, counts[books[0].Id]);
    }

    [Fact]
    public void ABookNumberedAThousandOrMore_IsAnOutOfNumberingBook_AndIsItsOwnCount()
    {
        var series = Series();
        var books = Run(series, "1", "2", "3");
        var outOfNumbering = Book(series, "2019");

        var counts = CompleteCount.Compute([.. books, outOfNumbering], [.. books, outOfNumbering]);

        Assert.Equal(3, counts[books[0].Id]);
        Assert.Equal(2019, counts[outOfNumbering.Id]);
    }

    [Fact]
    public void ABookWithoutANumber_GetsTheSeriesLastNumber()
    {
        var series = Series();
        var numbered = Run(series, "1", "2", "8");
        var annual = Book(series, "Annual");
        var blank = Book(series, null);

        var counts = CompleteCount.Compute([.. numbered, annual, blank], [annual, blank]);

        Assert.Equal(8, counts[annual.Id]);
        Assert.Equal(8, counts[blank.Id]);
    }

    [Fact]
    public void ASeriesWithNoNumberedBook_IsLeftAlone()
    {
        var series = Series();
        var books = Run(series, null, "Annual");

        Assert.Empty(CompleteCount.Compute(books, books));
    }

    [Fact]
    public void PlaceholdersForMissingIssues_DoNotRaiseTheLastNumber()
    {
        var series = Series();
        var real = Run(series, "1", "2", "3");
        var missing = Book(series, "50", placeholder: true);

        var counts = CompleteCount.Compute([.. real, missing], real);

        Assert.All(counts.Values, c => Assert.Equal(3, c));
    }

    [Fact]
    public void ANumberThatOnlyExistsAsAProposal_Counts()
    {
        var series = Series();
        var proposed = Book(series, null);
        proposed.MetadataProposals.Add(new MetadataProposal { Field = MetadataProposalField.Number, ProposedValue = "9", Status = MetadataProposalStatus.Accepted });
        var other = Book(series, "2");

        var counts = CompleteCount.Compute([proposed, other], [other]);

        Assert.Equal(9, counts[other.Id]);
    }

    // --- Numbering jumps: a step of more than 12 issues per month of publication ---

    [Fact]
    public void ANumberingJump_GivesTheBooksBeforeItTheJumpPointAndTheOthersTheMax()
    {
        // Issues 1..32 over three years, then a relaunch that continues at 900 two months later: 32 -> 900 is a jump.
        var series = Series();
        var early = Enumerable.Range(1, 32).Select(n => Book(series, n.ToString(), year: 2000 + n / 12, month: 1 + n % 12)).ToList();
        var relaunch = Book(series, "900", year: 2000 + 32 / 12, month: 1 + 32 % 12 + 1);

        var counts = CompleteCount.Compute([.. early, relaunch], [early[0], early[31], relaunch]);

        Assert.Equal(32, counts[early[0].Id]);
        Assert.Equal(32, counts[early[31].Id]);
        Assert.Equal(900, counts[relaunch.Id]);
    }

    [Fact]
    public void ASeriesThatNeverReachesThirty_IsNotCheckedForJumps()
    {
        var series = Series();
        var books = new[] { Book(series, "1", year: 2000, month: 1), Book(series, "25", year: 2000, month: 1) };

        var counts = CompleteCount.Compute(books, books);

        Assert.All(counts.Values, c => Assert.Equal(25, c));
    }

    [Fact]
    public void SteadyPublicationOverTheYears_IsNotAJump()
    {
        var series = Series();
        var books = Enumerable.Range(1, 60).Select(n => Book(series, n.ToString(), year: 2000 + n / 12, month: 1 + n % 12)).ToArray();

        var counts = CompleteCount.Compute(books, books);

        Assert.All(counts.Values, c => Assert.Equal(60, c));
    }
}
