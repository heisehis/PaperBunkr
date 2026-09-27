using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary><see cref="PublisherIconIndex"/> - CE icon-pack key rules
/// (docs/superpowers/specs/2026-09-25-publisher-icons-and-reader-textures-design.md §A1). Pure, no assets.</summary>
public class PublisherIconIndexTests
{
    private static string[] N(params string[] names) => names;

    [Fact]
    public void HashSeparatedAliases_AllReachTheSameFile_CaseInsensitively()
    {
        var idx = PublisherIconIndex.Build(["12bis#12 Bis.png"]);

        Assert.Equal("12bis#12 Bis.png", idx.FindUndated(N("12bis")));
        Assert.Equal("12bis#12 Bis.png", idx.FindUndated(N("12 BIS")));
    }

    [Fact]
    public void CommaSeparatedAliases_AreSplitToo()
    {
        var idx = PublisherIconIndex.Build(["Foo, Bar.png"]);

        Assert.NotNull(idx.FindUndated(N("foo")));
        Assert.NotNull(idx.FindUndated(N("bar")));
    }

    [Fact]
    public void SuffixStrippedForm_BridgesComicsAndBareNames()
    {
        var idx = PublisherIconIndex.Build(["Aftershock Comics.png", "Ablaze.png"]);

        Assert.Equal("Aftershock Comics.png", idx.FindUndated(N("aftershock")));
        Assert.Equal("Ablaze.png", idx.FindUndated(N("ablaze comics")));
    }

    [Fact]
    public void YearRange_ExpandsToEveryYearInclusive()
    {
        var idx = PublisherIconIndex.Build(["DC Comics(1977-2004)#DC(1977-2004).png", "DC Comics(2005-2010)#DC(2005-2010).png"]);

        Assert.Equal("DC Comics(1977-2004)#DC(1977-2004).png", idx.FindEra(N("dc"), 1977));
        Assert.Equal("DC Comics(1977-2004)#DC(1977-2004).png", idx.FindEra(N("DC Comics"), 2004));
        Assert.Equal("DC Comics(2005-2010)#DC(2005-2010).png", idx.FindEra(N("dc"), 2005));
        Assert.Null(idx.FindEra(N("dc"), 1976));
        Assert.Null(idx.FindEra(N("dc"), 2011));
    }

    [Fact]
    public void SingleYearKey_IsAOneYearEra()
    {
        var idx = PublisherIconIndex.Build(["Dark Horse(1990)#Dark Horse Comics(1990).png"]);

        Assert.NotNull(idx.FindEra(N("dark horse"), 1990));
        Assert.Null(idx.FindEra(N("dark horse"), 1991));
    }

    [Fact]
    public void SpacedRange_IsAccepted_WhereCeIgnoresIt()
    {
        var idx = PublisherIconIndex.Build(["Comic Kairakuten BEAST (2019 - 2021).png"]);

        Assert.NotNull(idx.FindEra(N("comic kairakuten beast"), 2020));
    }

    [Fact]
    public void MonthQualifiedRange_MatchesAtYearGranularity()
    {
        var idx = PublisherIconIndex.Build(["Foo(2005_03-2007_05).png"]);

        Assert.NotNull(idx.FindEra(N("foo"), 2006));
    }

    [Fact]
    public void SingleMonthKey_MatchesOnlyThatExactMonth_LikeCe()
    {
        var idx = PublisherIconIndex.Build(["DC(2016-2024_11).png", "DC(2024_12).png"]);

        Assert.Equal("DC(2024_12).png", idx.FindEra(N("dc"), 2024, 12));
        Assert.Equal("DC(2016-2024_11).png", idx.FindEra(N("dc"), 2024, 11));
        Assert.Equal("DC(2016-2024_11).png", idx.FindEra(N("dc"), 2024));      // year only: never the one-month key
        Assert.Null(idx.FindEra(N("dc"), 2025, 1));                            // CE gives 2025 no era logo either
    }

    [Fact]
    public void MonthQualifiedRangeEnd_StopsAtThatMonth()
    {
        var idx = PublisherIconIndex.Build(["Foo(2005_03-2007_05).png"]);

        // CE expands the range to a "(YYYY)" key for every year in it, so a month outside the
        // range but inside a covered year still falls back to the year match.
        Assert.NotNull(idx.FindEra(N("foo"), 2005, 3));
        Assert.NotNull(idx.FindEra(N("foo"), 2007, 5));
        Assert.NotNull(idx.FindEra(N("foo"), 2007, 6));
        Assert.NotNull(idx.FindEra(N("foo"), 2007));
        Assert.Null(idx.FindEra(N("foo"), 2004, 12));
        Assert.Null(idx.FindEra(N("foo"), 2008, 1));
    }

    [Fact]
    public void DatedFile_IsNotAnUndatedMatch_ButNewestEraIsTheFallback()
    {
        var idx = PublisherIconIndex.Build(["DC(1935-1940).png", "DC(2011-2016).png", "DC(1977-2004).png"]);

        Assert.Null(idx.FindUndated(N("dc")));
        Assert.Equal("DC(2011-2016).png", idx.FindNewestEra(N("dc")));
    }

    [Fact]
    public void UndatedFile_WinsOverEras_ForTheUndatedLookup()
    {
        var idx = PublisherIconIndex.Build(["Marvel.png", "Marvel(1939-1961).png"]);

        Assert.Equal("Marvel.png", idx.FindUndated(N("marvel")));
        Assert.Equal("Marvel(1939-1961).png", idx.FindEra(N("marvel"), 1950));
    }

    [Fact]
    public void MapIni_AddsAnExtraKey_ForAnExistingFile_AndIgnoresComments()
    {
        const string ini = "; This is just for AiT-Planet Lar\r\n; everything else maps with file name\r\n\r\nAiT-Planet Lar.jpg=AiT/Planet Lar\r\nMissing.png=Nope\r\n";
        var idx = PublisherIconIndex.Build(["AiT-Planet Lar.jpg"], ini);

        Assert.Equal("AiT-Planet Lar.jpg", idx.FindUndated(N("ait/planet lar")));
        Assert.Null(idx.FindUndated(N("nope")));
    }

    [Fact]
    public void NonImageFiles_AreIgnored()
    {
        var idx = PublisherIconIndex.Build(["map.ini", "Foo.png", "notes.txt"]);

        Assert.Equal(1, idx.FileCount);
    }

    [Fact]
    public void Lookup_TriesNamesInOrder_AndUnknownIsNull()
    {
        var idx = PublisherIconIndex.Build(["Second.png"]);

        Assert.Equal("Second.png", idx.FindUndated(N("first", "second")));
        Assert.Null(idx.FindUndated(N("nobody")));
        Assert.Null(idx.FindEra(N("nobody"), 2000));
        Assert.Null(idx.FindNewestEra(N("nobody")));
    }

    [Fact]
    public void DuplicateClaims_ResolveDeterministically_ByOrdinalFilename()
    {
        var idx = PublisherIconIndex.Build(["B#Same.png", "A#Same.png"]);

        Assert.Equal("A#Same.png", idx.FindUndated(N("same")));
    }
}
