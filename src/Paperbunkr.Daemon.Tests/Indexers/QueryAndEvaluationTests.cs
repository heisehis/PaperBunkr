using Paperbunkr.Daemon.Indexers;

namespace Paperbunkr.Daemon.Tests.Indexers;

public class QueryBuilderTests
{
    [Fact]
    public void NumberVariants_ForIntegers_AreUnpaddedAndZeroPadded()
    {
        Assert.Equal(new[] { "5", "05", "005" }, QueryBuilder.NumberVariants("5"));
        Assert.Equal(new[] { "5", "05", "005" }, QueryBuilder.NumberVariants("#005"));
        Assert.Equal(new[] { "12", "012" }, QueryBuilder.NumberVariants("12"));
        Assert.Equal(new[] { "263" }, QueryBuilder.NumberVariants("263"));
        Assert.Equal(new[] { "0", "00", "000" }, QueryBuilder.NumberVariants("0"));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("Annual 1")]
    public void NumberVariants_ForNonIntegers_AreLeftExactlyAsGiven(string number)
    {
        Assert.Equal(new[] { number }, QueryBuilder.NumberVariants(number));
    }

    [Theory]
    [InlineData("X-Men")]
    [InlineData("Spider-Man")]
    [InlineData("+Anima")]
    public void StrictPass_KeepsPunctuationIntact(string name)
    {
        var passes = QueryBuilder.Build(new IndexerQuery(name, "5"));

        Assert.False(passes[0].Sanitized);
        Assert.All(passes[0].Queries, q => Assert.StartsWith(name + " ", q));
    }

    [Fact]
    public void SanitizedPass_IsSecond_AndStripsPunctuationAndStopwords()
    {
        var passes = QueryBuilder.Build(new IndexerQuery("The Amazing Spider-Man: Homecoming", "5"));

        Assert.Equal(2, passes.Count);
        Assert.True(passes[1].Sanitized);
        Assert.Equal("Amazing Spider Man Homecoming 5", passes[1].Queries[0]);
    }

    [Fact]
    public void SanitizedPass_IsOmitted_WhenItWouldBeIdenticalToTheStrictPass()
    {
        var passes = QueryBuilder.Build(new IndexerQuery("Spawn", "5"));

        Assert.Single(passes);
    }

    [Fact]
    public void Queries_IncludeYearAndVolumeForms()
    {
        var pass = QueryBuilder.Build(new IndexerQuery("Batman", "5", Year: 2026, Volume: 3))[0];

        Assert.Contains("Batman 5", pass.Queries);
        Assert.Contains("Batman 05", pass.Queries);
        Assert.Contains("Batman 005", pass.Queries);
        Assert.Contains("Batman 5 2026", pass.Queries);
        Assert.Contains("Batman v3 5", pass.Queries);
    }
}

public class PackDetectorTests
{
    [Theory]
    [InlineData("Saga v1-6 (2012-2018) (digital)", 1, 6)]
    [InlineData("Batman #1-12 (2016)", 1, 12)]
    [InlineData("Spawn 001 to 050", 1, 50)]
    public void DetectsIssueRanges(string title, int start, int end)
    {
        var info = PackDetector.Detect(title);

        Assert.True(info.IsPack);
        Assert.Equal(start, info.RangeStart);
        Assert.Equal(end, info.RangeEnd);
    }

    [Theory]
    [InlineData("DC Comics Weekly Pack 2026.09.16")]
    [InlineData("Invincible Complete Series (digital)")]
    [InlineData("Marvel Megapack")]
    public void DetectsKeywordPacks_WithoutARange(string title)
    {
        var info = PackDetector.Detect(title);

        Assert.True(info.IsPack);
        Assert.Null(info.RangeStart);
    }

    [Theory]
    [InlineData("Spawn 263 (2016) (digital)")]
    [InlineData("X-Men 005 (1985-2026)")]           // a year range is not an issue range
    [InlineData("Batman 145 (2026-09-16)")]         // neither is a date
    [InlineData("Spider-Man 2099 #5")]
    public void DoesNotMistakeSingleIssuesForPacks(string title)
    {
        Assert.False(PackDetector.Detect(title).IsPack);
    }

    [Fact]
    public void Covers_ChecksTheRangeInclusively()
    {
        var info = PackDetector.Detect("Saga #1-12");

        Assert.True(info.Covers(1));
        Assert.True(info.Covers(12));
        Assert.False(info.Covers(13));
    }
}

public class ReleaseEvaluatorTests
{
    private static IndexerRelease Release(string title, long mb = 60, int seeders = 10) => new()
    {
        Title = title, DownloadUrl = "magnet:?xt=urn:btih:" + title.GetHashCode(), SizeBytes = mb * 1024 * 1024, Seeders = seeders,
    };

    private static readonly ScoringOptions Defaults = new();

    [Theory]
    [InlineData("Spawn 263 (2016) (digital)", "263")]
    [InlineData("Spawn #263 (2016)", "263")]
    [InlineData("Spawn 005 (1992)", "5")]
    [InlineData("Spawn 5 (1992)", "005")]
    [InlineData("Spawn v2 05", "5")]
    public void Accepts_TheRightIssue_AllowingPaddingAndHashForms(string title, string wanted)
    {
        Assert.NotNull(ReleaseEvaluator.Evaluate(Release(title), new IndexerQuery("Spawn", wanted), Defaults));
    }

    [Theory]
    [InlineData("Spawn 264 (2016)", "263")]          // wrong issue
    [InlineData("Spawn 1263 (2016)", "263")]         // number is inside a longer number
    [InlineData("Spawn 15 (1992)", "5")]
    [InlineData("Spawn Origins 5 (2009)", "5")]      // different series that merely starts the same
    [InlineData("Batman 263 (2016)", "263")]         // wrong series
    public void Rejects_WrongIssueOrWrongSeries(string title, string wanted)
    {
        Assert.Null(ReleaseEvaluator.Evaluate(Release(title), new IndexerQuery("Spawn", wanted), Defaults));
    }

    [Theory]
    [InlineData("X-Men 5 (2026)", "X-Men")]
    [InlineData("X Men 005 (2026)", "X-Men")]
    [InlineData("Anima 003 (2026)", "+Anima")]
    [InlineData("Boys 010 (2026)", "The Boys")]
    public void SeriesMatching_IgnoresPunctuationAndLeadingStopwords(string title, string series)
    {
        var wanted = title.Contains("003") ? "3" : title.Contains("010") ? "10" : "5";
        Assert.NotNull(ReleaseEvaluator.Evaluate(Release(title), new IndexerQuery(series, wanted), Defaults));
    }

    [Fact]
    public void NonIntegerNumbers_MustAppearAsWritten()
    {
        var query = new IndexerQuery("Batman", "1.5");

        Assert.NotNull(ReleaseEvaluator.Evaluate(Release("Batman 1.5 (2026)"), query, Defaults));
        Assert.Null(ReleaseEvaluator.Evaluate(Release("Batman 1 (2026)"), query, Defaults));
    }

    [Fact]
    public void SizeLimits_AndIgnoredWords_Reject()
    {
        var query = new IndexerQuery("Spawn", "5");

        Assert.Null(ReleaseEvaluator.Evaluate(Release("Spawn 5", mb: 900), query, new ScoringOptions { MaxSizeMb = 500 }));
        Assert.Null(ReleaseEvaluator.Evaluate(Release("Spawn 5", mb: 1), query, new ScoringOptions { MinSizeMb = 5 }));
        Assert.Null(ReleaseEvaluator.Evaluate(Release("Spawn 5 Spanish"), query, new ScoringOptions { IgnoredWords = new[] { "spanish" } }));
        Assert.NotNull(ReleaseEvaluator.Evaluate(Release("Spawn 5", mb: 900), query, new ScoringOptions { MaxSizeMb = 0 }));
    }

    [Fact]
    public void Packs_AreFlagged_AndRankBelowSingles()
    {
        var query = new IndexerQuery("Saga", "4");
        var single = ReleaseEvaluator.Evaluate(Release("Saga 004 (2012)", seeders: 2), query, Defaults);
        var pack = ReleaseEvaluator.Evaluate(Release("Saga v1-6 (2012-2018)", mb: 900, seeders: 500), query, Defaults);

        Assert.NotNull(single);
        Assert.NotNull(pack);
        Assert.False(single!.IsPack);
        Assert.True(pack!.IsPack);
        Assert.True(single.Score > pack.Score);
    }

    [Fact]
    public void ARangedPack_ThatDoesNotContainTheIssue_IsRejected()
    {
        var query = new IndexerQuery("Saga", "20");

        Assert.Null(ReleaseEvaluator.Evaluate(Release("Saga v1-6 #1-12"), query, Defaults));
    }

    [Fact]
    public void Scoring_PrefersSeedersCbzAndPreferredGroups_ButYearMismatchIsOnlyAPenalty()
    {
        var query = new IndexerQuery("Spawn", "5", Year: 2026);
        var options = new ScoringOptions { PreferredGroups = new[] { "Zone-Empire" } };

        double plain = ReleaseEvaluator.Evaluate(Release("Spawn 005 (2026)", seeders: 10), query, options)!.Score;
        double moreSeeders = ReleaseEvaluator.Evaluate(Release("Spawn 005 (2026)", seeders: 40), query, options)!.Score;
        double cbz = ReleaseEvaluator.Evaluate(Release("Spawn 005 (2026) cbz", seeders: 10), query, options)!.Score;
        double cbr = ReleaseEvaluator.Evaluate(Release("Spawn 005 (2026) cbr", seeders: 10), query, options)!.Score;
        double group = ReleaseEvaluator.Evaluate(Release("Spawn 005 (2026) (Zone-Empire)", seeders: 10), query, options)!.Score;
        var wrongYear = ReleaseEvaluator.Evaluate(Release("Spawn 005 (1999)", seeders: 10), query, options);

        Assert.True(moreSeeders > plain);
        Assert.True(cbz > plain && plain > cbr);
        Assert.True(group > plain);
        Assert.NotNull(wrongYear);                 // not rejected...
        Assert.True(wrongYear!.Score < plain);     // ...just ranked lower
    }

    [Fact]
    public void ReleasesWithNoSeeders_ScoreNegative()
    {
        var scored = ReleaseEvaluator.Evaluate(Release("Spawn 005 (2026)", seeders: 0), new IndexerQuery("Spawn", "5"), Defaults);

        Assert.True(scored!.Score < 0);
    }
}
