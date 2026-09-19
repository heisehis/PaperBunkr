using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Xunit;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="SeriesRatingResolver"/> (docs/superpowers/specs/2026-09-18-per-tracker-
/// score-and-finish-date-design.md) - read-time computation over a plain <see cref="Series"/>, no
/// database.
/// </summary>
public class SeriesRatingResolverTests
{
    [Fact]
    public void EmptySeries_ReturnsNull()
    {
        var series = new Series();

        Assert.Null(SeriesRatingResolver.Recompute(series));
    }

    [Fact]
    public void AllIssuesUnrated_ReturnsNull_NotZero()
    {
        var series = new Series { Issues = { new Issue { Rating = null }, new Issue { Rating = null } } };

        Assert.Null(SeriesRatingResolver.Recompute(series));
    }

    [Fact]
    public void MixedRatedAndUnrated_AveragesOnlyTheRatedOnes()
    {
        var series = new Series { Issues = { new Issue { Rating = 4f }, new Issue { Rating = 2f }, new Issue { Rating = null } } };

        Assert.Equal(3f, SeriesRatingResolver.Recompute(series));
    }

    [Fact]
    public void SingleRatedIssue_ReturnsThatIssuesRating()
    {
        var series = new Series { Issues = { new Issue { Rating = 4.5f } } };

        Assert.Equal(4.5f, SeriesRatingResolver.Recompute(series));
    }
}
