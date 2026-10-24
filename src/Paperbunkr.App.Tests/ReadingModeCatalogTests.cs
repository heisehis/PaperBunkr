using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The one list of reading modes the reader, the Detail picker, the manga badge and Edit Series share.</summary>
public class ReadingModeCatalogTests
{
    [Fact]
    public void EveryReadingMode_HasExactlyOneEntry()
    {
        var modes = Enum.GetValues<ReadingMode>();

        Assert.Equal(modes.Length, ReadingModeCatalog.All.Count);
        Assert.All(modes, mode => Assert.Single(ReadingModeCatalog.All, m => m.Mode == mode));
    }

    [Fact]
    public void Labels_AreUnique_AndDescriptionsAreNeverBlank()
    {
        Assert.Equal(ReadingModeCatalog.All.Count, ReadingModeCatalog.Labels.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ReadingModeCatalog.All, m => Assert.False(string.IsNullOrWhiteSpace(m.Description)));
    }

    [Fact]
    public void ALabel_RoundTripsToItsMode_CaseInsensitively_AndUnknownTextIsNull()
    {
        Assert.All(ReadingModeCatalog.All, m => Assert.Equal(m.Mode, ReadingModeCatalog.FromLabel(m.Label)));
        Assert.Equal(ReadingMode.Webtoon, ReadingModeCatalog.FromLabel("  long strip "));
        Assert.Null(ReadingModeCatalog.FromLabel("Sideways"));
        Assert.Null(ReadingModeCatalog.FromLabel(null));
    }

    [Theory]
    [InlineData(ReadingMode.RightToLeft, true)]
    [InlineData(ReadingMode.HorizontalContinuousRightToLeft, true)]
    [InlineData(ReadingMode.LeftToRight, false)]
    [InlineData(ReadingMode.Webtoon, false)]
    [InlineData(ReadingMode.TopToBottom, false)]
    public void OnlyTheRightToLeftModes_CountAsRightToLeftForAFile(ReadingMode mode, bool expected)
    {
        Assert.Equal(expected, ReadingModeCatalog.IsRightToLeft(mode));
    }

    [Fact]
    public void TheReadersNames_AreTheCatalogsNames()
    {
        // These are the names the reader's palette and pill have always shown; the catalog must keep them.
        Assert.Equal("Left to Right", ReadingModeCatalog.LabelFor(ReadingMode.LeftToRight));
        Assert.Equal("Long Strip", ReadingModeCatalog.LabelFor(ReadingMode.Webtoon));
        Assert.Equal("Longstrip (gapped)", ReadingModeCatalog.LabelFor(ReadingMode.VerticalContinuous));
        Assert.Equal("Horizontal Long Strip (RTL)", ReadingModeCatalog.LabelFor(ReadingMode.HorizontalContinuousRightToLeft));
    }
}
