using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The metadata consistency scan (docs/superpowers/specs/2026-10-06-smart-features-design.md §3.4): an issue is only an outlier when
/// its series has a clear norm - at least four issues with a value and 70% of them agreeing.
/// </summary>
public class MetadataConsistencyResolverTests
{
    private static int _nextId = 1;

    private static Series NewSeries(string name) => new() { Id = _nextId++, Name = name };

    private static Issue Issue(Series series, string number, int? year = null, string? publisher = null, string? rating = null) => new()
    {
        Id = _nextId++,
        Series = series,
        SeriesId = series.Id,
        Number = number,
        Year = year,
        Publisher = publisher,
        AgeRating = rating,
    };

    [Fact]
    public void Year_FlagsAnIssueFarFromTheMedian_AndNamesTheMedian()
    {
        var s = NewSeries("Saga");
        var issues = new[] { Issue(s, "1", 2012), Issue(s, "2", 2012), Issue(s, "3", 2013), Issue(s, "4", 2013), Issue(s, "5", 1960) };

        var finding = Assert.Single(MetadataConsistencyResolver.Scan(issues));

        Assert.Equal(ConsistencyCheck.Year, finding.Check);
        Assert.Equal("2012", finding.Expected);
        var outlier = Assert.Single(finding.Outliers);
        Assert.Equal("5", outlier.Number);
        Assert.Equal("1960", outlier.Value);
        Assert.Equal($"{s.Id}:Year", finding.DismissalKey);
    }

    [Fact]
    public void Year_WithinTenYearsOfTheMedian_IsNotAnOutlier()
    {
        var s = NewSeries("Long runner");
        var issues = new[] { Issue(s, "1", 2000), Issue(s, "2", 2003), Issue(s, "3", 2005), Issue(s, "4", 2008), Issue(s, "5", 2010) };

        Assert.Empty(MetadataConsistencyResolver.Scan(issues));
    }

    [Fact]
    public void Year_NeedsFourDatedIssues()
    {
        var s = NewSeries("Short");
        var issues = new[] { Issue(s, "1", 2012), Issue(s, "2", 2012), Issue(s, "3", 1960), Issue(s, "4") };

        Assert.Empty(MetadataConsistencyResolver.Scan(issues));
    }

    [Fact]
    public void Year_WithNoClearNorm_ReportsNothing()
    {
        // An anthology spread across decades: fewer than 70% sit near the median, so nothing is "wrong".
        var s = NewSeries("Anthology");
        var issues = new[] { Issue(s, "1", 1950), Issue(s, "2", 1965), Issue(s, "3", 1980), Issue(s, "4", 1995), Issue(s, "5", 2010) };

        Assert.Empty(MetadataConsistencyResolver.Scan(issues));
    }

    [Fact]
    public void Publisher_FlagsTheMinority_WhenSeventyPercentAgree_IgnoringCaseAndBlanks()
    {
        var s = NewSeries("Invincible");
        var issues = new[]
        {
            Issue(s, "1", publisher: "Image"), Issue(s, "2", publisher: "image"), Issue(s, "3", publisher: "Image"),
            Issue(s, "4", publisher: "Marvel"), Issue(s, "5"),
        };

        var finding = Assert.Single(MetadataConsistencyResolver.Scan(issues));

        Assert.Equal(ConsistencyCheck.Publisher, finding.Check);
        Assert.Equal("Image", finding.Expected);
        Assert.Equal("4", Assert.Single(finding.Outliers).Number);
    }

    [Fact]
    public void Publisher_SplitEvenly_HasNoMajority_SoNothingIsFlagged()
    {
        var s = NewSeries("Relaunched");
        var issues = new[]
        {
            Issue(s, "1", publisher: "Image"), Issue(s, "2", publisher: "Image"),
            Issue(s, "3", publisher: "Boom"), Issue(s, "4", publisher: "Boom"),
        };

        Assert.Empty(MetadataConsistencyResolver.Scan(issues));
    }

    [Fact]
    public void AgeRating_IsCheckedTheSameWay_AndEachCheckIsItsOwnFinding()
    {
        var s = NewSeries("Both");
        var issues = new[]
        {
            Issue(s, "1", 2012, "Image", "Teen"), Issue(s, "2", 2012, "Image", "Teen"), Issue(s, "3", 2012, "Image", "Teen"),
            Issue(s, "4", 2012, "Image", "Teen"), Issue(s, "5", 1970, "Image", "Mature 17+"),
        };

        var findings = MetadataConsistencyResolver.Scan(issues);

        Assert.Equal([ConsistencyCheck.Year, ConsistencyCheck.AgeRating], findings.Select(f => f.Check));
        Assert.All(findings, f => Assert.Equal("5", Assert.Single(f.Outliers).Number));
    }

    [Fact]
    public void Findings_AreOrderedBySeriesName_AndOutliersByIssueNumber()
    {
        var b = NewSeries("Beta");
        var a = NewSeries("Alpha");
        var issues = new[]
        {
            Issue(b, "1", publisher: "X"), Issue(b, "2", publisher: "X"), Issue(b, "3", publisher: "X"), Issue(b, "4", publisher: "Y"),
            Issue(a, "10", publisher: "Z"), Issue(a, "2", publisher: "Z"), Issue(a, "3", publisher: "X"), Issue(a, "4", publisher: "X"),
            Issue(a, "5", publisher: "X"), Issue(a, "6", publisher: "X"), Issue(a, "7", publisher: "X"), Issue(a, "8", publisher: "X"),
        };

        var findings = MetadataConsistencyResolver.Scan(issues);

        Assert.Equal(["Alpha", "Beta"], findings.Select(f => f.SeriesName));
        Assert.Equal(["2", "10"], findings[0].Outliers.Select(o => o.Number));
    }
}
