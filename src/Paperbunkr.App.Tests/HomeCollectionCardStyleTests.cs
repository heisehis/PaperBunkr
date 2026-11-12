using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Cover styles for Home collection cards (docs/superpowers/specs/2026-11-06-collection-identity-design.md): each style and each fallback.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomeCollectionCardStyleTests
{
    private static List<CollectionMember> Members(int distinctCovers)
    {
        var list = new List<CollectionMember>();
        for (int i = 1; i <= distinctCovers; i++)
        {
            var series = new Series { Id = 100 + i, Name = $"S{i}", ContentType = ContentType.Comic };
            series.Issues.Add(new Issue { Id = 1000 + i, SeriesId = series.Id, Number = "1", FilePath = $"C:\\x\\s{i}.cbz", FileSize = 10 * i });
            series.CoverIssueId = 1000 + i;
            list.Add(new CollectionMember(i, i, CollectionMemberKind.Series, series.Id, series.Name, series, null, null));
        }

        return list;
    }

    [Fact]
    public void Single_AlwaysShowsTheSingleCover()
    {
        var members = Members(4);
        var (mosaic, fan) = HomeCollectionCard.BuildStyled(CollectionCoverStyle.Single, members[0], members);
        Assert.Null(mosaic);
        Assert.Null(fan);
    }

    [Theory]
    [InlineData(CollectionCoverStyle.Auto)]
    [InlineData(CollectionCoverStyle.Mosaic)]
    public void AutoAndMosaic_NeedThreeDistinctCovers(CollectionCoverStyle style)
    {
        var two = Members(2);
        Assert.Equal((null, null), HomeCollectionCard.BuildStyled(style, two[0], two));

        var three = Members(3);
        var (mosaic, fan) = HomeCollectionCard.BuildStyled(style, three[0], three);
        Assert.NotNull(mosaic);
        Assert.Equal(4, mosaic!.Count);
        Assert.Null(fan);
    }

    [Fact]
    public void Fan_NeedsTwoCovers_AndTakesAtMostThree()
    {
        var one = Members(1);
        Assert.Equal((null, null), HomeCollectionCard.BuildStyled(CollectionCoverStyle.Fan, one[0], one));

        var two = Members(2);
        var (mosaic2, fan2) = HomeCollectionCard.BuildStyled(CollectionCoverStyle.Fan, two[0], two);
        Assert.Null(mosaic2);
        Assert.Equal(2, fan2!.Count);

        var five = Members(5);
        var (_, fan5) = HomeCollectionCard.BuildStyled(CollectionCoverStyle.Fan, five[3], five);
        Assert.Equal(3, fan5!.Count);
    }

    [Fact]
    public void NoMembers_MeansTheSingleCover()
    {
        Assert.Equal((null, null), HomeCollectionCard.BuildStyled(CollectionCoverStyle.Fan, null, null));
    }

    [Fact]
    public void ManualCoverOverridesTheStyle()
    {
        var collection = new Collection { Id = 1, Name = "C", CoverStyle = CollectionCoverStyle.Fan, IsAutoCover = false, CoverImagePath = "C:\\nope.png" };
        var members = Members(4);
        var hint = new CollectionCoverHint("C:\\nope.png", members[0]);

        var card = HomeCollectionCard.FromCollection(collection, hint, members);

        Assert.Null(card.FanCovers);
        Assert.Null(card.MosaicCovers);
    }

    [Fact]
    public void Ring_ShowsOnlyWithProgress()
    {
        var collection = new Collection { Id = 1, Name = "C" };
        var hint = new CollectionCoverHint(null, null);

        Assert.Null(HomeCollectionCard.FromCollection(collection, hint).RingFraction);
        Assert.Null(HomeCollectionCard.FromCollection(collection, hint, null, new CollectionProgress(0, 0)).RingFraction);

        var card = HomeCollectionCard.FromCollection(collection, hint, null, new CollectionProgress(1, 4));
        Assert.Equal(0.25, card.RingFraction!.Value, 6);
        Assert.Contains("25%", card.RingText);
    }

    [Fact]
    public void FanLayout_HasOneAnglePerCard_AndNeverAnEmptySlot()
    {
        Assert.Single(FanCoverStack.Layout(1));
        Assert.Equal(2, FanCoverStack.Layout(2).Count);
        Assert.Equal(3, FanCoverStack.Layout(3).Count);
        Assert.Equal(0.0, FanCoverStack.Layout(3)[^1].Angle); // the front card is upright
    }
}
