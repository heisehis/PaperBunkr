using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="BookAgeResolver"/> (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4g-age-progression-design.md) - read-time computation over a plain <see cref="Issue"/>,
/// no database.
/// </summary>
public class BookAgeResolverTests
{
    [Fact]
    public void ExplicitCeLabel_WinsOverYear_EvenWhenTheyDisagree()
    {
        var issue = new Issue { BookAge = "Golden", Year = 2015 };

        var (age, confidence, reason) = BookAgeResolver.Resolve(issue);

        Assert.Equal(ComicAge.Golden, age);
        Assert.Equal(1.0m, confidence);
        Assert.Null(reason);
    }

    [Fact]
    public void RecognizedLabel_IgnoresItsOwnParentheticalRangeText()
    {
        var issue = new Issue { BookAge = "Silver (1956-69)" };

        Assert.Equal(ComicAge.Silver, BookAgeResolver.Resolve(issue).Age);
    }

    [Fact]
    public void UnrecognizedBookAgeString_WithYear_FallsBackToYearInference()
    {
        var issue = new Issue { BookAge = "Chrome Age", Year = 1965 };

        Assert.Equal(ComicAge.Silver, BookAgeResolver.Resolve(issue).Age);
    }

    [Fact]
    public void Year1982_ResolvesToModern_AtReducedConfidence_WithDisputedWindowReason()
    {
        var issue = new Issue { Year = 1982 };

        var (age, confidence, reason) = BookAgeResolver.Resolve(issue);

        Assert.Equal(ComicAge.Modern, age);
        Assert.Equal(0.6m, confidence);
        Assert.Contains("commonly cited elsewhere as still Bronze Age", reason);
    }

    [Fact]
    public void Year1990_ResolvesToModern_AtFullConfidence_NoReason()
    {
        var (age, confidence, reason) = BookAgeResolver.Resolve(new Issue { Year = 1990 });

        Assert.Equal(ComicAge.Modern, age);
        Assert.Equal(1.0m, confidence);
        Assert.Null(reason);
    }

    [Fact]
    public void NoBookAge_NoYear_ReturnsNoGuess()
    {
        var (age, confidence, reason) = BookAgeResolver.Resolve(new Issue());

        Assert.Null(age);
        Assert.Equal(0m, confidence);
        Assert.Null(reason);
    }
}
