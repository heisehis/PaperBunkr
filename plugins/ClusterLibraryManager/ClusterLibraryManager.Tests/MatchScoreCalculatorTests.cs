using ClusterLibraryManager.ComicVine;

namespace ClusterLibraryManager.Tests;

/// <summary>Implementation plan Phase 3 Step 3.2 verification - table-driven tests, one per
/// matchscore.py term (design doc §4), isolating each term by holding every other input neutral.</summary>
public sealed class MatchScoreCalculatorTests
{
    private static ComicVineVolumeSearchResult Candidate(
        string name = "Batman", string? publisher = "DC Comics", int? countOfIssues = 50, string? startYear = "1990") =>
        new(1, name, startYear, publisher, countOfIssues, null);

    [Theory]
    [InlineData("Batman", "Batman", 5)] // one matching word, no unmatched words either side
    [InlineData("Batman Returns", "Batman", 4)] // one match (+5), one unmatched book word (-1)
    [InlineData("Batman", "Batman Returns", 4)] // one match (+5), one unmatched candidate word (-1)
    [InlineData("Superman", "Batman", -2)] // no match: -1 unmatched book word, -1 unmatched candidate word remaining
    public void NameScore_word_overlap(string bookSeries, string candidateName, double expectedNameScoreContribution)
    {
        // Isolate namescore: publisher/count_of_issues/year neutral, no prior choice, recency at the
        // candidate's own year so recency_score is a known constant to subtract back out.
        double score = MatchScoreCalculator.Compute(bookSeries, null, null, null, Candidate(name: candidateName, publisher: null, countOfIssues: null, startYear: "2000"), false, currentYear: 2000);
        // bookscore(null,null)=100, publisherscore(null)=0, yearscore(null,_)=0, recency(2000,2000)=0, priorscore=0
        Assert.Equal(expectedNameScoreContribution + 100, score);
    }

    [Fact]
    public void PriorScore_adds_flat_seven_when_previously_chosen()
    {
        double withoutPrior = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: null, countOfIssues: null, startYear: null), wasPreviouslyChosenForSimilarBook: false, currentYear: 2024);
        double withPrior = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: null, countOfIssues: null, startYear: null), wasPreviouslyChosenForSimilarBook: true, currentYear: 2024);

        Assert.Equal(7, withPrior - withoutPrior);
    }

    [Theory]
    [InlineData("Panini Comics")] // substring match
    [InlineData("Marvel Italia")] // exact match
    [InlineData("Marvel UK")]
    [InlineData("Abril")]
    public void PublisherScore_penalizes_mirror_reprint_publishers(string mirrorPublisher)
    {
        double mirrored = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: mirrorPublisher, countOfIssues: null, startYear: null), false, 2024);
        double normal = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: "DC Comics", countOfIssues: null, startYear: null), false, 2024);

        Assert.Equal(-6, mirrored - normal);
    }

    [Fact]
    public void BookScore_is_always_100_for_a_series_with_more_than_100_issues_even_if_the_number_seems_too_high()
    {
        double score = MatchScoreCalculator.Compute("Batman", null, 9999, null, Candidate(publisher: null, countOfIssues: 500, startYear: null), false, 2024);
        // namescore=5 (exact match), publisherscore=0, yearscore=0, recency≈0 (startYear null -> -1)
        Assert.Equal(5 + 100 - 1, score);
    }

    [Fact]
    public void BookScore_is_100_when_the_issue_number_plausibly_fits_within_the_count()
    {
        double score = MatchScoreCalculator.Compute("Batman", null, 10, null, Candidate(publisher: null, countOfIssues: 20, startYear: null), false, 2024);
        Assert.Equal(5 + 100 - 1, score);
    }

    [Fact]
    public void BookScore_is_negative_100_when_the_issue_number_exceeds_the_count()
    {
        double score = MatchScoreCalculator.Compute("Batman", null, 50, null, Candidate(publisher: null, countOfIssues: 20, startYear: null), false, 2024);
        Assert.Equal(5 - 100 - 1, score);
    }

    [Fact]
    public void YearScore_is_negative_100_when_the_book_has_a_year_but_the_series_does_not()
    {
        double withBookYear = MatchScoreCalculator.Compute("Batman", null, null, 1990, Candidate(publisher: null, countOfIssues: null, startYear: null), false, 2024);
        double withoutBookYear = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: null, countOfIssues: null, startYear: null), false, 2024);

        Assert.Equal(-100, withBookYear - withoutBookYear);
    }

    [Fact]
    public void YearScore_is_negative_500_when_the_series_starts_after_the_books_year()
    {
        // Vary bookYear, not seriesYear, so recency_score (which depends only on seriesYear) stays
        // identical between the two branches and doesn't leak into this yearscore-only comparison.
        double seriesAfterBook = MatchScoreCalculator.Compute("Batman", null, null, 1980, Candidate(publisher: null, countOfIssues: null, startYear: "2000"), false, 2024);
        double seriesNotAfterBook = MatchScoreCalculator.Compute("Batman", null, null, 2010, Candidate(publisher: null, countOfIssues: null, startYear: "2000"), false, 2024);

        Assert.Equal(-500, seriesAfterBook - seriesNotAfterBook);
    }

    [Fact]
    public void RecencyScore_favors_a_series_year_closer_to_the_current_year()
    {
        double olderSeries = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: null, countOfIssues: null, startYear: "1950"), false, currentYear: 2024);
        double newerSeries = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: null, countOfIssues: null, startYear: "2020"), false, currentYear: 2024);

        Assert.True(newerSeries > olderSeries);
    }

    [Fact]
    public void RecencyScore_defaults_to_a_flat_negative_one_when_the_series_year_is_unknown()
    {
        double unknownYear = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: null, countOfIssues: null, startYear: null), false, currentYear: 2024);
        // namescore=5, bookscore=100(unknown count treated neutrally), publisherscore=0, yearscore=0, recency=-1
        Assert.Equal(5 + 100 - 1, unknownYear);
    }
}
