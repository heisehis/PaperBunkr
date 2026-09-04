using System;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="SeriesMetaFields.FromSeries"/> - aggregates publisher / year / format / age-rating /
/// language across a series' issues for the hero badge row (docs/superpowers/specs/2026-09-04-
/// detail-screen-icons-and-glyphs-design.md Part 4).
/// </summary>
public class SeriesMetaFieldsTests
{
    private static Series Series(params Issue[] issues)
    {
        var s = new Series { Name = "S" };
        foreach (var i in issues) s.Issues.Add(i);
        return s;
    }

    [Fact]
    public void AggregatesFromIssues_WhenSeriesLevelFieldIsBlank()
    {
        var s = Series(
            new Issue { Publisher = "DC Comics", Format = "Single Issue", AgeRating = "Teen", LanguageISO = "en",
                        ReleasedTime = new DateTime(2024, 10, 1) },
            new Issue { Publisher = "DC Comics", Format = "Single Issue", AgeRating = "Teen", LanguageISO = "en",
                        ReleasedTime = new DateTime(2025, 1, 1) },
            new Issue { /* nothing set - shouldn't wipe the aggregate */ });

        var f = SeriesMetaFields.FromSeries(s);

        Assert.Equal("DC Comics", f.Publisher);
        Assert.Equal("2024", f.Year);
        Assert.Equal("Single Issue", f.Format);
        Assert.Equal("Teen", f.AgeRating);
        Assert.Equal("en", f.LanguageIso);
    }

    [Fact]
    public void SeriesPublisher_TakesPrecedenceOverIssuePublisher()
    {
        var s = Series(new Issue { Publisher = "Vertigo" });
        s.Publisher = "DC Comics";
        Assert.Equal("DC Comics", SeriesMetaFields.FromSeries(s).Publisher);
    }

    [Fact]
    public void MostCommon_WinsForFormatAndRating_TiesGoToFirstSeen()
    {
        var s = Series(
            new Issue { Format = "Annual", AgeRating = "Mature" },
            new Issue { Format = "Single Issue", AgeRating = "Teen" },
            new Issue { Format = "Single Issue", AgeRating = "Teen" });

        var f = SeriesMetaFields.FromSeries(s);
        Assert.Equal("Single Issue", f.Format);
        Assert.Equal("Teen", f.AgeRating);
    }

    [Fact]
    public void Language_IsNullWhenIssuesDisagree()
    {
        var s = Series(new Issue { LanguageISO = "en" }, new Issue { LanguageISO = "ja" });
        Assert.Null(SeriesMetaFields.FromSeries(s).LanguageIso);
    }

    [Fact]
    public void EverythingBlank_YieldsAllNull()
    {
        var f = SeriesMetaFields.FromSeries(Series(new Issue(), new Issue()));
        Assert.Equal(SeriesMetaFields.Empty, f);
    }
}
