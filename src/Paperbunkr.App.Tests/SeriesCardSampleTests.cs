using Paperbunkr.App.Models;

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
}
