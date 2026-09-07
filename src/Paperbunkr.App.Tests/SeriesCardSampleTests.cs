using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="SeriesCardSample.ComputePanoramaWidth"/>, the pure clamp math backing
/// Panorama grid's per-cover adaptive sizing (docs/superpowers/specs/
/// 2026-08-09-library-toolbar-design.md Phase A). No Avalonia app context needed - a plain
/// <see langword="double"/> in, <see langword="double"/> out.
/// </summary>
public class SeriesCardSampleTests
{
    [Fact]
    public void ComputePanoramaWidth_SquareAspectRatio_ReturnsHeight()
    {
        double width = SeriesCardSample.ComputePanoramaWidth(1.0);

        Assert.Equal(SeriesCardSample.PanoramaHeight, width);
    }

    [Fact]
    public void ComputePanoramaWidth_LandscapeCover_WiderThanHeight()
    {
        double width = SeriesCardSample.ComputePanoramaWidth(2.0); // 2:1 landscape

        Assert.Equal(SeriesCardSample.PanoramaHeight * 2, width);
        Assert.True(width > SeriesCardSample.PanoramaHeight);
    }

    [Fact]
    public void ComputePanoramaWidth_PortraitCover_NarrowerThanHeight()
    {
        double width = SeriesCardSample.ComputePanoramaWidth(SeriesCardSample.DefaultCoverAspectRatio); // 2:3 portrait

        Assert.True(width < SeriesCardSample.PanoramaHeight);
    }

    [Fact]
    public void ComputePanoramaWidth_ExtremeWideCover_ClampsToMaxWidth()
    {
        double width = SeriesCardSample.ComputePanoramaWidth(10.0); // absurdly wide

        Assert.Equal(SeriesCardSample.PanoramaMaxWidth, width);
    }

    [Fact]
    public void ComputePanoramaWidth_ExtremeTallCover_ClampsToMinWidth()
    {
        double width = SeriesCardSample.ComputePanoramaWidth(0.05); // absurdly tall/narrow (e.g. a webtoon strip cover)

        Assert.Equal(SeriesCardSample.PanoramaMinWidth, width);
    }

    /// <summary>
    /// <see cref="SeriesCardSample.FromSeries"/>'s Publisher (docs/superpowers/specs/2026-09-06-
    /// feedback-notification-system-design.md follow-up, 2026-09-07) - real bug: it read the stale
    /// <c>Series.Publisher</c> field directly (populated once at CE-migration time only -
    /// Series.cs's own doc comment) instead of aggregating across issues via
    /// <see cref="SeriesMetaFields"/>, the same fix already applied to the Detail screen's hero
    /// badge row. This is why the Library's Publisher badge silently never showed for any series
    /// scanned/edited post-CE-migration.
    /// </summary>
    [Fact]
    public void FromSeries_Publisher_AggregatesFromIssues_NotTheStaleSeriesLevelField()
    {
        var series = new Series { Name = "Absolute Batman", Publisher = null };
        series.Issues.Add(new Issue { Id = 1, Number = "1", Publisher = "DC Comics" });
        series.Issues.Add(new Issue { Id = 2, Number = "2", Publisher = "DC Comics" });

        var card = SeriesCardSample.FromSeries(series);

        Assert.Equal("DC Comics", card.Publisher);
        Assert.True(card.HasPublisher);
    }

    [Fact]
    public void FromSeries_Publisher_UsesSeriesLevelFieldWhenExplicitlySet()
    {
        // SeriesMetaFields (reused here) treats a set series.Publisher as authoritative even over
        // per-issue values - matching its own tested precedent (SeriesPublisher_TakesPrecedenceOverIssuePublisher
        // in SeriesMetaFieldsTests.cs). A user who explicitly edited the series-level field should
        // have that win, not get silently overridden by whatever the issues happen to say.
        var series = new Series { Name = "S", Publisher = "Legacy CE Publisher" };
        series.Issues.Add(new Issue { Id = 1, Number = "1", Publisher = "Some Other Publisher" });

        var card = SeriesCardSample.FromSeries(series);

        Assert.Equal("Legacy CE Publisher", card.Publisher);
    }

    [Fact]
    public void FromSeries_Publisher_NoIssuesAndNoSeriesLevelField_IsNull()
    {
        var series = new Series { Name = "S", Publisher = null };

        var card = SeriesCardSample.FromSeries(series);

        Assert.Null(card.Publisher);
        Assert.False(card.HasPublisher);
    }
}
