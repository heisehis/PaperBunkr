using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.Data.Tests.ComicVine;

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
        double score = MatchScoreCalculator.Compute("Batman", null, "9999", null, Candidate(publisher: null, countOfIssues: 500, startYear: null), false, 2024);
        // namescore=5 (exact match), publisherscore=0, yearscore=0, recency≈0 (startYear null -> -1)
        Assert.Equal(5 + 100 - 1, score);
    }

    [Fact]
    public void BookScore_is_100_when_the_issue_number_plausibly_fits_within_the_count()
    {
        double score = MatchScoreCalculator.Compute("Batman", null, "10", null, Candidate(publisher: null, countOfIssues: 20, startYear: null), false, 2024);
        Assert.Equal(5 + 100 - 1, score);
    }

    [Fact]
    public void BookScore_is_negative_100_when_the_issue_number_exceeds_the_count()
    {
        double score = MatchScoreCalculator.Compute("Batman", null, "50", null, Candidate(publisher: null, countOfIssues: 20, startYear: null), false, 2024);
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

    // docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §2.3 - tokenizer fixes,
    // verified directly against matchscore.py.

    [Fact]
    public void NameScore_apostrophe_is_stripped_not_a_split_point()
    {
        // "Don't" -> "dont" (one word), matching CE - if this regressed to splitting on the apostrophe
        // it would tokenize as "don"/"t" (two words) and only partially match "Dont".
        double score = MatchScoreCalculator.Compute("Don't Fear the Reaper", null, null, null,
            Candidate(name: "Dont Fear the Reaper", publisher: null, countOfIssues: null, startYear: "2000"), false, currentYear: 2000);
        // 4 matched words at +5 each = 20, no unmatched either side
        Assert.Equal(20 + 100, score);
    }

    [Theory]
    [InlineData("Giant-Sized X-Men", "Giant Size X-Men", 4)]   // giant,size,X,Men - "X-Men" itself splits on the hyphen too
    [InlineData("Giant Sized X-Men", "Giant Size X-Men", 4)]
    [InlineData("King-Sized Annual", "King Size Annual", 3)]   // king,size,annual
    [InlineData("One-Shot Special", "One Shot Special", 3)]    // one,shot,special
    public void NameScore_canonicalizes_giant_king_sized_and_one_shot(string bookSeries, string candidateName, int expectedFullMatchWordCount)
    {
        double score = MatchScoreCalculator.Compute(bookSeries, null, null, null,
            Candidate(name: candidateName, publisher: null, countOfIssues: null, startYear: "2000"), false, currentYear: 2000);
        Assert.Equal(expectedFullMatchWordCount * 5 + 100, score);
    }

    [Fact]
    public void NameScore_underscore_is_not_a_split_point()
    {
        double score = MatchScoreCalculator.Compute("some_word", null, null, null,
            Candidate(name: "some_word", publisher: null, countOfIssues: null, startYear: "2000"), false, currentYear: 2000);
        Assert.Equal(5 + 100, score); // one word, not two
    }

    // docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §2.4 - fractional issue numbers.

    [Fact]
    public void BookScore_handles_fractional_issue_numbers()
    {
        double plausible = MatchScoreCalculator.Compute("Batman", null, "5.5", null, Candidate(publisher: null, countOfIssues: 10, startYear: null), false, 2024);
        double implausible = MatchScoreCalculator.Compute("Batman", null, "55.5", null, Candidate(publisher: null, countOfIssues: 10, startYear: null), false, 2024);

        Assert.Equal(5 + 100 - 1, plausible);      // 5.5 - 1 = 4.5 <= 10
        Assert.Equal(5 - 100 - 1, implausible);    // 55.5 - 1 = 54.5 > 10
    }

    // docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §2.5 - year sanity check.

    [Fact]
    public void YearScore_treats_an_out_of_range_book_year_as_absent()
    {
        double garbageYear = MatchScoreCalculator.Compute("Batman", null, null, 31337, Candidate(publisher: null, countOfIssues: null, startYear: null), false, 2024);
        double noYear = MatchScoreCalculator.Compute("Batman", null, null, null, Candidate(publisher: null, countOfIssues: null, startYear: null), false, 2024);

        Assert.Equal(noYear, garbageYear);
    }

    [Fact]
    public void YearScore_treats_an_out_of_range_series_year_as_absent()
    {
        // A series "starting" in year 50 is garbage, not a real signal that it predates the book - but
        // RecencyScore (a separate term) still reads the raw, unvalidated series year, so the two
        // candidates below aren't expected to score identically overall; only YearScore's own
        // contribution should be identical (as if the series year were absent in both cases). Isolate
        // that by subtracting out RecencyScore's own known, independently-computed delta.
        double garbageSeriesYear = MatchScoreCalculator.Compute("Batman", null, null, 1990, Candidate(publisher: null, countOfIssues: null, startYear: "50"), false, 2024);
        double noSeriesYear = MatchScoreCalculator.Compute("Batman", null, null, 1990, Candidate(publisher: null, countOfIssues: null, startYear: null), false, 2024);
        double recencyDeltaOnly = RecencyOnly(seriesYear: 50, currentYear: 2024) - RecencyOnly(seriesYear: null, currentYear: 2024);

        Assert.Equal(recencyDeltaOnly, garbageSeriesYear - noSeriesYear, precision: 6);
    }

    private static double RecencyOnly(int? seriesYear, int currentYear) => seriesYear.HasValue ? -(currentYear - seriesYear.Value) / 100.0 : -1.0;
}
