using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class AffinityResolverTests
{
    private static AffinityIssue Issue(int id, int series, float? rating, bool read, string? publisher = "Image")
        => new(id, series, rating, read, publisher);

    private static AffinityCredit Credit(int creator, string name, string role, AffinityIssue issue) => new(creator, name, role, issue);

    /// <summary>Three series, three rated issues: just clears both gates.</summary>
    private static List<AffinityIssue> Qualifying(int baseId, float rating, bool read)
        => Enumerable.Range(0, 3).Select(i => Issue(baseId + i, baseId + i, rating, read)).ToList();

    [Fact]
    public void ScoreIsAverageRatingTimesCompletionRate()
    {
        var issues = new List<AffinityIssue>
        {
            Issue(1, 1, 5f, true), Issue(2, 2, 4f, true), Issue(3, 3, 3f, true), Issue(4, 3, null, false),
        };
        var credits = issues.Select(i => Credit(1, "Brian K. Vaughan", "Writer", i)).ToList();

        var data = AffinityResolver.Compute(credits, issues);

        var entry = Assert.Single(data.Authors);
        Assert.Equal(4.0, entry.AverageRating, 3);
        Assert.Equal(0.75, entry.CompletionRate, 3);
        Assert.Equal(3.0, entry.Score, 3);
        Assert.Equal(3, entry.Series);
        Assert.Equal(4, entry.OwnedIssues);
    }

    [Fact]
    public void CreatorsBelowTheGatesAreHidden()
    {
        // Two series only.
        var twoSeries = new[] { Issue(1, 1, 5f, true), Issue(2, 1, 5f, true), Issue(3, 2, 5f, true) };
        // Three series but only two rated issues.
        var fewRated = new[] { Issue(4, 3, 5f, true), Issue(5, 4, 5f, true), Issue(6, 5, null, true) };
        var credits = twoSeries.Select(i => Credit(1, "A", "Writer", i)).Concat(fewRated.Select(i => Credit(2, "B", "Writer", i))).ToList();

        var data = AffinityResolver.Compute(credits, twoSeries.Concat(fewRated).ToList());

        Assert.Empty(data.Authors);
    }

    [Fact]
    public void RolesSplitIntoAuthorsAndArtistsAndOtherRolesAreIgnored()
    {
        var issues = Qualifying(10, 4f, true);
        var credits = issues.Select(i => Credit(1, "Writer Person", "Writer", i))
            .Concat(issues.Select(i => Credit(2, "Artist Person", "Penciller", i)))
            .Concat(issues.Select(i => Credit(3, "Letter Person", "Letterer", i)))
            .ToList();

        var data = AffinityResolver.Compute(credits, issues);

        Assert.Equal("Writer Person", Assert.Single(data.Authors).Name);
        Assert.Equal("Artist Person", Assert.Single(data.Artists).Name);
    }

    [Fact]
    public void AnArtistWithSeveralRolesOnTheSameIssueCountsItOnce()
    {
        var issues = Qualifying(20, 5f, true);
        var credits = issues.SelectMany(i => new[]
        {
            Credit(2, "Both", "Penciller", i), Credit(2, "Both", "Inker", i),
        }).ToList();

        var entry = Assert.Single(AffinityResolver.Compute(credits, issues).Artists);

        Assert.Equal(3, entry.OwnedIssues);
        Assert.Equal(1.0, entry.CompletionRate, 3);
    }

    [Fact]
    public void RankingPutsHigherScoreFirstAndPublishersAreRankedToo()
    {
        var loved = Qualifying(30, 5f, true);
        var meh = Qualifying(40, 2f, true);
        var credits = loved.Select(i => Credit(1, "Loved", "Writer", i)).Concat(meh.Select(i => Credit(2, "Meh", "Writer", i))).ToList();

        var data = AffinityResolver.Compute(credits, loved.Concat(meh).ToList());

        Assert.Equal(new[] { "Loved", "Meh" }, data.Authors.Select(a => a.Name));
        Assert.Equal("Image", Assert.Single(data.Publishers).Name);
    }

    [Fact]
    public void EmptyInputHasNoData()
    {
        Assert.False(AffinityResolver.Compute(Array.Empty<AffinityCredit>(), Array.Empty<AffinityIssue>()).HasData);
    }
}
