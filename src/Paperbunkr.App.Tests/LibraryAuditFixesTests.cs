using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>2026-09-26 Library audit fixes - CE-parity string sorting and the singular issue count.</summary>
public class LibraryAuditFixesTests
{
    private static readonly IBrush Brush = Brushes.Gray;

    private static IssueListRow Row(string seriesName) => new() { SeriesName = seriesName, Title = "", CoverBrush = Brush };

    private static List<string> SortSeries(params string[] names)
    {
        var rows = names.Select(Row).ToList();
        var comparison = IssueListFieldCatalog.SortFields[IssueListSortField.Series].Compare;
        rows.Sort(comparison);
        return rows.Select(r => r.SeriesName).ToList();
    }

    [Fact]
    public void SeriesSort_IgnoresLeadingArticle_LikeCeSeriesComparer()
        => Assert.Equal(new[] { "Batman", "The Flash", "Superman" }, SortSeries("Superman", "The Flash", "Batman"));

    [Fact]
    public void SeriesSort_ComparesNumbersNaturally()
        => Assert.Equal(new[] { "Volume 2", "Volume 10" }, SortSeries("Volume 10", "Volume 2"));

    [Fact]
    public void SeriesSort_IsCaseInsensitive()
        => Assert.Equal(new[] { "alpha", "Bravo" }, SortSeries("Bravo", "alpha"));

    [Fact]
    public void SeriesSort_ArticleOnlyWithTrailingSpace_DoesNotThrow()
        => Assert.Equal(2, SortSeries("The ", "Batman").Count);

    [Fact]
    public void NaturalString_WithoutArticles_KeepsTheLiteralOrder()
    {
        var comparison = SortStrategies.NaturalString(r => r.SeriesName, ignoreArticles: false);
        Assert.True(comparison(Row("The Flash"), Row("Superman")) > 0);
    }

    [Theory]
    [InlineData(0, "0 issues")]
    [InlineData(1, "1 issue")]
    [InlineData(2, "2 issues")]
    public void FormatIssueCount_Singularizes(int count, string expected)
        => Assert.Equal(expected, SeriesCardSample.FormatIssueCount(count));
}
